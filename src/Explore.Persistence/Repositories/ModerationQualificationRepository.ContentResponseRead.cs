using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    public async Task<ModerationQualificationWriteOutcome> DiscloseContentResponseStatementAsync(
        Guid tenantId, Guid responseId, Func<ReadOnlyMemory<byte>, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (!ContentResponseEnvironment(tenantId) || responseProtection is null || responseId == Guid.Empty)
            return ModerationQualificationWriteOutcome.Unavailable;
        var deliveryStarted = false;
        try
        {
            // External disclosure is single-use, never wrapped in a provider retry strategy.
            return await privacy.ExecuteSerializedAsync(async token =>
            {
                if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    return ModerationQualificationWriteOutcome.Unavailable;
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                    : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var transaction = context.Database.CurrentTransaction;
                var proof = await ReadContentResponseReviewProofAsync(tenantId, responseId, token);
                if (proof?.Response is not { } row) return ModerationQualificationWriteOutcome.NotFound;
                var plaintext = ContentResponseProtector(row.Id, row.HostBindingId, row.TenantId,
                    row.EffectId, row.NoticeId, row.CaseId, row.ContentSubjectId, row.RepresentativeHumanId,
                    row.AssignmentId, row.AssignmentRevision, row.SnapshotHash, row.RetainUntil)
                    .Unprotect(row.ProtectedStatement.ToArray());
                try
                {
                    if (plaintext.Length is < 1 or > ModerationContentResponse.MaximumStatementBytes)
                        return ModerationQualificationWriteOutcome.Unavailable;
                    var current = await ReadContentResponseReviewProofAsync(tenantId, responseId, token);
                    if (current is null) return ModerationQualificationWriteOutcome.Unavailable;
                    if (current.Response is not { } currentRow || current.Authority.UserId != proof.Authority.UserId
                        || current.Authority.Binding.Id != proof.Authority.Binding.Id
                        || current.QualificationRevision != proof.QualificationRevision
                        || current.Reviewer.Id != proof.Reviewer.Id || current.Reviewer.Revision != proof.Reviewer.Revision
                        || current.PolicySourceId != proof.PolicySourceId || current.PolicyRevision != proof.PolicyRevision
                        || current.PolicyAdoptionId != proof.PolicyAdoptionId
                        || currentRow.RequestDigest != row.RequestDigest
                        || currentRow.SubmittedAt != row.SubmittedAt || currentRow.RetainUntil != row.RetainUntil
                        || !CryptographicOperations.FixedTimeEquals(currentRow.ProtectedStatement.Span, row.ProtectedStatement.Span)
                        || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                        || context.ChangeTracker.HasChanges() || clock.GetUtcNow() < proof.ObservedAt
                        || clock.GetUtcNow() >= proof.ValidUntil || clock.GetUtcNow() >= current.ValidUntil)
                        return ModerationQualificationWriteOutcome.Unavailable;
                    token.ThrowIfCancellationRequested();
                    deliveryStarted = true;
                    await write(plaintext, token);
                    return ModerationQualificationWriteOutcome.Known;
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }, cancellationToken);
        }
        catch (AbortQualification aborted) when (!deliveryStarted) { return aborted.Outcome; }
        catch (CryptographicException) when (!deliveryStarted) { return ModerationQualificationWriteOutcome.Unavailable; }
        catch (ArgumentException) when (!deliveryStarted) { return ModerationQualificationWriteOutcome.Unavailable; }
        catch (JsonException) when (!deliveryStarted) { return ModerationQualificationWriteOutcome.Unavailable; }
        catch (DbException) when (!deliveryStarted) { return ModerationQualificationWriteOutcome.Unavailable; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private async Task<ContentResponseReviewProof?> ReadContentResponseReviewProofAsync(
        Guid tenantId, Guid responseId, CancellationToken token, bool includeStatement = true,
        IReadOnlyCollection<Guid>? representatives = null)
    {
        if (machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) is not { } user)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unauthenticated);
        if (context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || await privacy.IsSubjectFencedAsync(user, token))
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null) return null;
        context.ModerationTrustDomainId = host.TrustDomainId;
        var metadata = await context.Set<ModerationContentResponse>().AsNoTracking()
            .Where(row => row.Id == responseId && row.HostBindingId == host.Id)
            .Select(row => new
            {
                row.CaseId,
                row.EffectId,
                row.RepresentativeHumanId,
                row.SubmittedAt,
                row.RetainUntil,
                row.AssignmentId,
                row.AssignmentRevision,
                row.SnapshotHash,
                row.ContentSubjectId,
                row.TenantId,
                row.TrustDomainId
            })
            .SingleOrDefaultAsync(token);
        var observedAt = clock.GetUtcNow();
        if (metadata is null || observedAt < metadata.SubmittedAt || observedAt >= metadata.RetainUntil
            || await privacy.IsSubjectFencedAsync(metadata.RepresentativeHumanId, token))
            return null;
        var tenant = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId), tenantId);
        var authorityOwner = new ModerationQualificationAuthority(context, admin, machine);
        var authority = await authorityOwner.AcquireContentResponseReviewAsync(
            tenant, metadata.CaseId, metadata.RepresentativeHumanId, token, representatives);
        if (authority is null || authority.UserId != user || authority.Binding.Id != host.Id) return null;
        foreach (var id in authority.FencedHumans)
            if (await privacy.IsSubjectFencedAsync(id, token)) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, metadata.CaseId, token);
        var allegation = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == metadata.CaseId, token);
        if (allegation?.Subject is not { Kind: ModerationSubjectKind.Content } subject
            || subject.Origin != tenant || allegation.Scope.Kind != ModerationScopeKind.Resource
            || allegation.Scope.Parent != tenant || metadata.ContentSubjectId != subject.Id
            || metadata.TenantId != tenantId || metadata.TrustDomainId != host.TrustDomainId)
            return null;
        var effect = await context.ModerationEffectIntents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == metadata.EffectId, token);
        var decision = effect is null ? null : await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == effect.DecisionId, token);
        if (decision is null || decision.CaseId != allegation.Id
            || decision.Subject != subject || decision.Scope != allegation.Scope) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, decision.Id, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEffectIntent>(context, metadata.EffectId, token);
        var procedural = await activation.AcquireQualificationPolicyAsync(host.Id, allegation, observedAt, token);
        if (procedural is null) return null;
        var qualification = new ModerationNoticeReviewerQualification(allegation.Id, decision.Scope,
            new(procedural.Approval.ApprovedSourceId, procedural.Approval.ApprovedSourceRevision, procedural.Approval.ApprovedWriterGeneration),
            new(procedural.Current.SourceId, procedural.Current.SourceRevision, procedural.Current.WriterGeneration),
            procedural.Current.MaximumReviewerLifetime, observedAt, procedural.ValidUntil);
        var reviewers = await new ModerationNoticeRecipientRepository(context)
            .ReadReviewersAsync(decision, qualification, observedAt, token, user);
        var reviewer = reviewers.SingleOrDefault();
        if (reviewer is null || !reviewer.IsQualifiedAt(clock.GetUtcNow())
            || reviewer.ReviewerHumanId == metadata.RepresentativeHumanId
            || reviewer.ControllerHumanId == metadata.RepresentativeHumanId)
            return null;
        await using var sourceFence = await RelationalNamedLock.AcquireTransactionAsync(
            context, $"moderation.notice:{metadata.EffectId:D}", token);
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .Where(row => row.EffectId == metadata.EffectId)
            .Select(row => new
            {
                row.DecisionId,
                row.SubjectId,
                row.SubjectKind,
                row.SnapshotHash,
                row.CapturedAt,
                row.RetainUntil,
                row.StatusMetadataJson,
                row.StatusMetadataHash
            })
            .SingleOrDefaultAsync(token);
        var original = source is null ? null
            : ModerationNoticeSource.StatusMetadata.Read(source.StatusMetadataJson, source.StatusMetadataHash);
        if (source is null || source.DecisionId != decision.Id || source.SubjectKind != (int)ModerationSubjectKind.Content
            || source.SubjectId != subject.Id || source.SnapshotHash != metadata.SnapshotHash
            || source.RetainUntil != metadata.RetainUntil || source.CapturedAt > metadata.SubmittedAt
            || original is null || original.HostBindingId != host.Id || original.WriterTenantId != tenantId
            || original.CaseId != allegation.Id || original.SnapshotHash != metadata.SnapshotHash
            || !original.ContentRepresentatives.Contains(new(metadata.AssignmentId, metadata.AssignmentRevision, metadata.RepresentativeHumanId)))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContentResponse>(context, responseId, token);
        var privateReadAt = clock.GetUtcNow();
        if (privateReadAt < observedAt || privateReadAt >= metadata.RetainUntil
            || privateReadAt >= procedural.ValidUntil || !reviewer.IsQualifiedAt(privateReadAt))
            return null;
        ModerationContentResponse? response = null;
        if (includeStatement)
        {
            response = await context.Set<ModerationContentResponse>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == responseId && row.CaseId == allegation.Id
                    && row.RetainUntil > privateReadAt, token);
            if (response is null) return null;
            response.ValidatePersistedState();
            if (response.HostBindingId != host.Id || response.TrustDomainId != host.TrustDomainId
                || response.TenantId != tenantId || response.ContentSubjectId != subject.Id
                || response.EffectId != metadata.EffectId
                || response.RepresentativeHumanId != metadata.RepresentativeHumanId
                || response.SubmittedAt != metadata.SubmittedAt || response.RetainUntil != metadata.RetainUntil
                || response.AssignmentId != metadata.AssignmentId || response.AssignmentRevision != metadata.AssignmentRevision
                || response.SnapshotHash != metadata.SnapshotHash)
                return null;
        }
        var until = new[] { metadata.RetainUntil, procedural.ValidUntil, reviewer.QualifiedUntil!.Value }.Min();
        var revision = await context.ModerationQualificationEpochs.AsNoTracking()
            .Where(row => row.Id == authority.Epoch.Id).Select(row => row.Revision).SingleAsync(token);
        var now = clock.GetUtcNow();
        if (now < observedAt || now >= until || revision != authority.Epoch.Revision
            || await admin.ResolveUserIdAsync(token) != user || machine.IsMachineCaller
            || !await authorityOwner.ProviderTenantMatchesAsync(tenantId, token)
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
            || context.ModerationTrustDomainId != host.TrustDomainId)
            return null;
        return new(authority, revision, response,
            new(responseId, metadata.CaseId, metadata.EffectId, metadata.SubmittedAt, metadata.RetainUntil, until),
            reviewer, procedural.Current.SourceId,
            procedural.Current.SourceRevision, procedural.Approval.ApprovalId, observedAt, until);
    }

    private sealed record ContentResponseReviewProof(
        ModerationSourceAuthority Authority, long QualificationRevision, ModerationContentResponse? Response,
        ModerationContentResponseSummary Summary,
        ModerationReviewerAssignment Reviewer, Guid PolicySourceId, long PolicyRevision, Guid PolicyAdoptionId,
        DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
}
