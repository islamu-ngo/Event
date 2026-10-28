using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task DiscoverParticipantInvestigationHumansAsync(
        ModerationQualificationMutation? mutation, Guid hostId, Guid tenantId,
        HashSet<Guid> humans, HashSet<Guid> required, CancellationToken token)
    {
        if (mutation is not RecordModerationParticipantIncidentResponsibilitySource request) return;
        AddRequired(request.SubjectHumanId);
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.ReadGrantId && row.TenantId == tenantId, token);
        if (grant is not null)
        {
            AddRequired(grant.CallerUserId);
            AddRequired(grant.IssuerUserId);
        }
        var reporter = await context.ModerationCaseNarratives.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && row.CaseId == request.OccurrenceCaseId && row.SourceId == request.EvidenceSourceId
                && row.SourceVersion == request.EvidenceSourceVersion)
            .Select(row => (Guid?)row.ReporterUserId).SingleOrDefaultAsync(token);
        if (reporter is { } originalReporter) AddRequired(originalReporter);
        // Original allegations and investigation assertions have independent custody.
        // Both acquisition plans precede every host/tenant/epoch/target fence.
        await DiscoverCaseNarrativeHumansAsync(null, ModerationCaseTargetKind.ParticipantIncident,
            ModerationCaseNarrativePurpose.ConfidentialAllegation, hostId, tenantId, humans, token);
        await DiscoverCaseNarrativeHumansAsync(null, ModerationCaseTargetKind.ParticipantIncident,
            ModerationCaseNarrativePurpose.ParticipantInvestigation, hostId, tenantId, humans, token);
        void AddRequired(Guid human)
        {
            humans.Add(human);
            required.Add(human);
        }
    }
}
