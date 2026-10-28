using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task DiscoverOriginalMeasurementHumansAsync(ModerationOriginalEffectAnchor anchor,
        Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        // Metadata discovery only: no private notice payload before retained privacy qualification.
        var selection = await context.ModerationContributionSelections.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == anchor.EffectId
                && row.HostBindingId == hostId && row.TenantId == tenantId, token);
        if (selection is null) return;
        humans.Add(selection.AuthoredByHumanId);
        var publisher = await context.ModerationPolicyPublications.AsNoTracking()
            .Where(row => row.Id == selection.PublicationId && row.HostBindingId == hostId && row.TenantId == tenantId)
            .Select(row => (Guid?)row.PrincipalId).SingleOrDefaultAsync(token);
        if (publisher is { } publishedBy) humans.Add(publishedBy);
        var preparer = await context.ModerationPolicyPublicationCandidates.AsNoTracking()
            .Where(row => row.Id == selection.PublicationId && row.HostBindingId == hostId && row.TenantId == tenantId)
            .Select(row => (Guid?)row.PreparedByHumanId).SingleOrDefaultAsync(token);
        if (preparer is { } preparedBy) humans.Add(preparedBy);
        var adoption = await context.ModerationProfileAdoptions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SelectionId == selection.Id, token);
        if (adoption is not null) humans.Add(adoption.AdoptedByHumanId);
        var disclosure = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == anchor.EffectId, token);
        if (disclosure is not null)
        {
            humans.Add(disclosure.ApprovedByHumanId);
            foreach (var representative in disclosure.Intent.ContentRepresentatives)
                humans.Add(representative.HumanId);
        }
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == selection.DecisionId, token);
        if (decision is not null)
        {
            humans.Add(decision.IssuerId);
            foreach (var approval in decision.Approvals) humans.Add(approval.Id);
        }
        foreach (var assignment in await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => row.CaseId == selection.CaseId).ToListAsync(token))
        {
            humans.Add(assignment.ReviewerHumanId);
            foreach (var human in new[] { assignment.ControllerHumanId, assignment.QualifiedByHumanId,
                assignment.AssessedByHumanId }.OfType<Guid>()) humans.Add(human);
        }
    }
}
