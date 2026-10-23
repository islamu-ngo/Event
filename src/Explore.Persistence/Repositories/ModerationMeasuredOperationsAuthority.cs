using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    private async Task DiscoverMeasuredOperationsHumansAsync(
        ModerationQualificationMutation? mutation, ModerationOperation? operation, Guid? publicationCandidateId,
        Guid? candidateId, Guid hostId, Guid tenantId, HashSet<Guid> humans, CancellationToken token)
    {
        if (mutation is RecordModerationMeasuredOperationsSource record)
        {
            humans.Add(record.ReviewerHumanId);
            humans.Add(record.ReviewerControllerHumanId);
            humans.Add(record.RecorderControllerHumanId);
        }
        if (mutation is not (RecordModerationMeasuredOperationsSource or AcceptModerationMeasuredOperationsSource
            or WithdrawModerationMeasuredOperationsAcceptance) && operation is null && publicationCandidateId is null) return;
        var publications = await context.ModerationPolicySelections.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId))
            .Select(row => row.ActivePublicationId).ToListAsync(token);
        if (candidateId is { } candidate) publications.Add(candidate);
        var originalAnchor = mutation switch
        {
            RecordModerationMeasuredOperationsSource source => source.OriginalEffect,
            AcceptModerationMeasuredOperationsSource source => source.OriginalEffect,
            _ => null
        };
        if (originalAnchor is not null)
        {
            var originalPublication = await context.ModerationContributionSelections.AsNoTracking()
                .Where(row => row.EffectId == originalAnchor.EffectId && row.HostBindingId == hostId && row.TenantId == tenantId)
                .Select(row => (Guid?)row.PublicationId).SingleOrDefaultAsync(token);
            if (originalPublication is { } publication) publications.Add(publication);
        }
        var acceptingSource = (mutation as AcceptModerationMeasuredOperationsSource)?.SourceId;
        Guid? withdrawnSource = null;
        if (mutation is WithdrawModerationMeasuredOperationsAcceptance withdrawal)
            withdrawnSource = await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking()
                .Where(row => row.Id == withdrawal.AcceptanceId).Select(row => (Guid?)row.SourceId).SingleOrDefaultAsync(token);
        var sources = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId
                && (publications.Contains(row.PublicationId) || row.Id == withdrawnSource || row.Id == acceptingSource)).ToListAsync(token);
        foreach (var source in sources)
        {
            humans.Add(source.RecordedByHumanId);
            humans.Add(source.ReviewerHumanId);
            humans.Add(source.ReviewerControllerHumanId);
            humans.Add(source.RecorderControllerHumanId);
        }
        var ids = sources.Select(row => row.Id).ToArray();
        var accepted = await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking()
            .Where(row => ids.Contains(row.SourceId)).ToListAsync(token);
        foreach (var acceptance in accepted)
        {
            humans.Add(acceptance.AcceptedByHumanId);
            if (acceptance.WithdrawnByHumanId is { } human) humans.Add(human);
        }
        if (mutation is WithdrawModerationMeasuredOperationsAcceptance) return;
        // Only discovery precedes the global human/role/host/tenant/epoch fence order.
        // The original evidence session still rechecks signatures, catalogue, installation,
        // current authority and each immutable request-bound source receipt.
        var challenges = await context.ModerationConsumerMeasurementChallenges.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && publications.Contains(row.PublicationId))
            .ToListAsync(token);
        foreach (var challenge in challenges) humans.Add(challenge.PreparedByHumanId);
        var challengeIds = challenges.Select(row => row.Id).ToArray();
        var receipts = await context.ModerationConsumerMeasurementReceipts.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && challengeIds.Contains(row.ChallengeId))
            .ToListAsync(token);
        foreach (var receipt in receipts) humans.Add(receipt.AcceptedByHumanId);
        var issuerIds = receipts.Select(row => row.IssuerEnrollmentId).ToArray();
        var issuers = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
            .Where(row => row.HostBindingId == hostId && row.TenantId == tenantId && issuerIds.Contains(row.Id))
            .Select(row => row.ApprovedByHumanId).ToListAsync(token);
        foreach (var human in issuers) humans.Add(human);
    }
}
