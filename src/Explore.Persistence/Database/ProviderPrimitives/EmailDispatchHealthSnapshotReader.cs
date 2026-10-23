using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class EmailDispatchHealthSnapshotReader(
    IEmailDispatchAdmissionDbContextFactory factory) : IEmailDispatchHealthSnapshotReader
{
    public async Task<EmailDispatchHealthSnapshot> ReadAsync(
        DateTime now,
        DateTime processingStartedBefore,
        int tenantSampleLimit,
        CancellationToken cancellationToken)
    {
        if (tenantSampleLimit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(tenantSampleLimit));
        if (processingStartedBefore > now)
            throw new ArgumentOutOfRangeException(nameof(processingStartedBefore));

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        if (context.Database.IsNpgsql())
            return await PostgresEmailDispatchAdmissionContract.ReadHealthAsync(
                context, now, processingStartedBefore, tenantSampleLimit, cancellationToken);

        var repository = new EmailDispatchOutboxRepository(context);
        int due = await repository.CountDueDispatchAsync(now, cancellationToken);
        int retry = await repository.CountRetryScheduledAsync(cancellationToken);
        int stale = await repository.CountStaleProcessingAsync(processingStartedBefore, cancellationToken);
        int dead = await repository.CountDeadLetteredAsync(cancellationToken);
        int unknown = await repository.CountUnknownAsync(cancellationToken);
        int parked = await repository.CountParkedAsync(cancellationToken);
        DateTime? oldest = await repository.GetOldestDueCreatedAtAsync(now, cancellationToken);
        var backlog = await repository.CountDueDispatchByTenantAsync(now, tenantSampleLimit, cancellationToken);
        var state = await repository.GetProcessorState(cancellationToken);
        return new EmailDispatchHealthSnapshot(
            due, retry, stale, dead, unknown, parked, oldest,
            state?.OptionalRemindersDeferred == true, state?.IsPaused == true,
            state?.GlobalSmtpRateLimitPerMinuteOverride.HasValue == true, backlog);
    }
}
