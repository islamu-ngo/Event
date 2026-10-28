using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    internal Task<ModerationSourceAuthority?> AcquireCaseNarrativeAsync(
        AuthorityScope target, ModerationCaseTargetKind kind, CancellationToken token,
        ModerationCaseNarrativePurpose purpose = ModerationCaseNarrativePurpose.ConfidentialAllegation,
        ModerationCaseReadSourcePlan? protectedRead = null) =>
        AcquireAsync(target, false, false, null, null, token, narrativeKind: kind, narrativePurpose: purpose,
            protectedRead: protectedRead);

    private async Task DiscoverCaseNarrativeHumansAsync(ModerationQualificationMutation? mutation,
        ModerationCaseTargetKind? kind, ModerationCaseNarrativePurpose purpose,
        Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is RecordModerationCaseNarrativePolicySource recording)
        {
            AddOwners(recording.Ownership);
            kind = recording.TargetKind;
            purpose = recording.Terms.Purpose;
        }
        Guid? sourceId = (mutation as AdoptModerationCaseNarrativePolicySource)?.SourceId;
        if (mutation is WithdrawModerationCaseNarrativeAdoption withdrawal)
            sourceId = await context.Set<ModerationCaseNarrativeAdoption>().AsNoTracking()
                .Where(row => row.Id == withdrawal.AdoptionId && row.HostBindingId == hostId && row.TenantId == tenantId)
                .Select(row => (Guid?)row.SourceId).SingleOrDefaultAsync(token);
        if (kind is null && sourceId is null) return;
        var sources = await context.Set<ModerationCaseNarrativePolicySource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (row.TargetKind == kind && row.Purpose == purpose || row.Id == sourceId)).ToListAsync(token);
        foreach (var source in sources)
        {
            humans.Add(source.RecordedByHumanId);
            AddOwners(source.Ownership);
        }
        var ids = sources.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.Set<ModerationCaseNarrativeAdoption>().AsNoTracking()
            .Where(row => ids.Contains(row.SourceId) && row.HostBindingId == hostId && row.TenantId == tenantId).ToListAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
        }
        void AddOwners(ModerationCaseNarrativeOwnership ownership)
        {
            humans.Add(ownership.PrivacyOwnerHumanId);
            humans.Add(ownership.PrivacyControllerHumanId);
            humans.Add(ownership.GovernanceControllerHumanId);
        }
    }
}
