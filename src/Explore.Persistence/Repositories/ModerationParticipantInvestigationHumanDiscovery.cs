using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Payload-free acquisition planning only; callers fence and requalify every discovered fact.</summary>
internal static class ModerationParticipantInvestigationHumanDiscovery
{
    internal static async Task<Guid[]> DiscoverAsync(ExploreDbContext context, Guid sourceId,
        Guid hostId, Guid tenantId, CancellationToken token)
    {
        var source = await context.ModerationParticipantIncidentResponsibilitySources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == sourceId && row.HostBindingId == hostId
                && row.TenantId == tenantId, token);
        if (source is null) return [];
        var humans = new HashSet<Guid> { source.SubjectId, source.AssessorHumanId, source.ReadIssuerHumanId };
        var reporter = await context.ModerationCaseNarratives.AsNoTracking()
            .Where(row => row.Id == source.OriginalNarrativeId && row.HostBindingId == hostId
                && row.TenantId == tenantId).Select(row => (Guid?)row.ReporterUserId).SingleOrDefaultAsync(token);
        if (reporter is { } reporterId) humans.Add(reporterId);
        var policies = await context.ModerationCaseNarrativePolicySources.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && row.TargetKind == ModerationCaseTargetKind.ParticipantIncident
                && (row.Purpose == ModerationCaseNarrativePurpose.ConfidentialAllegation
                    || row.Purpose == ModerationCaseNarrativePurpose.ParticipantInvestigation)
                && !context.ModerationCaseNarrativePolicySources.Any(newer =>
                    newer.HostBindingId == row.HostBindingId && newer.TenantId == row.TenantId
                    && newer.TargetKind == row.TargetKind && newer.Purpose == row.Purpose
                    && newer.WriterGeneration > row.WriterGeneration))
            .ToArrayAsync(token);
        foreach (var policy in policies)
        {
            humans.Add(policy.RecordedByHumanId);
            humans.Add(policy.Ownership.PrivacyOwnerHumanId);
            humans.Add(policy.Ownership.PrivacyControllerHumanId);
            humans.Add(policy.Ownership.GovernanceControllerHumanId);
        }
        var policyIds = policies.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.ModerationCaseNarrativeAdoptions.AsNoTracking()
            .Where(row => policyIds.Contains(row.SourceId) && row.HostBindingId == hostId
                && row.TenantId == tenantId).ToArrayAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } withdrawer) humans.Add(withdrawer);
        }
        return humans.Order().ToArray();
    }
}
