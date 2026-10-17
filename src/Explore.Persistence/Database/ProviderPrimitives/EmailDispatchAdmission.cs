using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class EmailDispatchAdmission(
    IEmailDispatchAdmissionDbContextFactory admissionFactory,
    ITenantExploreDbContextFactory tenantFactory) : IEmailDispatchAdmission
{
    public async Task<IReadOnlyList<EmailDispatchOutbox>> ClaimPendingBatchAsync(
        EmailDispatchBatchClaimRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<PostgresEmailDispatchAdmissionContract.ClaimedOwner> owners;
        await using (var admission = await admissionFactory.CreateDbContextAsync(cancellationToken))
        {
            if (!admission.Database.IsNpgsql())
            {
                var claimed = await new EmailDispatchOutboxRepository(admission)
                    .ClaimPendingBatchAsync(request, cancellationToken);
                owners = claimed.Select(row => new PostgresEmailDispatchAdmissionContract.ClaimedOwner(row.TenantId, row.Id))
                    .ToArray();
            }
            else
            {
                owners = await PostgresEmailDispatchAdmissionContract.ClaimAsync(
                    admission, request, null, null, cancellationToken);
            }
        }

        return await HydrateAsync(owners, request.LeaseToken, request.ClaimedAt, cancellationToken);
    }

    public async Task<EmailDispatchOutbox?> TryClaimSpecificAsync(
        EmailDispatchSpecificClaimRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<PostgresEmailDispatchAdmissionContract.ClaimedOwner> owners;
        await using (var admission = await admissionFactory.CreateDbContextAsync(cancellationToken))
        {
            if (!admission.Database.IsNpgsql())
            {
                var claimed = await new EmailDispatchOutboxRepository(admission)
                    .TryClaimSpecificAsync(request, cancellationToken);
                owners = claimed is null
                    ? []
                    : [new PostgresEmailDispatchAdmissionContract.ClaimedOwner(claimed.TenantId, claimed.Id)];
            }
            else
            {
                owners = await PostgresEmailDispatchAdmissionContract.ClaimAsync(
                    admission,
                    new EmailDispatchBatchClaimRequest(request.LeaseToken, 1, 1,
                        request.GlobalProcessingLimit, request.TenantProcessingLimit,
                        request.OptionalReminderBacklogHighWatermark, request.OptionalReminderBacklogLowWatermark,
                        request.ClaimedAt),
                    request.TenantId, request.PublishEventId, cancellationToken);
            }
        }

        return (await HydrateAsync(owners, request.LeaseToken, request.ClaimedAt, cancellationToken))
            .SingleOrDefault();
    }

    public async Task<EmailDispatchStaleRecoveryResult> RecoverStaleProcessing(
        EmailDispatchStaleRecoveryRequest request, CancellationToken cancellationToken)
    {
        await using var admission = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return await new EmailDispatchOutboxRepository(admission)
            .RecoverStaleProcessing(request, cancellationToken);
    }

    private async Task<IReadOnlyList<EmailDispatchOutbox>> HydrateAsync(
        IReadOnlyList<PostgresEmailDispatchAdmissionContract.ClaimedOwner> owners,
        Guid leaseToken, DateTime claimedAt, CancellationToken cancellationToken)
    {
        var rows = new List<EmailDispatchOutbox>(owners.Count);
        foreach (var tenant in owners.GroupBy(owner => owner.TenantId))
        {
            await using var runtime = await tenantFactory.CreateDbContextAsync(tenant.Key, cancellationToken);
            Guid[] ids = tenant.Select(owner => owner.OutboxId).ToArray();
            rows.AddRange(await runtime.EmailDispatchOutbox.AsNoTracking()
                .Where(row => row.TenantId == tenant.Key && ids.Contains(row.Id)
                    && row.Status == EmailDispatchStatus.Processing
                    && row.ProcessingLeaseToken == leaseToken
                    && row.ProcessingStartedAt == claimedAt)
                .ToListAsync(cancellationToken));
        }

        // A cancelled hydration leaves the committed admission recoverable; no provider
        // handoff has occurred, and recovery retains the lease/attempt fences.
        return rows;
    }
}
