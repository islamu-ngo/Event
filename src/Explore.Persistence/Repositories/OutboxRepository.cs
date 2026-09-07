using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public class OutboxRepository : GenericRepository<OutboxMessage, Guid>,
    IOutboxRepository,
    IConfigurationManifestEffectOutboxRepository,
    IConfigurationImportEffectOutboxRepository
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);
    private readonly ExploreDbContext _dbContext;

    public OutboxRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public new async Task<OutboxMessage> Create(OutboxMessage message)
    {
        await _dbContext.OutboxMessages.AddAsync(message);
        await _dbContext.SaveChangesAsync();
        return message;
    }

    public async Task<IReadOnlyList<OutboxMessage>> CreateRange(
        IReadOnlyCollection<OutboxMessage> messages,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        await _dbContext.OutboxMessages.AddRangeAsync(messages, ct);
        await _dbContext.SaveChangesAsync(ct);
        return messages.ToArray();
    }

    public async Task<List<OutboxMessage>> GetPendingBatch(int batchSize, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => (m.Status == OutboxMessageStatus.Pending
                         && (m.NextRetryAt == null || m.NextRetryAt <= now))
                        || (m.Status == OutboxMessageStatus.Processing
                            && m.NextRetryAt != null
                            && m.NextRetryAt <= now)
                        || (m.Status == OutboxMessageStatus.DeadLettered
                            && m.NextRetryAt != null
                            && m.NextRetryAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public Task<OutboxMessage?> GetByIdAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(messageId, Guid.Empty);
        return _dbContext.OutboxMessages
            .AsNoTracking()
            .SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingManifestEffectsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        DateTime now = DateTime.UtcNow;
        return await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.EventType
                    == ConfigurationManifestEffectOutbox.EventType
                && ((message.Status == OutboxMessageStatus.Pending
                        && (message.NextRetryAt == null || message.NextRetryAt <= now))
                    || (message.Status == OutboxMessageStatus.Processing
                        && message.NextRetryAt != null
                        && message.NextRetryAt <= now)))
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingImportEffectsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        DateTime now = DateTime.UtcNow;
        return await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.EventType == ConfigurationImportEffectOutbox.EventType
                && ((message.Status == OutboxMessageStatus.Pending
                        && (message.NextRetryAt == null
                            || message.NextRetryAt <= now))
                    || (message.Status == OutboxMessageStatus.Processing
                        && message.NextRetryAt != null
                        && message.NextRetryAt <= now)))
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<DateTime?> TryClaimForProcessing(
        Guid id,
        DateTime claimedAt,
        CancellationToken ct = default)
    {
        var now = AtPostgresPrecision(claimedAt);
        var leaseExpiresAt = now.Add(ProcessingLease);
        var rowsAffected = await _dbContext.OutboxMessages
            .Where(m => m.Id == id
                && ((m.Status == OutboxMessageStatus.Pending
                        && (m.NextRetryAt == null || m.NextRetryAt <= now))
                    || (m.Status == OutboxMessageStatus.Processing
                        && m.NextRetryAt != null
                        && m.NextRetryAt <= now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Processing)
                .SetProperty(m => m.NextRetryAt, leaseExpiresAt), ct);

        return rowsAffected == 1 ? leaseExpiresAt : null;
    }

    public async Task<bool> TryReplaceProcessingPayloadAsync(
        Guid id,
        string expectedPayload,
        string replacementPayload,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementPayload);
        int rowsAffected = await _dbContext.OutboxMessages
            .Where(message => message.Id == id
                && message.Status == OutboxMessageStatus.Processing
                && message.Payload == expectedPayload)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Payload, replacementPayload), ct);
        return rowsAffected == 1;
    }

    public async Task<bool> MarkAsCompleted(
        Guid id,
        DateTime processingLeaseExpiresAt,
        CancellationToken ct = default)
    {
        var rowsAffected = await _dbContext.OutboxMessages
            .Where(m => m.Id == id
                && m.Status == OutboxMessageStatus.Processing
                && m.NextRetryAt == processingLeaseExpiresAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Completed)
                .SetProperty(m => m.ProcessedAt, DateTime.UtcNow)
                .SetProperty(m => m.NextRetryAt, (DateTime?)null)
                .SetProperty(m => m.LastError, (string?)null), ct);

        return rowsAffected == 1;
    }

    public async Task<OutboxFailureTransition> MarkAsFailed(
        Guid id,
        DateTime processingLeaseExpiresAt,
        string error,
        bool isRetryable,
        int retryDelaySeconds,
        DateTime failedAt,
        CancellationToken ct = default)
    {
        var now = AtPostgresPrecision(failedAt);
        var boundedError = error.Length > 2000 ? error[..2000] : error;
        var ownedClaim = _dbContext.OutboxMessages.Where(m =>
            m.Id == id
            && m.Status == OutboxMessageStatus.Processing
            && m.NextRetryAt == processingLeaseExpiresAt);

        var deadLettered = await ownedClaim
            .Where(m => m.RetryCount + 1 >= m.MaxRetries)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.DeadLettered)
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                .SetProperty(m => m.LastError, boundedError)
                .SetProperty(m => m.NextRetryAt, processingLeaseExpiresAt)
                .SetProperty(m => m.DeadLetteredAt, now), ct);
        if (deadLettered == 1)
        {
            return OutboxFailureTransition.DeadLettered;
        }

        if (!isRetryable)
        {
            var failed = await ownedClaim
                .Where(m => m.RetryCount + 1 < m.MaxRetries)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, OutboxMessageStatus.Failed)
                    .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                    .SetProperty(m => m.LastError, boundedError)
                    .SetProperty(m => m.NextRetryAt, (DateTime?)null)
                    .SetProperty(m => m.DeadLetteredAt, (DateTime?)null), ct);
            return failed == 1
                ? OutboxFailureTransition.Failed
                : OutboxFailureTransition.NotOwned;
        }

        var retryScheduled = await ownedClaim
            .Where(m => m.RetryCount + 1 < m.MaxRetries)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxMessageStatus.Pending)
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                .SetProperty(m => m.LastError, boundedError)
                .SetProperty(m => m.NextRetryAt, now.AddSeconds(Math.Max(0, retryDelaySeconds)))
                .SetProperty(m => m.DeadLetteredAt, (DateTime?)null), ct);
        return retryScheduled == 1
            ? OutboxFailureTransition.RetryScheduled
            : OutboxFailureTransition.NotOwned;
    }

    public async Task<DateTime?> TryClaimDeadLetterReconciliation(
        Guid id,
        DateTime claimedAt,
        CancellationToken ct = default)
    {
        var now = AtPostgresPrecision(claimedAt);
        var leaseExpiresAt = now.Add(ProcessingLease);
        var rowsAffected = await _dbContext.OutboxMessages
            .Where(m => m.Id == id
                && m.Status == OutboxMessageStatus.DeadLettered
                && m.NextRetryAt != null
                && m.NextRetryAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.NextRetryAt, leaseExpiresAt), ct);

        return rowsAffected == 1 ? leaseExpiresAt : null;
    }

    public async Task<bool> MarkDeadLetterReconciled(
        Guid id,
        DateTime processingLeaseExpiresAt,
        CancellationToken ct = default)
    {
        var rowsAffected = await _dbContext.OutboxMessages
            .Where(m => m.Id == id
                && m.Status == OutboxMessageStatus.DeadLettered
                && m.NextRetryAt == processingLeaseExpiresAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.NextRetryAt, (DateTime?)null), ct);

        return rowsAffected == 1;
    }

    public async Task<List<OutboxMessage>> GetFailedEntries(int limit = 100, CancellationToken ct = default)
    {
        return await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Status == OutboxMessageStatus.Failed || m.Status == OutboxMessageStatus.DeadLettered)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<int> CountIncompleteByEventTypeAsync(string eventType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return _dbContext.OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.EventType == eventType
                && message.Status != OutboxMessageStatus.Completed, ct);
    }

    public Task<int> CountDeadLetteredByEventTypeAsync(string eventType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return _dbContext.OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.EventType == eventType
                && message.Status == OutboxMessageStatus.DeadLettered, ct);
    }

    public async Task<int> DeleteCompletedOlderThan(DateTime cutoff, CancellationToken ct = default)
    {
        return await _dbContext.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Completed && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static DateTime AtPostgresPrecision(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
