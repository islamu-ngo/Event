using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationRecognitionErasureRepository
{
    private async Task<AuthorityScope> ReadEnrollmentJurisdictionAsync(
        ModerationHostBinding host, Guid? tenantId, CancellationToken token)
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        if (tenantId is null) return instance;
        if (tenantId == Guid.Empty || context.TenantFilterTenantId != tenantId
            || !await Rows<Tenant>().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, token))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.JurisdictionUnavailable);
        return AuthorityScope.Tenant(instance, tenantId.Value);
    }

    private async Task<CaptureAuthority> AcquireContinuityAuthorityAsync(
        ModerationHostBinding host, IEnumerable<Guid> subjects, Guid? enrollmentTenantId, CancellationToken token)
    {
        // Discovery supplies only a sorted inventory. It is not an administrator assertion.
        // Match writers follow capture's native order: all User anchors, logins, grants,
        // host/principal, tenants, epochs, publication slots, secret binding, request slots.
        var sources = await Rows<ModerationRecognitionPrivacySource>()
            .Where(row => row.HostBindingId == host.Id && row.TrustDomainId == host.TrustDomainId).ToListAsync(token);
        var publications = sources.Select(row => row.PublicationId).Distinct().ToArray();
        var definitions = sources.Select(row => row.DefinitionId).Distinct().ToArray();
        var preparers = await Rows<ModerationPolicyPublicationCandidate>()
            .Where(row => publications.Contains(row.Id)).Select(row => row.PreparedByHumanId).ToArrayAsync(token);
        var publishers = await Rows<ModerationPolicyPublication>()
            .Where(row => definitions.Contains(row.Id)).Select(row => row.PrincipalId).ToArrayAsync(token);
        var subjectIds = subjects.Distinct().Order().ToArray();
        var humans = sources.SelectMany(row => new[]
        {
            row.RecordedByHumanId, row.PrivacyOwnerHumanId, row.RecoveryOwnerHumanId,
            row.PrivacyControllerHumanId, row.RecoveryControllerHumanId, row.GovernanceControllerHumanId
        }).Concat(preparers).Concat(publishers).Concat(subjectIds).Distinct().Order().ToArray();
        foreach (var human in humans)
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, human, token);
        var logins = await Rows<UserExternalLogin>().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in logins)
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var grants = await Rows<PlatformUserRole>().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in grants)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var memberships = await Rows<TenantUser>().Where(row => humans.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in memberships)
            await RelationalEntityRowFence.AcquireGlobalAsync<TenantUser>(context, id, token);
        var tenantGrants = await Rows<TenantUserRoleGrant>().Where(row => memberships.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in tenantGrants)
            await RelationalEntityRowFence.AcquireGlobalAsync<TenantUserRoleGrant>(context, grant.Id, token);
        foreach (var id in grants.Select(row => row.RoleId).Concat(tenantGrants.Select(row => row.RoleId)).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, token);
        var current = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (current is null || current.Id != host.Id || current.TrustDomainId != host.TrustDomainId
            || current.SolutionId != host.SolutionId || current.InstanceId != host.InstanceId
            || current.MaintenanceServicePrincipalId != host.MaintenanceServicePrincipalId)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, token);
        if (!await Rows<ServicePrincipal>().AnyAsync(row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        var tenants = sources.Select(row => row.TenantId)
            .Concat(enrollmentTenantId is { } tenant ? [tenant] : Array.Empty<Guid>()).Distinct().Order().ToArray();
        foreach (var id in tenants)
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, id, token);
        var epochs = await Rows<ModerationQualificationEpoch>().Where(row => row.HostBindingId == host.Id)
            .OrderBy(row => row.TenantId).ToArrayAsync(token);
        foreach (var epoch in epochs)
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, epoch.Id, token);
        foreach (var id in tenants.Append(host.InstanceId).Distinct().Order())
            _ = await RelationalNamedLock.AcquireTransactionAsync(context, $"moderation.publication:{host.Id:N}:{id:N}", token);
        var secrets = await Rows<SecretBinding>().Where(row =>
                row.SettingKey == Explore.Application.Contracts.Moderation.ModerationRecognitionKeyRing.SecretSettingKey)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in secrets)
            await RelationalEntityRowFence.AcquireGlobalAsync<SecretBinding>(context, id, token);
        foreach (var id in subjectIds)
        {
            _ = await RelationalNamedLock.AcquireTransactionAsync(context, $"moderation.recognition-erasure:{id:N}", token);
            await RequireSettledSubjectAsync(id, token);
        }
        _ = await ReadEnrollmentJurisdictionAsync(host, enrollmentTenantId, token);
        return new(host, humans);
    }

    private async Task<QualifiedContinuity?> QualifyContinuityAsync(
        CaptureAuthority authority, ModerationRecognitionCaptureItem item, CancellationToken token)
    {
        item.ValidatePersistedState();
        if (!ModerationRecognitionContinuityBinding.WithinOriginalInterval(item, clock.GetUtcNow())) return null;
        var host = authority.Host!;
        if (item.TrustDomainId != host.TrustDomainId || item.SubjectInstanceId != host.InstanceId
            || item.SubjectSolutionId != host.SolutionId)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPending>(context, item.IntentId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionCaptureItem>(context, item.Id, token);
        if (!await RequireContinuitySettlementAsync(item, token)) return null;
        var canonical = await CanonicalAsync(item.SubjectId, item.DecisionId, item.SanctionId, token)
            ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        var (decision, sanction) = canonical;
        if (!item.MatchesCurrent(decision, sanction, clock.GetUtcNow())) return null;
        await RequireOriginalContinuityCaseAsync(decision, token);
        try { if (!await HasFinalFindingAsync(decision, token)) return null; }
        catch (CaptureUnavailable)
        {
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        }
        var source = await Rows<ModerationRecognitionPrivacySource>().Include(row => row.Retention)
            .SingleOrDefaultAsync(row => row.Id == item.SourceId, token)
            ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        var definition = await ModerationPolicyPublicationGraph.Include(Rows<ModerationPolicyPublication>()
            .Where(row => row.Id == source.DefinitionId)).SingleOrDefaultAsync(token)
            ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        var profile = definition.Profiles.SingleOrDefault(row => row.ProfileId == source.ProfileId);
        if (!MatchesOriginalContinuitySource(item, source, definition, profile, host))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        var originalAdoption = await Rows<ModerationRecognitionPrivacyAdoption>()
            .SingleOrDefaultAsync(row => row.Id == item.AdoptionId && row.SourceId == source.Id, token)
            ?? throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        originalAdoption.ValidatePersistedState();
        if (originalAdoption.WithdrawnAt is not null || clock.GetUtcNow() >= originalAdoption.ValidUntil
            || clock.GetUtcNow() >= source.ValidUntil) return null;
        await RequireLatestContinuitySourceAsync(source, token);
        var adoption = await QualifySourceAsync(authority, source, definition, profile!, clock.GetUtcNow(), token);
        if (adoption is null)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
        RequireOriginalAdoption(item, source, adoption);
        foreach (var human in new[] { source.RecordedByHumanId, source.PrivacyOwnerHumanId, source.RecoveryOwnerHumanId,
            source.PrivacyControllerHumanId, source.RecoveryControllerHumanId, source.GovernanceControllerHumanId })
        {
            if (await identity.IsSubjectFencedAsync(human, token))
                throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
            await RequireSettledSubjectAsync(human, token);
        }
        return new(item, decision, sanction, ContinuityUntil(item, source, adoption));
    }

    private async Task RequireOriginalContinuityCaseAsync(ModerationDecision decision, CancellationToken token)
    {
        var original = await Rows<ModerationCase>().SingleOrDefaultAsync(row => row.Id == decision.CaseId, token);
        if (original is null || original.Subject != decision.Subject || original.Scope != decision.Scope)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
    }

    private async Task RequireLatestContinuitySourceAsync(ModerationRecognitionPrivacySource source, CancellationToken token)
    {
        var latest = await Rows<ModerationRecognitionPrivacySource>().Where(row =>
                row.HostBindingId == source.HostBindingId && row.TrustDomainId == source.TrustDomainId
                && row.TenantId == source.TenantId && row.PublicationId == source.PublicationId
                && row.ProfileId == source.ProfileId && row.ModeCode == source.ModeCode)
            .OrderByDescending(row => row.WriterGeneration).Select(row => row.Id).FirstOrDefaultAsync(token);
        if (latest != source.Id)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
    }

    private async Task<bool> RequireContinuitySettlementAsync(ModerationRecognitionCaptureItem item, CancellationToken token)
    {
        await RequireCaptureSettlementAsync(item, token);
        return await RecaptureLineageCurrentAsync(item, token);
    }

    private async Task RequireCaptureSettlementAsync(ModerationRecognitionCaptureItem item, CancellationToken token)
    {
        var pending = await Rows<ModerationRecognitionPending>().Include(row => row.Material)
            .SingleOrDefaultAsync(row => row.IntentId == item.IntentId && row.SubjectId == item.ErasureSubjectId, token);
        if (pending is null || !pending.HasCompleteMaterial || pending.AuthoritySequence is not { } sequence
            || pending.State != ModerationRecognitionPendingState.AuthorityBound
            || pending.Failure != ModerationRecognitionPendingFailure.None
            || !pending.Material.Any(row => row.Id == item.Id))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SettlementRequired);
        pending.ValidatePersistedState();
        var fact = (await erasure.ReadAfterAsync(sequence - 1, 1, token)).SingleOrDefault();
        if (fact is null || fact.AuthoritySequence != sequence || fact.IntentId != pending.IntentId
            || fact.SubjectKind != PrivacyErasureSubjectKind.User || fact.SubjectId != pending.SubjectId
            || fact.ReasonCode != pending.ReasonCode || fact.PolicyVersion != pending.PolicyVersion
            || fact.IsLegalHoldPseudonymized || fact.RecordedAtUtc < pending.ComputedAt!.Value.UtcDateTime)
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SettlementRequired);
    }

    private static bool MatchesOriginalContinuitySource(ModerationRecognitionCaptureItem item,
        ModerationRecognitionPrivacySource source, ModerationPolicyPublication definition,
        ModerationPolicyPublicationProfile? profile, ModerationHostBinding host) =>
        profile is not null && source.HostBindingId == host.Id && source.TrustDomainId == item.TrustDomainId
        && source.Id == item.SourceId && source.Revision == item.SourceRevision && source.WriterGeneration == item.SourceGeneration
        && definition.HostBindingId == host.Id && definition.TrustDomainId == host.TrustDomainId
        && definition.Jurisdiction.Contains(item.Scope) && definition.Jurisdiction.Id == item.RuleOwnerId
        && profile.RuleCode == item.RuleCode && profile.RuleRevision == item.RuleRevision
        && profile.Scopes.Any(row => row.Kind == item.ScopeKind)
        && (item.Purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
            ? profile.Recognition is ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
                or ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment
            : profile.Recognition == ModerationReferenceRecognition.SeparatelyQualifiedFinalEnrollment);

    private static void RequireOriginalAdoption(ModerationRecognitionCaptureItem item,
        ModerationRecognitionPrivacySource source, ModerationRecognitionPrivacyAdoption adoption)
    {
        var retention = source.Retention.Single(row => row.RecordClass == ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial);
        if (adoption.Id != item.AdoptionId || adoption.Revision != item.AdoptionRevision
            || adoption.WriterGeneration != item.AdoptionGeneration || source.RecordedAt > item.OriginalCapturedAt
            || adoption.AdoptedAt > item.OriginalCapturedAt
            || item.RetainUntil - item.OriginalCapturedAt > TimeSpan.FromTicks(retention.MaximumRetentionTicks))
            throw new ContinuityUnavailable(ModerationRecognitionContinuityFailure.SourceUnavailable);
    }

    private static DateTimeOffset ContinuityUntil(ModerationRecognitionCaptureItem item,
        ModerationRecognitionPrivacySource source, ModerationRecognitionPrivacyAdoption adoption) =>
        new[] { item.RetainUntil, item.EndsAt ?? item.RetainUntil,
            source.ValidUntil, adoption.ValidUntil }.Min();

    private sealed record QualifiedContinuity(ModerationRecognitionCaptureItem Item,
        ModerationDecision Decision, ModerationSanction Sanction, DateTimeOffset CurrentUntil);
}
