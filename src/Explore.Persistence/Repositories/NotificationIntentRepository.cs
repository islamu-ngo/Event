// ABOUTME: Repository for normalized notification intent, delivery, and external delegation rows.
// ABOUTME: Uses exact tenant predicates for worker-safe lookup without leaking IQueryable.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Extensions;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema.ProviderPrimitives;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class NotificationIntentRepository : GenericRepository<NotificationIntent, Guid>,
    INotificationIntentRepository,
    IRecipientNotificationGraphRepository
{
    private const string UniqueViolationSqlState = "23505";
    private readonly ExploreDbContext _dbContext;
    private readonly ISettingMutationLock _mutationLock;
    private readonly NotificationDeliveryPolicyResolver _deliveryPolicyResolver = new();

    public NotificationIntentRepository(ExploreDbContext dbContext, ISettingMutationLock mutationLock) : base(dbContext)
    {
        _dbContext = dbContext;
        _mutationLock = mutationLock;
    }

    public async Task<NotificationIntent> CreateIntentAsync(NotificationIntent intent, CancellationToken cancellationToken = default)
    {
        return await CreateGraphAsync(intent, cancellationToken);
    }

    public Task<NotificationIntent> CreateGraphAsync(NotificationIntent intent, CancellationToken cancellationToken = default) =>
        _mutationLock.ExecuteManyAsync([GovernanceSettingKeys.Email.DeliveryEnabled],
            token => CreateGraphUnderPolicyLockAsync(intent, token), cancellationToken);

    private async Task<NotificationIntent> CreateGraphUnderPolicyLockAsync(
        NotificationIntent intent, CancellationToken cancellationToken)
    {
        var occurrence = await EnsureFanoutOccurrencePendingUnderEventLockAsync(intent, cancellationToken);
        var control = await ReadTenantControlAsync(intent.TenantId, cancellationToken);
        intent.EmailDeliveryPolicyRevision = occurrence?.EmailDeliveryPolicyRevision ?? control?.DeliveryPolicyRevision ?? 0;
        await ApplyEmailPolicyAsync(intent, intent.Deliveries, control, cancellationToken);
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

    public Task RepairMissingRecipientDeliveryRowsAsync(
        NotificationIntent winningIntent,
        IReadOnlyList<NotificationDelivery> expectedDeliveries,
        Notification? expectedNotification,
        EmailDispatchOutbox? expectedEmail,
        CancellationToken cancellationToken = default) =>
        _mutationLock.ExecuteManyAsync([GovernanceSettingKeys.Email.DeliveryEnabled], async token =>
        {
            await RepairUnderPolicyLockAsync(winningIntent, expectedDeliveries, expectedNotification, expectedEmail, token);
            return true;
        }, cancellationToken);

    private async Task RepairUnderPolicyLockAsync(NotificationIntent winningIntent,
        IReadOnlyList<NotificationDelivery> expectedDeliveries, Notification? expectedNotification,
        EmailDispatchOutbox? expectedEmail, CancellationToken cancellationToken)
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

        List<NotificationDelivery> reconstructedEmailDeliveries = [];
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
                _dbContext.NotificationDeliveries.Add(expected);
                if (expected.ChannelId == (int)NotificationPreferenceChannelEnum.Email)
                    reconstructedEmailDeliveries.Add(expected);
                continue;
            }

            // Repair does not authorize replay of terminal email history or release an operator park.
            if (existing.ChannelId == (int)NotificationPreferenceChannelEnum.Email
                && existing.StatusId is not ((int)NotificationDeliveryStatusEnum.Pending)
                    and not ((int)NotificationDeliveryStatusEnum.Queued))
                continue;

            if (existing.NotificationId is null && expected.NotificationId is not null && expectedNotification is not null)
            {
                expectedNotification.NotificationIntentId = tracked.Id;
                expectedNotification.NotificationIntent = tracked;
                existing.NotificationId = expectedNotification.Id;
                existing.Notification = expectedNotification;
                _dbContext.Notifications.Add(expectedNotification);
            }

            if (existing.EmailDispatchOutboxId is null && expected.EmailDispatchOutboxId is not null && expectedEmail is not null)
            {
                expectedEmail.NotificationIntentId = tracked.Id;
                expectedEmail.NotificationIntent = tracked;
                existing.EmailDispatchOutboxId = expectedEmail.Id;
                existing.EmailDispatchOutbox = expectedEmail;
                _dbContext.EmailDispatchOutbox.Add(expectedEmail);
                reconstructedEmailDeliveries.Add(existing);
            }
        }

        // Existing outboxes belong to admission/settlement, including a live SMTP handoff.
        if (reconstructedEmailDeliveries.Count > 0)
        {
            var control = await ReadTenantControlAsync(tracked.TenantId, cancellationToken);
            await ApplyEmailPolicyAsync(tracked, reconstructedEmailDeliveries, control, cancellationToken);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<NotificationFanoutOccurrence?> EnsureFanoutOccurrencePendingUnderEventLockAsync(
        NotificationIntent intent,
        CancellationToken cancellationToken)
    {
        if (intent.FanoutOccurrenceId is not { } occurrenceId)
        {
            return null;
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
        var occurrence = await _dbContext.NotificationFanoutOccurrences
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .SingleOrDefaultAsync(occurrence => occurrence.TenantId == intent.TenantId
                && occurrence.Id == occurrenceId
                && occurrence.EventId == eventId
                && occurrence.State == NotificationFanoutOccurrenceState.Pending,
                cancellationToken);
        if (occurrence is null)
        {
            throw new NotificationFanoutOccurrenceUnavailableException();
        }
        return occurrence;
    }

    private Task<EmailDispatchTenantControl?> ReadTenantControlAsync(Guid tenantId, CancellationToken cancellationToken) =>
        _dbContext.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking().SingleOrDefaultAsync(control => control.TenantId == tenantId, cancellationToken);

    private async Task ApplyEmailPolicyAsync(NotificationIntent intent,
        IEnumerable<NotificationDelivery> candidates, EmailDispatchTenantControl? control, CancellationToken cancellationToken)
    {
        var deliveries = candidates.Where(delivery =>
            delivery.ChannelId == (int)NotificationPreferenceChannelEnum.Email
            && delivery.StatusId is (int)NotificationDeliveryStatusEnum.Pending or (int)NotificationDeliveryStatusEnum.Queued)
            .ToArray();
        if (deliveries.Length == 0)
            return;

        var policy = await EmailDeliveryPolicyReader.ReadAsync(_dbContext, intent.TenantId, cancellationToken);
        DateTime now = await RelationalDatabaseClock.GetUtcNowAsync(_dbContext, cancellationToken);
        foreach (var delivery in deliveries)
        {
            var definition = await _dbContext.Set<NotificationDeliveryPolicy>().AsNoTracking()
                .SingleAsync(value => value.Id == delivery.DeliveryPolicyId, cancellationToken);
            var resolution = _deliveryPolicyResolver.Resolve(definition.Id, definition.MasterCode, delivery.PolicyVersion);
            if (!resolution.IsSupported)
                throw new InvalidOperationException("Notification graph has an unsupported email delivery policy.");
            bool skip = EmailDeliveryPolicy.ShouldSuppressOptional(
                state: policy.State, honorsPreference: resolution.HonorsPreference,
                occurrenceRevision: intent.EmailDeliveryPolicyRevision,
                suppressedThroughRevision: control?.OptionalSuppressedThroughRevision);
            if (!skip && policy.State == EmailDeliveryState.Available)
                continue;

            string reason = skip && policy.State is (EmailDeliveryState.Disabled or EmailDeliveryState.Available)
                ? "email_delivery_disabled" : "email_capability_unavailable";
            delivery.StatusId = (int)(skip ? NotificationDeliveryStatusEnum.Skipped : NotificationDeliveryStatusEnum.Parked);
            delivery.ProviderStatus = skip ? "skipped" : "parked";
            delivery.FailureCategory = reason;
            delivery.CompletedAt = skip ? now : null;
            delivery.UpdatedAt = now;
            if (delivery.EmailDispatchOutbox is not { } email)
                continue;
            email.Status = skip ? EmailDispatchStatus.Skipped : EmailDispatchStatus.Parked;
            email.ParkedAt = skip ? null : now;
            email.ParkReason = skip ? null : EmailDispatchParkReason.CapabilityUnavailable;
            email.NextAttemptAt = null;
            email.ProcessingStartedAt = null;
            email.ProcessingLeaseToken = null;
            email.LastFailureCategory = reason;
            email.LastError = "Email delivery was suppressed before SMTP handoff by the delivery policy.";
            email.LastFailureAt = now;
            email.UpdatedAt = now;
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
