using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    public async Task<ModerationPolicyPublicationCandidateResult> PrepareAsync(
        ModerationPolicyPublicationRequest request, DateTimeOffset validUntil,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Preparation owns a complete clean native unit of work.");
        if (machine.IsMachineCaller) return new(ModerationPolicyPublicationOutcome.Denied);
        if (await admin.ResolveUserIdAsync(cancellationToken) is null)
            return new(ModerationPolicyPublicationOutcome.Unauthenticated);
        if (!BoundedPreparation(request) || validUntil.Offset != TimeSpan.Zero
            || validUntil == DateTimeOffset.MinValue || validUntil == DateTimeOffset.MaxValue)
            return new(ModerationPolicyPublicationOutcome.Invalid);

        request = request with { Proposed = ModerationPolicyPublicationProjection.CanonicalProposed(request.Proposed) };
        var definitionId = Guid.CreateVersion7();
        var digest = ModerationPolicyPublicationProjection.Digest(new { Request = request, ValidUntil = validUntil });
        var stateDigest = ModerationPolicyPublicationProjection.ProposedDigest(request.Proposed);
        var authority = new ModerationQualificationAuthority(context, admin, machine);
        var priorTrust = context.ModerationTrustDomainId;
        ModerationPolicyPublicationCandidate? accepted = null;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: async token =>
                {
                    accepted = null;
                    context.ChangeTracker.Clear();
                    var current = await AcquirePreparationAuthorityAsync(token);
                    await using var slot = await AcquireSlotAsync(current.Binding.Id, request.Proposed.Jurisdiction, token);
                    var original = await CandidateGraph().SingleOrDefaultAsync(
                        row => row.OperationId == request.OperationId, token);
                    if (original is not null)
                    {
                        if (!Matches(original, current) || !await CandidateCurrentAsync(original, clock.GetUtcNow(), token))
                            throw new AbortPublication(ModerationPolicyPublicationOutcome.Conflict);
                        accepted = original;
                        return new ModerationPolicyPublicationCandidateResult(ModerationPolicyPublicationOutcome.Known, original);
                    }
                    if (current.Epoch.Revision != request.ExpectedQualificationRevision
                        || validUntil <= clock.GetUtcNow()
                        || await context.ModerationPolicyPublications.AnyAsync(
                            row => row.Id == request.Proposed.PublicationId || row.OperationId == request.OperationId, token)
                        || await context.ModerationPolicyPublicationCandidates.AnyAsync(
                            row => row.Id == request.Proposed.PublicationId, token))
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Conflict);
                    var selection = await context.ModerationPolicySelections.AsNoTracking().SingleOrDefaultAsync(
                        row => row.HostBindingId == current.Binding.Id && row.JurisdictionId == request.Proposed.Jurisdiction.Id, token);
                    if ((selection?.Revision ?? 0) != request.Proposed.ExpectedRevision
                        || selection is not null && selection.Id != request.Proposed.PolicyId
                        || selection?.LatestPublicationId != request.ExpectedPublicationId)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Conflict);
                    var prior = selection is null ? null :
                        await Graph().SingleAsync(row => row.Id == selection.LatestPublicationId, token);
                    var priorDraft = prior is null ? null : ModerationPolicyPublicationProjection.RestoreHistory(prior);
                    if (prior is not null && priorDraft is null)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                    var definitionRequest = request with
                    {
                        Proposed = request.Proposed with
                        {
                            PublicationId = definitionId,
                            RequestedState = ModerationPolicyRequestedState.Draft
                        }
                    };
                    var compilation = ModerationPolicyProposedStateCompiler.Compile(definitionRequest.Proposed,
                        new(current.Binding.Id, current.Epoch.Id, current.Epoch.Revision,
                            ModerationPolicyPublicationProjection.WriterGeneration,
                            ModerationPolicyPublicationProjection.AuthoringBounds(request.Proposed.Jurisdiction), priorDraft), inventory);
                    if (compilation.Draft is not { } draft)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Invalid);
                    var now = clock.GetUtcNow();
                    if (now >= validUntil) throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                    var definition = Snapshot(definitionRequest, draft, current, digest, now, [], definitionOnly: true);
                    var candidate = ModerationPolicyPublicationCandidate.Prepare(definition,
                        request.Proposed.PublicationId, request.OperationId, digest, stateDigest,
                        current.UserId, now, validUntil);
                    if (ModerationPolicyPublicationProjection.RestoreCandidate(candidate, inventory) is null)
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Invalid);
                    context.ModerationPolicyPublications.Add(definition);
                    context.ModerationPolicyPublicationCandidates.Add(candidate);
                    current.Epoch.Advance(request.ExpectedQualificationRevision);
                    await context.SaveChangesAsync(token);
                    if (!await authority.ProviderTenantMatchesAsync(current.Tenant.Id, token)
                        || context.ModerationTrustDomainId != current.Binding.TrustDomainId
                        || !await CandidateCurrentAsync(candidate, clock.GetUtcNow(), token))
                        throw new AbortPublication(ModerationPolicyPublicationOutcome.Unavailable);
                    accepted = candidate;
                    return new ModerationPolicyPublicationCandidateResult(ModerationPolicyPublicationOutcome.Known, candidate);
                },
                verifySucceeded: async token =>
                {
                    context.ChangeTracker.Clear();
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var current = await AcquirePreparationAuthorityAsync(token);
                    await using var slot = await AcquireSlotAsync(current.Binding.Id, request.Proposed.Jurisdiction, token);
                    var original = await CandidateGraph().SingleOrDefaultAsync(row => row.OperationId == request.OperationId, token);
                    return accepted is not null && original is not null && original.Id == accepted.Id
                        && Matches(original, current) && await CandidateCurrentAsync(original, clock.GetUtcNow(), token);
                },
                isolationLevel: IsolationLevel.Serializable, cancellationToken: cancellationToken);
        }
        catch (AbortPublication aborted) { return new(aborted.Outcome); }
        catch (DbUpdateConcurrencyException) { return new(ModerationPolicyPublicationOutcome.Conflict); }
        catch (DbUpdateException error) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(error))
        { return new(ModerationPolicyPublicationOutcome.Conflict); }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = priorTrust;
        }

        async Task<ModerationSourceAuthority> AcquirePreparationAuthorityAsync(CancellationToken token)
        {
            var current = await authority.AcquireAsync(request.Proposed.Jurisdiction, token)
                ?? throw new AbortPublication(ModerationPolicyPublicationOutcome.Denied);
            if (current.Binding.Id != request.ExpectedHostBindingId || current.Epoch.Id != request.ExpectedAuthoritySourceId)
                throw new AbortPublication(ModerationPolicyPublicationOutcome.Conflict);
            return current;
        }

        bool Matches(ModerationPolicyPublicationCandidate candidate, ModerationSourceAuthority current) =>
            candidate.Id == request.Proposed.PublicationId && candidate.PolicyId == request.Proposed.PolicyId
            && candidate.RequestDigest == digest && candidate.ProposedStateDigest == stateDigest
            && candidate.PreparedByHumanId == current.UserId && candidate.HostBindingId == current.Binding.Id
            && candidate.TrustDomainId == current.Binding.TrustDomainId && candidate.TenantId == current.Tenant.Id
            && candidate.AuthoritySourceId == current.Epoch.Id
            && candidate.AuthorityRevision == request.ExpectedQualificationRevision
            && candidate.ExpectedRevision == request.Proposed.ExpectedRevision
            && candidate.PreviousPublicationId == request.ExpectedPublicationId
            && candidate.Jurisdiction == request.Proposed.Jurisdiction && candidate.ValidUntil == validUntil;
    }

    public Task<ModerationPolicyPublicationCandidate?> AcquireCandidateAsync(
        Guid transactionId, Guid hostBindingId, AuthorityScope jurisdiction, Guid policyId,
        Guid publicationId, DateTimeOffset observedAt, CancellationToken cancellationToken = default) =>
        definitions.AcquireCandidateAsync(transactionId, hostBindingId, jurisdiction, policyId, publicationId, observedAt, cancellationToken);

    private IQueryable<ModerationPolicyPublicationCandidate> CandidateGraph() => definitions.CandidateGraph();

    private Task<bool> CandidateCurrentAsync(
        ModerationPolicyPublicationCandidate candidate, DateTimeOffset now, CancellationToken token,
        bool published = false) => definitions.CandidateCurrentAsync(candidate, now, token, published);

    private static bool BoundedPreparation(ModerationPolicyPublicationRequest request)
    {
        var proposed = request.Proposed;
        return Version7(request.OperationId) && Version7(request.ExpectedHostBindingId)
            && Version7(request.ExpectedAuthoritySourceId) && request.ExpectedQualificationRevision is > 0 and < long.MaxValue - 1
            && request.ExpectedWriterGeneration == ModerationPolicyPublicationProjection.WriterGeneration
            && proposed is not null && proposed.RequestedState == ModerationPolicyRequestedState.Active
            && Version7(proposed.PolicyId) && Version7(proposed.PublicationId)
            && proposed.ExpectedRevision is >= 0 and < long.MaxValue
            && (proposed.ExpectedRevision == 0 ? request.ExpectedPublicationId is null
                : request.ExpectedPublicationId is { } previous && Version7(previous))
            && proposed.Jurisdiction?.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant
            && proposed.Classes is { IsDefaultOrEmpty: false, Length: <= 256 }
            && proposed.Reasons is { IsDefaultOrEmpty: false, Length: <= 256 }
            && proposed.Profiles is { IsDefaultOrEmpty: false, Length: <= 256 }
            && proposed.Modes is { IsDefaultOrEmpty: false, Length: <= 2 }
            && proposed.SupportedProfiles is { IsDefault: false, Length: <= 256 }
            && proposed.Classes.All(row => row is not null && row.Reference is not null && row.Meaning is { Length: > 0 and <= 4000 })
            && proposed.Reasons.All(row => row is not null && row.Reference is not null && row.Class is not null
                && row.Meaning is { Length: > 0 and <= 4000 })
            && proposed.Profiles.All(row => row is not null && row.ScopeKinds is { IsDefault: false, Length: <= 6 }
                && row.SubjectKinds is { IsDefault: false, Length: <= 4 }
                && row.Effects is { IsDefault: false, Length: <= 26 } && row.Strategies is { IsDefault: false, Length: <= 3 })
            && proposed.InventoryFingerprint is { Length: > 0 and <= 64 };
    }
}
