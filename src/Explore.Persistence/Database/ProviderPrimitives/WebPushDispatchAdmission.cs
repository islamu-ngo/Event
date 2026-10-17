using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class WebPushDispatchAdmission(IWebPushDispatchAdmissionDbContextFactory admissionFactory)
    : IWebPushDispatchAdmission
{
    public async Task<IReadOnlyList<WebPushDispatchOwner>> GetPendingBatch(
        int batchSize, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        if (context.Database.IsNpgsql())
            return await PostgresWebPushDispatchAdmissionContract.DiscoverAsync(context, batchSize, now, cancellationToken);

        var rows = await new WebPushDispatchOutboxRepository(context).GetPendingBatch(batchSize, now, cancellationToken);
        return rows.Select(row => new WebPushDispatchOwner(row.TenantId, row.Id)).ToArray();
    }

    public async Task<bool> TryMarkAsProcessing(
        WebPushDispatchOwner owner, Guid leaseToken, DateTime startedAt,
        CancellationToken cancellationToken = default)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        if (context.Database.IsNpgsql())
            return await PostgresWebPushDispatchAdmissionContract.ClaimAsync(
                context, owner, leaseToken, startedAt, cancellationToken);

        return await new WebPushDispatchOutboxRepository(context)
            .TryMarkAsProcessing(owner.TenantId, owner.Id, leaseToken, startedAt, cancellationToken);
    }

    public async Task<int> RecoverStaleProcessing(
        DateTime processingStartedBefore, DateTime recoveredAt, string failureCategory,
        string errorMessage, int batchSize, CancellationToken cancellationToken = default)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        if (context.Database.IsNpgsql())
            return await PostgresWebPushDispatchAdmissionContract.RecoverAsync(
                context, processingStartedBefore, recoveredAt, failureCategory, errorMessage, batchSize, cancellationToken);

        return await new WebPushDispatchOutboxRepository(context).RecoverStaleProcessing(
            processingStartedBefore, recoveredAt, failureCategory, errorMessage, batchSize, cancellationToken);
    }

    public async Task<int> CountDueDispatchAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.IsNpgsql()
            ? await PostgresWebPushDispatchAdmissionContract.CountAsync(context, 1, now, cancellationToken)
            : await new WebPushDispatchOutboxRepository(context).CountDueDispatchAsync(now, cancellationToken);
    }

    public async Task<int> CountRetryScheduledAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.IsNpgsql()
            ? await PostgresWebPushDispatchAdmissionContract.CountAsync(context, 2, null, cancellationToken)
            : await new WebPushDispatchOutboxRepository(context).CountRetryScheduledAsync(cancellationToken);
    }

    public async Task<int> CountStaleProcessingAsync(DateTime processingStartedBefore, CancellationToken cancellationToken = default)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.IsNpgsql()
            ? await PostgresWebPushDispatchAdmissionContract.CountAsync(context, 3, processingStartedBefore, cancellationToken)
            : await new WebPushDispatchOutboxRepository(context).CountStaleProcessingAsync(processingStartedBefore, cancellationToken);
    }

    public async Task<int> CountTerminalFailureAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await admissionFactory.CreateDbContextAsync(cancellationToken);
        return context.Database.IsNpgsql()
            ? await PostgresWebPushDispatchAdmissionContract.CountAsync(context, 4, null, cancellationToken)
            : await new WebPushDispatchOutboxRepository(context).CountTerminalFailureAsync(cancellationToken);
    }
}
