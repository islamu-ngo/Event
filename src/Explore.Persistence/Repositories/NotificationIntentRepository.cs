using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Extensions;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class NotificationIntentRepository : GenericRepository<NotificationIntent, Guid>,
    INotificationIntentRepository,
    IRecipientNotificationGraphRepository
{
    private const string UniqueViolationSqlState = "23505";
    private readonly ExploreDbContext _dbContext;

    public NotificationIntentRepository(ExploreDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<NotificationIntent> CreateIntentAsync(NotificationIntent intent, CancellationToken cancellationToken = default)
    {
        return await CreateGraphAsync(intent, cancellationToken);
    }

    public async Task<NotificationIntent> CreateGraphAsync(NotificationIntent intent, CancellationToken cancellationToken = default)
    {
        await EnsureFanoutOccurrencePendingUnderEventLockAsync(intent, cancellationToken);
        try
        {
            _dbContext.NotificationIntents.Add(intent);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return intent;
        }
        catch (DbUpdateException ex) when (IsDeduplicationConflict(ex))
        {
            throw new NotificationIntentDeduplicationConflictException(ex);
        }
    }

    private bool IsDeduplicationConflict(DbUpdateException exception)
    {
        if (exception.InnerException is not Npgsql.PostgresException
            {
                SqlState: UniqueViolationSqlState,
                ConstraintName: { } constraintName
            })
        {
            return false;
        }

        string primaryKey = RelationalConstraintDescriptorResolver
            .PrimaryKey<NotificationIntent>(_dbContext).Name;
        string deduplication = RelationalConstraintDescriptorResolver.UniqueIndex<NotificationIntent>(
            _dbContext,
            nameof(NotificationIntent.TenantId),
            nameof(NotificationIntent.DeduplicationKey)).Name;
        string occurrenceRecipient = RelationalConstraintDescriptorResolver.UniqueIndex<NotificationIntent>(
            _dbContext,
            nameof(NotificationIntent.TenantId),
            nameof(NotificationIntent.FanoutOccurrenceId),
            nameof(NotificationIntent.RecipientUserId)).Name;
        return constraintName == primaryKey ||
               constraintName == deduplication ||
               constraintName == occurrenceRecipient;
    }

    public async Task<NotificationIntent?> GetGraphByTenantOccurrenceAndRecipientAsync(
        Guid tenantId,
        Guid occurrenceId,
        Guid recipientUserId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationIntents
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Include(intent => intent.Deliveries)
                .ThenInclude(delivery => delivery.Notification)
            .Include(intent => intent.Deliveries)
                .ThenInclude(delivery => delivery.EmailDispatchOutbox)
            .SingleOrDefaultAsync(intent => intent.TenantId == tenantId
                && intent.FanoutOccurrenceId == occurrenceId
                && intent.RecipientUserId == recipientUserId,
                cancellationToken);
    }

    public async Task<NotificationIntent?> GetGraphByTenantAndDeduplicationKeyAsync(
        Guid tenantId,
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationIntents
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .Include(intent => intent.Deliveries)
                .ThenInclude(delivery => delivery.Notification)
            .Include(intent => intent.Deliveries)
                .ThenInclude(delivery => delivery.EmailDispatchOutbox)
            .SingleOrDefaultAsync(intent => intent.TenantId == tenantId
                && intent.DeduplicationKey == deduplicationKey,
                cancellationToken);
    }

    public async Task<NotificationIntent?> GetByTenantAndIdAsync(
        Guid tenantId,
        Guid intentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationIntents
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .FirstOrDefaultAsync(intent => intent.TenantId == tenantId && intent.Id == intentId, cancellationToken);
    }

    public async Task<bool> ExistsByDeduplicationKeyAsync(
        Guid tenantId,
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.NotificationIntents
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .AnyAsync(intent => intent.TenantId == tenantId && intent.DeduplicationKey == deduplicationKey, cancellationToken);
    }

    public async Task<NotificationDelivery> AddDeliveryAsync(
        NotificationDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        _dbContext.NotificationDeliveries.Add(delivery);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return delivery;
    }

    public async Task RepairMissingRecipientDeliveryRowsAsync(
        NotificationIntent winningIntent,
        IReadOnlyList<NotificationDelivery> expectedDeliveries,
        Notification? expectedNotification,
        EmailDispatchOutbox? expectedEmail,
        CancellationToken cancellationToken = default)
    {
        await EnsureFanoutOccurrencePendingUnderEventLockAsync(winningIntent, cancellationToken);
        var tracked = await _dbContext.NotificationIntents
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Include(intent => intent.Deliveries)
                .ThenInclude(delivery => delivery.Notification)
            .Include(intent => intent.Deliveries)
                .ThenInclude(delivery => delivery.EmailDispatchOutbox)
            .SingleAsync(intent => intent.TenantId == winningIntent.TenantId
                && intent.Id == winningIntent.Id,
                cancellationToken);

        foreach (NotificationDelivery expected in expectedDeliveries)
        {
            NotificationDelivery? existing = tracked.Deliveries.SingleOrDefault(row => row.ChannelId == expected.ChannelId);
            if (existing is null)
            {
                expected.NotificationIntentId = tracked.Id;
                expected.NotificationIntent = tracked;
                if (expected.Notification is not null)
                {
                    expected.Notification.NotificationIntentId = tracked.Id;
                    expected.Notification.NotificationIntent = tracked;
                }

                if (expected.EmailDispatchOutbox is not null)
                {
                    expected.EmailDispatchOutbox.NotificationIntentId = tracked.Id;
                    expected.EmailDispatchOutbox.NotificationIntent = tracked;
                }

                tracked.Deliveries.Add(expected);
                continue;
            }

            if (existing.NotificationId is null && expected.NotificationId is not null && expectedNotification is not null)
            {
                expectedNotification.NotificationIntentId = tracked.Id;
                expectedNotification.NotificationIntent = tracked;
                existing.NotificationId = expectedNotification.Id;
                existing.Notification = expectedNotification;
            }

            if (existing.EmailDispatchOutboxId is null && expected.EmailDispatchOutboxId is not null && expectedEmail is not null)
            {
                expectedEmail.NotificationIntentId = tracked.Id;
                expectedEmail.NotificationIntent = tracked;
                existing.EmailDispatchOutboxId = expectedEmail.Id;
                existing.EmailDispatchOutbox = expectedEmail;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureFanoutOccurrencePendingUnderEventLockAsync(
        NotificationIntent intent,
        CancellationToken cancellationToken)
    {
        if (intent.FanoutOccurrenceId is not { } occurrenceId)
        {
            return;
        }

        if (intent.EventId is not { } eventId
            || occurrenceId == Guid.Empty
            || eventId == Guid.Empty
            || intent.TenantId == Guid.Empty)
        {
            throw new NotificationFanoutOccurrenceUnavailableException();
        }

        NotificationFanoutPrecedenceLock.EnsureActiveTransaction(_dbContext);
        await using IAsyncDisposable eventPrecedenceLease = await NotificationFanoutPrecedenceLock.AcquireAsync(
            _dbContext,
            intent.TenantId,
            eventId,
            cancellationToken);
        bool remainsPending = await _dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .AnyAsync(occurrence => occurrence.TenantId == intent.TenantId
                && occurrence.Id == occurrenceId
                && occurrence.EventId == eventId
                && occurrence.State == NotificationFanoutOccurrenceState.Pending,
                cancellationToken);
        if (!remainsPending)
        {
            throw new NotificationFanoutOccurrenceUnavailableException();
        }
    }

    public async Task<NotificationExternalDelegation> AddExternalDelegationAsync(
        NotificationExternalDelegation delegation,
        CancellationToken cancellationToken = default)
    {
        _dbContext.NotificationExternalDelegations.Add(delegation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return delegation;
    }
}
