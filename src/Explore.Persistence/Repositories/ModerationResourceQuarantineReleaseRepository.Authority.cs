using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationResourceQuarantineReleaseRepository
{
    // Ordering shared with native qualification writers: human/login/role rows, host/service,
    // tenant/status/epoch, case/procedural/reviewer/evidence, native ancestry, operational cause.
    // Discovery below reads opaque coordinates only. Every discovered source is reread after fencing.
    private async Task<ModerationSourceAuthority> AcquireHumansAsync(
        Guid resourceId, Guid? caseId, IReadOnlyList<EventResourceModerationQuarantineReview> chain,
        Guid? assignmentId, CancellationToken token)
    {
        if (machine.IsMachineCaller || context.TenantFilterTenantId is not { } tenantId
            || await admin.ResolveUserIdAsync(token) is not { } userId)
            throw new SafetyUnavailable();
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new SafetyUnavailable();
        context.ModerationTrustDomainId = host.TrustDomainId;
        var ids = caseId is { } freshCase
            ? (await DiscoverResourceSafetyHumansAsync(
                resourceId, freshCase, assignmentId, chain, token, includeMaterialHumans: false)).ToHashSet()
            : new HashSet<Guid> { userId };
        if (ReferenceEquals(_resourceSafetyTransaction, context.Database.CurrentTransaction)
            && _resourceSafetySessions.Values.Any(session => !ids.SetEquals(session.Authority.FencedHumans)))
            throw new SafetyUnavailable();
        foreach (var id in ids.Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        var humans = ids.ToArray();
        if (await context.Users.AsNoTracking().CountAsync(row => humans.Contains(row.Id) && !row.IsDeleted, token) != humans.Length
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => humans.Contains(row.SubjectId), token))
            throw new SafetyUnavailable();
        var logins = await context.UserExternalLogins.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var id in logins) await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var row in platform) await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, row.Id, token);
        var members = await context.TenantUsers.AsNoTracking()
            .Where(row => humans.Contains(row.UserId) && row.TenantId == tenantId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToListAsync(token);
        foreach (var id in members) await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row => members.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var row in grants) await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenantId, value => value.Id, row.Id, token);
        foreach (var id in platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId)).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId),
            host.SolutionId), host.InstanceId);
        var scope = AuthorityScope.Resource(AuthorityScope.Tenant(instance, tenantId), resourceId);
        var native = await new ModerationQualificationAuthority(context, admin, machine).AcquireAsync(scope, token);
        if (native is null || native.Binding.Id != host.Id || native.UserId != userId)
            throw new SafetyUnavailable();
        var platformIds = platform.Select(row => row.Id).ToArray();
        var administrators = await context.PlatformUserRoles.AsNoTracking().Where(row =>
            platformIds.Contains(row.Id) && !row.User.IsDeleted
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
            .Select(row => row.UserId).ToListAsync(token);
        var grantIds = grants.Select(row => row.Id).ToArray();
        administrators.AddRange(await context.TenantUserRoleGrants.AsNoTracking().Where(row =>
            grantIds.Contains(row.Id) && row.TenantId == tenantId && row.RevokedAt == null
            && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
            && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
            && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted)
            .Select(row => row.TenantUser.UserId).ToListAsync(token));
        return native with { FencedHumans = humans, Administrators = administrators.Distinct().ToArray() };
    }

    private async Task<QualifiedSafety> QualifyAsync(
        Guid quarantineId, Guid resourceId, Guid materialId, Guid caseId, long caseRevision,
        IReadOnlyList<EventResourceModerationQuarantineReview> chain, Guid? assignmentId,
        DateTimeOffset now, object operation, CancellationToken token,
        Guid? ownReleasedStamp = null, Guid? ownWithdrawalId = null)
    {
        var authority = await AcquireHumansAsync(resourceId, caseId, chain, assignmentId, token);
        var scope = AuthorityScope.Resource(authority.Tenant, resourceId);
        foreach (var row in chain.OrderBy(row => row.Id))
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantineReview>(context, row.Id, token);
            var current = await context.Set<EventResourceModerationQuarantineReview>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == row.Id && value.TenantId == authority.Tenant.Id, token);
            if (current is null || Digest(current) != Digest(row)) throw new SafetyUnavailable();
        }
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, caseId, token);
        var allegation = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == caseId, token)
            ?? throw new SafetyUnavailable();
        allegation.ValidatePersistedState();
        if (allegation.Revision != caseRevision || allegation.State != ModerationCaseState.Open
            || allegation.Subject is not { Kind: ModerationSubjectKind.Content } subject
            || subject.Origin != authority.Tenant || allegation.UpdatedAt > now
            || (allegation.Scope == authority.Tenant
                ? allegation.AttributionSourceId is null
                : allegation.Scope != scope || allegation.AttributionSourceId is not null))
            throw new SafetyUnavailable();
        await using var policySlot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.qualification-policy:{authority.Binding.Id:N}:{caseId:N}", token);
        var policy = await context.ModerationQualificationPolicySources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == authority.Binding.Id && row.CaseId == caseId, token)
            ?? throw new SafetyUnavailable();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationPolicySource>(context, policy.Id, token);
        policy = await context.ModerationQualificationPolicySources.AsNoTracking().SingleAsync(row => row.Id == policy.Id, token);
        var adoption = await context.ModerationQualificationPolicyAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SourceId == policy.Id && row.SourceRevision == policy.Revision, token)
            ?? throw new SafetyUnavailable();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationPolicyAdoption>(context, adoption.Id, token);
        adoption = await context.ModerationQualificationPolicyAdoptions.AsNoTracking().SingleAsync(row => row.Id == adoption.Id, token);
        if (!adoption.MatchesCurrent(policy, caseRevision, now) || policy.TenantId != authority.Tenant.Id
            || policy.TrustDomainId != authority.Binding.TrustDomainId
            || !authority.Administrators.Contains(policy.PublishedByHumanId)
            || !authority.Administrators.Contains(adoption.AdoptedByHumanId))
            throw new SafetyUnavailable();
        await ProveAsync(ModerationQualificationSourceKind.QualificationPolicy, policy.Id, policy.Revision,
            policy.PublishedByHumanId, policy.PublishedAt, authority, receipt => new PublishModerationQualificationPolicySource
            {
                OperationId = receipt.Id, Target = allegation.Scope, ExpectedQualificationRevision = receipt.QualificationRevision - 1,
                SourceId = policy.Id, CaseId = caseId, ExpectedCaseRevision = caseRevision,
                ExpectedSourceRevision = policy.Revision - 1, MaximumReviewerLifetime = policy.MaximumReviewerLifetime,
                MaximumUrgencyLifetime = policy.MaximumUrgencyLifetime, ValidUntil = policy.ValidUntil
            }, token);
        await ProveAsync(ModerationQualificationSourceKind.QualificationPolicyAdoption, adoption.Id, 1,
            adoption.AdoptedByHumanId, adoption.AdoptedAt, authority, receipt => new AdoptModerationQualificationPolicySource
            {
                OperationId = receipt.Id, Target = allegation.Scope, ExpectedQualificationRevision = receipt.QualificationRevision - 1,
                SourceId = policy.Id, ExpectedSourceRevision = policy.Revision, ExpectedWriterGeneration = policy.WriterGeneration,
                AdoptionId = adoption.Id, CaseId = caseId, ExpectedCaseRevision = caseRevision, ValidUntil = adoption.ValidUntil
            }, token);
        var until = policy.ValidUntil < adoption.ValidUntil ? policy.ValidUntil : adoption.ValidUntil;
        var reviewers = new Dictionary<Guid, ModerationReviewerAssignment>();
        foreach (var id in chain.Select(row => row.ReviewerAssignmentId)
                     .Concat(assignmentId is { } requested ? [requested] : Array.Empty<Guid>()).Distinct().Order())
        {
            var reviewer = await ReadReviewerAsync(id, allegation, policy, authority, now, token);
            reviewers.Add(id, reviewer);
            if (reviewer.QualifiedUntil!.Value < until) until = reviewer.QualifiedUntil.Value;
        }
        foreach (var row in chain)
        {
            row.ValidatePersistedState();
            var reviewer = reviewers[row.ReviewerAssignmentId];
            if (row.HostBindingId != authority.Binding.Id || row.TrustDomainId != authority.Binding.TrustDomainId
                || row.CaseId != caseId || row.CaseRevision != caseRevision || row.RecordedAt > now
                || row.ValidUntil <= now || row.QualificationRevision > authority.Epoch.Revision
                || row.HumanId != reviewer.ReviewerHumanId || row.ControllerHumanId != reviewer.ControllerHumanId
                || row.ReviewerRevision != reviewer.Revision)
                throw new SafetyUnavailable();
            if (row.ValidUntil < until) until = row.ValidUntil;
        }
        var native = await ModerationResourceGenerationReader.AcquireAsync(context, quarantineId, resourceId,
            authority.Binding.Id, authority.Binding.TrustDomainId, authority.Tenant.Id, 1, authority.UserId,
            authority.Epoch.Revision, now, until, token) ?? throw new SafetyUnavailable();
        await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceModerationQuarantine>(context, quarantineId, token);
        var cause = await context.EventResourceModerationQuarantines.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == quarantineId && row.TenantId == authority.Tenant.Id, token)
            ?? throw new SafetyUnavailable();
        cause.ValidatePersistedState();
        if (native.MaterialVersionId != materialId || native.EventId != subject.Id
            || cause.EventId != native.EventId || cause.EventResourceId != resourceId
            || cause.MaterialVersionId != materialId || cause.AppliedAt > now
            || chain.Any(row => !row.Owns(cause)
                || row.NativeDigest != NativeDigest(native, ownReleasedStamp is null ? null : row.ResourceStamp))
            || ownReleasedStamp is { } committedStamp && native.ResourceStamp != committedStamp)
            throw new SafetyUnavailable();
        if (chain.Count != 0)
        {
            var purpose = chain[0];
            if (await context.Set<EventResourceModerationQuarantineReview>().AsNoTracking()
                .AnyAsync(row => row.TenantId == authority.Tenant.Id && row.PurposeId == purpose.Id
                    && row.Step == ResourceSafetyReviewStep.WithdrawPurpose && row.Id != ownWithdrawalId, token))
                throw new SafetyUnavailable();
        }
        ResourceSafetyProvenanceSession? provenance = null;
        ModerationCaseTargetProofOwner.ResourceSafetyAttribution? attribution = null;
        if (allegation.Scope == authority.Tenant)
        {
            provenance = OpenResourceSafetyProvenance(operation, authority, allegation, cause, native,
                policy, adoption, reviewers, chain, assignmentId, now, until,
                ownReleasedStamp is null ? native.ResourceStamp : chain[0].ResourceStamp,
                ownWithdrawalId);
            attribution = await ModerationCaseTargetProofOwner.AcquireResourceSafetyAttributionAsync(
                context, provenance, token) ?? throw new SafetyUnavailable();
            if (attribution.ValidUntil < until) until = attribution.ValidUntil;
        }
        if (clock.GetUtcNow() < now || clock.GetUtcNow() >= until) throw new SafetyUnavailable();
        return new(authority, cause, native, reviewers, until, provenance, attribution);
    }

    private async Task<ModerationReviewerAssignment> ReadReviewerAsync(
        Guid id, ModerationCase allegation, ModerationQualificationPolicySource policy,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationReviewerAssignment>(context, id, token);
        var reviewer = await context.ModerationReviewerAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new SafetyUnavailable();
        reviewer.ValidatePersistedState();
        if (reviewer.CaseId != allegation.Id || reviewer.TrustDomainId != authority.Binding.TrustDomainId
            || reviewer.CaseTenantId != authority.Tenant.Id || reviewer.SubjectHumanId is not null
            || !reviewer.IsQualifiedAt(now)
            || !authority.Administrators.Contains(reviewer.ReviewerHumanId)
            || !authority.Administrators.Contains(reviewer.AssignedByHumanId)
            || !authority.FencedHumans.Contains(reviewer.ControllerHumanId!.Value)
            || !authority.Administrators.Contains(reviewer.QualifiedByHumanId!.Value)
            || !authority.Administrators.Contains(reviewer.AssessedByHumanId!.Value)
            || await context.ModerationReviewerAssignments.AsNoTracking().CountAsync(row =>
                row.CaseId == allegation.Id && row.ReviewerHumanId == reviewer.ReviewerHumanId
                && row.WithdrawnAt == null, token) != 1)
            throw new SafetyUnavailable();
        await ProveAsync(ModerationQualificationSourceKind.Reviewer, reviewer.Id, 1,
            reviewer.AssignedByHumanId, reviewer.CreatedAt, authority, receipt => new AssignModerationReviewerSource
            {
                OperationId = receipt.Id, Target = allegation.Scope, ExpectedQualificationRevision = receipt.QualificationRevision - 1,
                AssignmentId = reviewer.Id, CaseId = allegation.Id, ExpectedCaseRevision = allegation.Revision,
                ReviewerHumanId = reviewer.ReviewerHumanId
            }, token);
        var identityId = reviewer.IdentityEvidenceId!.Value;
        var conflictId = reviewer.ConflictEvidenceId!.Value;
        foreach (var evidenceId in new[] { identityId, conflictId }.Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, evidenceId, token);
        var identity = await context.ModerationQualificationEvidence.AsNoTracking().SingleAsync(row => row.Id == identityId, token);
        var conflict = await context.ModerationQualificationEvidence.AsNoTracking().SingleAsync(row => row.Id == conflictId, token);
        identity.ValidatePersistedState();
        conflict.ValidatePersistedState();
        if (identity.Purpose != ModerationQualificationEvidencePurpose.Controller
            || conflict.Purpose != ModerationQualificationEvidencePurpose.Conflict
            || conflict.ConflictState != (int)ModerationReviewerConflictState.Independent
            || identity.AssertedByHumanId != reviewer.QualifiedByHumanId
            || conflict.AssertedByHumanId != reviewer.AssessedByHumanId || identity.RecordedAt > conflict.RecordedAt
            || conflict.RecordedAt != reviewer.UpdatedAt
            || new[] { identity, conflict }.Any(row => row.CaseId != allegation.Id || row.TenantId != authority.Tenant.Id
                || row.TrustDomainId != authority.Binding.TrustDomainId || row.ReviewerHumanId != reviewer.ReviewerHumanId
                || row.ControllerHumanId != reviewer.ControllerHumanId || row.ValidUntil != reviewer.QualifiedUntil
                || row.RecordedAt > now || row.ValidUntil <= now
                || row.ValidUntil - row.RecordedAt > policy.MaximumReviewerLifetime
                || row.AssertedByHumanId == reviewer.ReviewerHumanId || row.AssertedByHumanId == reviewer.ControllerHumanId))
            throw new SafetyUnavailable();
        await ProveAsync(ModerationQualificationSourceKind.Reviewer, reviewer.Id, null,
            identity.AssertedByHumanId, identity.RecordedAt, authority, receipt => new QualifyModerationReviewerController
            {
                OperationId = receipt.Id, Target = allegation.Scope, ExpectedQualificationRevision = receipt.QualificationRevision - 1,
                AssignmentId = reviewer.Id, ExpectedAssignmentRevision = receipt.ResultRevision - 1,
                ControllerHumanId = reviewer.ControllerHumanId.Value, EvidenceId = identity.Id,
                Justification = identity.Justification, QualifiedUntil = identity.ValidUntil!.Value
            }, token);
        await ProveAsync(ModerationQualificationSourceKind.Reviewer, reviewer.Id, reviewer.Revision,
            conflict.AssertedByHumanId, conflict.RecordedAt, authority, receipt => new AssessModerationReviewerConflict
            {
                OperationId = receipt.Id, Target = allegation.Scope, ExpectedQualificationRevision = receipt.QualificationRevision - 1,
                AssignmentId = reviewer.Id, ExpectedAssignmentRevision = receipt.ResultRevision - 1,
                State = ModerationReviewerConflictState.Independent, EvidenceId = conflict.Id, Justification = conflict.Justification
            }, token);
        return reviewer;
    }

    private async Task ProveAsync(
        ModerationQualificationSourceKind kind, Guid id, long? revision, Guid human, DateTimeOffset recordedAt,
        ModerationSourceAuthority authority, Func<ModerationQualificationMutationReceipt, ModerationQualificationMutation> reconstruct,
        CancellationToken token)
    {
        var receipts = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceKind == kind && row.SourceId == id && (revision == null || row.ResultRevision == revision)
                && row.PrincipalId == human && row.CompletedAt == recordedAt).ToListAsync(token);
        foreach (var discovered in receipts)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, discovered.Id, token);
            var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
            if (receipt is null || receipt.SourceKind != kind || receipt.SourceId != id
                || revision is { } expected && receipt.ResultRevision != expected
                || receipt.PrincipalId != human || receipt.CompletedAt != recordedAt)
                continue;
            receipt.ValidatePersistedState();
            if (receipt.HostBindingId == authority.Binding.Id && receipt.TrustDomainId == authority.Binding.TrustDomainId
                && receipt.TenantId == authority.Tenant.Id && receipt.QualificationRevision <= authority.Epoch.Revision
                && receipt.RequestDigest == Digest(reconstruct(receipt)))
                return;
        }
        throw new SafetyUnavailable();
    }

    private static string Digest(object request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));

    private static string NativeDigest(ModerationResourceGeneration native, Guid? originalStamp = null) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("event.resource-safety-material.v1\n"
            + JsonSerializer.Serialize(new
            {
                native.HostBindingId, native.TrustDomainId, native.TenantId, native.ResourceId, native.MaterialVersionId,
                ResourceStamp = originalStamp ?? native.ResourceStamp, native.EventId, native.EventStamp, native.ActorId,
                native.ActorStamp, native.OrganizerActorId, native.OrganizerActorStamp, native.DeliveryType,
                native.StorageObjectId, native.StorageObjectStamp, native.StorageProviderBindingId, native.StorageProvider
            }))));

    private sealed record QualifiedSafety(
        ModerationSourceAuthority Authority, EventResourceModerationQuarantine Cause,
        ModerationResourceGeneration Native, IReadOnlyDictionary<Guid, ModerationReviewerAssignment> Reviewers,
        DateTimeOffset ValidUntil, ResourceSafetyProvenanceSession? Provenance,
        ModerationCaseTargetProofOwner.ResourceSafetyAttribution? Attribution);
}
