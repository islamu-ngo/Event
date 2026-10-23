using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class NotificationFanoutAdmission(
    INotificationFanoutAdmissionDbContextFactory admissionFactory,
    ITenantExploreDbContextFactory tenantFactory) : INotificationFanoutAdmission
{
    public async Task<NotificationFanoutClaimRoundResult> ClaimDueRoundAsync(
        NotificationFanoutClaimRoundRequest request,
        CancellationToken cancellationToken)
    {
        PostgresNotificationFanoutAdmissionContract.AdmittedRound admitted;
        await using (var global = await admissionFactory.CreateDbContextAsync(cancellationToken))
        {
            if (!global.Database.IsNpgsql())
            {
                return await new NotificationFanoutRunRepository(global)
                    .ClaimDueRoundAsync(request, cancellationToken);
            }

            admitted = await PostgresNotificationFanoutAdmissionContract.ClaimAsync(
                global, request, cancellationToken);
        }

        var claims = new List<NotificationFanoutClaim>(admitted.Owners.Count);
        int unavailable = admitted.UnavailableCount;
        foreach (var owner in admitted.Owners)
        {
            await using var runtime = await tenantFactory.CreateDbContextAsync(owner.TenantId, cancellationToken);
            var run = await runtime.NotificationFanoutRuns.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TenantId == owner.TenantId
                    && row.Id == owner.RunId && row.FanoutOccurrenceId == owner.OccurrenceId
                    && row.Status == "processing" && row.ProcessingLeaseToken == owner.LeaseToken
                    && row.ProcessingFence == owner.Fence && row.ProcessingGeneration == owner.Generation,
                    cancellationToken);
            if (run is null)
            {
                unavailable++;
                continue;
            }

            NotificationFanoutAudienceCursor? cursor = run.CursorFirstEligibleRegistrationCreatedAt.HasValue
                && run.CursorUserId.HasValue
                ? new NotificationFanoutAudienceCursor(run.CursorFirstEligibleRegistrationCreatedAt.Value,
                    run.CursorUserId.Value)
                : null;
            claims.Add(new NotificationFanoutClaim(run.Id, run.TenantId, owner.OccurrenceId,
                owner.LeaseToken, owner.Fence, owner.Generation, cursor));
        }

        // Cancellation after admission leaves a fenced, expiring claim; no recipient
        // materialization has begun, so recovery resumes its persisted cursor.
        return new NotificationFanoutClaimRoundResult(claims, admitted.CandidateCount,
            admitted.LeaseContentionCount, admitted.CapacityDeferredCount, unavailable);
    }

    public async Task<NotificationFanoutProcessorSnapshot> GetProcessorSnapshotAsync(
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        await using var global = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return global.Database.IsNpgsql()
            ? await PostgresNotificationFanoutAdmissionContract.SnapshotAsync(
                global, observedAt, cancellationToken)
            : await new NotificationFanoutRunRepository(global)
                .GetProcessorSnapshotAsync(observedAt, cancellationToken);
    }
}
