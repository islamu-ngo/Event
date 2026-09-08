using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Repositories;

public class IncomingWebhookMessageRepository : IIncomingWebhookMessageRepository
{
    private const string UniqueViolationSqlState = "23505";
    private readonly ExploreDbContext _dbContext;

    public IncomingWebhookMessageRepository(ExploreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryCreateAsync(IncomingWebhookMessage message, CancellationToken cancellationToken)
    {
        bool exists = await _dbContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AnyAsync(existing =>
                existing.TenantId == message.TenantId &&
                existing.Provider == message.Provider &&
                (existing.ProviderMessageId == message.ProviderMessageId ||
                 existing.IdempotencyKey == message.IdempotencyKey),
                cancellationToken);
        if (exists)
        {
            return false;
        }

        try
        {
            await _dbContext.IncomingWebhookMessages.AddAsync(message, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(ex))
        {
            _dbContext.ChangeTracker.Clear();
            bool duplicate = await _dbContext.IncomingWebhookMessages
                .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
                .AnyAsync(existing =>
                    existing.TenantId == message.TenantId &&
                    existing.Provider == message.Provider &&
                    (existing.ProviderMessageId == message.ProviderMessageId ||
                     existing.IdempotencyKey == message.IdempotencyKey),
                    cancellationToken);
            if (duplicate)
            {
                return false;
            }

            throw;
        }
    }

    public async Task<IncomingWebhookMessage?> GetByProviderMessageIdForUpdateAsync(
        Guid tenantId,
        string provider,
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .FirstOrDefaultAsync(
                e => e.TenantId == tenantId
                    && e.Provider == provider
                    && e.ProviderMessageId == providerMessageId,
                cancellationToken);
    }

    public Task<IncomingWebhookMessage?> GetByIdempotencyKeyForUpdateAsync(
        Guid tenantId,
        string provider,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _dbContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .SingleOrDefaultAsync(
                value => value.TenantId == tenantId && value.Provider == provider && value.IdempotencyKey == idempotencyKey,
                cancellationToken);

    public Task<IncomingWebhookMessage?> GetByTenantAndIdForUpdateAsync(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        CancellationToken cancellationToken)
    {
        return _dbContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .FirstOrDefaultAsync(
                message =>
                    message.TenantId == tenantId &&
                    message.Id == incomingWebhookMessageId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<IncomingWebhookClaim>> ClaimDueAsync(
        IncomingWebhookClaimRequest request,
        CancellationToken cancellationToken)
    {
        if (request.BatchSize is < 1 or > 1000 || request.LeaseDuration <= TimeSpan.Zero)
        {
            return [];
        }

        if (request.ClaimedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Claim time must use UTC kind.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.LeaseOwner))
        {
            throw new ArgumentException("Lease owner is required.", nameof(request));
        }

        var leaseOwner = request.LeaseOwner.Trim();
        if (leaseOwner.Length > IncomingWebhookMessage.MaxLeaseOwnerLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"Lease owner cannot exceed {IncomingWebhookMessage.MaxLeaseOwnerLength} characters.");
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                return await ClaimDueInTransactionAsync(request, leaseOwner, cancellationToken);
            }
            catch
            {
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private async Task<IReadOnlyList<IncomingWebhookClaim>> ClaimDueInTransactionAsync(
        IncomingWebhookClaimRequest request,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var leaseExpiresAt = request.ClaimedAt.Add(request.LeaseDuration);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        _ = await RelationalNamedLock.AcquireTransactionAsync(
            _dbContext,
            "incoming-webhook-claim",
            cancellationToken);

        var candidates = await _dbContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookWorkerCrossTenantQueue)
            .Where(message =>
                ((message.StatusId == (int)IncomingWebhookMessageStatus.Verified ||
                  message.StatusId == (int)IncomingWebhookMessageStatus.RetryDue) &&
                 (message.NextAttemptAt == null || message.NextAttemptAt <= request.ClaimedAt)) ||
                (message.StatusId == (int)IncomingWebhookMessageStatus.Processing &&
                 message.ProcessingLeaseExpiresAt != null &&
                 message.ProcessingLeaseExpiresAt <= request.ClaimedAt))
            .OrderBy(message => message.NextAttemptAt ?? message.ReceivedAt)
            .ThenBy(message => message.ReceivedAt)
            .ThenBy(message => message.Id)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken);

        var claims = new List<IncomingWebhookClaim>(candidates.Count);
        foreach (var message in candidates)
        {
            if (message.Status == IncomingWebhookMessageStatus.Processing)
            {
                message.RecoverExpiredClaim(request.ClaimedAt);
            }

            var leaseToken = Guid.CreateVersion7();
            message.Claim(leaseOwner, leaseToken, leaseExpiresAt, request.ClaimedAt);
            TrackAppendedEvidence(message);
            claims.Add(new IncomingWebhookClaim(
                message.Id,
                message.TenantId,
                leaseToken,
                message.ProcessingFence,
                message.ProcessingGeneration));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    public Task<IncomingWebhookMessage?> GetActiveClaimAsync(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        return _dbContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .FirstOrDefaultAsync(message =>
                message.TenantId == tenantId &&
                message.Id == incomingWebhookMessageId &&
                message.StatusId == (int)IncomingWebhookMessageStatus.Processing &&
                message.ProcessingLeaseToken == leaseToken &&
                message.ProcessingFence == processingFence &&
                message.ProcessingGeneration == processingGeneration &&
                message.ProcessingLeaseExpiresAt > observedAt,
                cancellationToken);
    }

    public async Task<bool> RefreshActiveClaimAsync(
        IncomingWebhookMessage message,
        IncomingWebhookClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc ||
            message.TenantId != claim.TenantId ||
            message.Id != claim.IncomingWebhookMessageId)
        {
            return false;
        }

        await _dbContext.Entry(message).ReloadAsync(cancellationToken);
        return message.StatusId == (int)IncomingWebhookMessageStatus.Processing &&
               message.ProcessingLeaseToken == claim.LeaseToken &&
               message.ProcessingFence == claim.ProcessingFence &&
               message.ProcessingGeneration == claim.ProcessingGeneration &&
               message.ProcessingLeaseExpiresAt > observedAt;
    }

    public async Task<bool> TryRenewClaimAsync(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        Guid leaseToken,
        long processingFence,
        int processingGeneration,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc || leaseExpiresAt.Kind != DateTimeKind.Utc || leaseExpiresAt <= observedAt)
        {
            return false;
        }

        var affected = await _dbContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .Where(message =>
                message.TenantId == tenantId &&
                message.Id == incomingWebhookMessageId &&
                message.StatusId == (int)IncomingWebhookMessageStatus.Processing &&
                message.ProcessingLeaseToken == leaseToken &&
                message.ProcessingFence == processingFence &&
                message.ProcessingGeneration == processingGeneration &&
                message.ProcessingLeaseExpiresAt > observedAt &&
                message.ProcessingLeaseExpiresAt < leaseExpiresAt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.ProcessingLeaseExpiresAt, leaseExpiresAt)
                .SetProperty(message => message.UpdatedAt, observedAt), cancellationToken);

        return affected == 1;
    }

    public void TrackAppendedEvidence(IncomingWebhookMessage message)
    {
        foreach (var attempt in message.ProcessingAttempts)
        {
            if (_dbContext.Entry(attempt).State == EntityState.Detached)
            {
                _dbContext.IncomingWebhookProcessingAttempts.Add(attempt);
            }
        }

        foreach (var redriveRecord in message.RedriveRecords)
        {
            if (_dbContext.Entry(redriveRecord).State == EntityState.Detached)
            {
                _dbContext.IncomingWebhookRedriveRecords.Add(redriveRecord);
            }
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        TrackEvidenceForTrackedAggregates();
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEffectReceiptIdentityViolation(exception))
        {
            throw new IncomingWebhookEffectReceiptConflictException(exception);
        }
    }

    private bool IsEffectReceiptIdentityViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: UniqueViolationSqlState,
            ConstraintName: { } constraintName
        } &&
        constraintName == RelationalConstraintDescriptorResolver.UniqueIndex<IncomingWebhookEffectReceipt>(
            _dbContext,
            nameof(IncomingWebhookEffectReceipt.TenantId),
            nameof(IncomingWebhookEffectReceipt.IncomingWebhookMessageId),
            nameof(IncomingWebhookEffectReceipt.EffectKind)).Name;

    private void TrackEvidenceForTrackedAggregates()
    {
        var automaticChangeDetection = _dbContext.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            _dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
            var messages = _dbContext.ChangeTracker
                .Entries<IncomingWebhookMessage>()
                .Select(entry => entry.Entity)
                .ToArray();
            foreach (var message in messages)
            {
                TrackAppendedEvidence(message);
            }
        }
        finally
        {
            _dbContext.ChangeTracker.AutoDetectChangesEnabled = automaticChangeDetection;
        }
    }
}
