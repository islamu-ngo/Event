using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain;

namespace Explore.Infrastructure.Tests.Fixtures;

public sealed class InMemoryEmailDispatchOutboxRepository(EmailDispatchOutbox dispatch)
    : IEmailDispatchOutboxRepository
{
    private readonly object _gate = new();

    public EmailDispatchOutbox Dispatch => dispatch;

    public List<EmailDispatchAttempt> Attempts { get; } = [];

    public List<EmailDispatchReceipt> Receipts { get; } = [];

    public int ReplayCount { get; private set; }

    public Task<EmailDispatchOutbox> Create(EmailDispatchOutbox entity, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<EmailDispatchOutbox>> ClaimPendingBatchAsync(
        EmailDispatchBatchClaimRequest request,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!TryClaim(request.LeaseToken, request.ClaimedAt))
            {
                return Task.FromResult<IReadOnlyList<EmailDispatchOutbox>>([]);
            }

            return Task.FromResult<IReadOnlyList<EmailDispatchOutbox>>([dispatch]);
        }
    }

    public Task<EmailDispatchOutbox?> TryClaimSpecificAsync(
        EmailDispatchSpecificClaimRequest request,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (request.TenantId != dispatch.TenantId ||
                request.PublishEventId != dispatch.PublishEventId ||
                !TryClaim(request.LeaseToken, request.ClaimedAt))
            {
                return Task.FromResult<EmailDispatchOutbox?>(null);
            }

            return Task.FromResult<EmailDispatchOutbox?>(dispatch);
        }
    }

    public Task<EventReminderStateChangeResult> SuppressEventRemindersInCurrentTransactionAsync(
        EventReminderSupersessionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TenantId == Guid.Empty
            || request.EventId == Guid.Empty
            || request.SupersededAt.Kind != DateTimeKind.Utc
            || string.IsNullOrWhiteSpace(request.ReasonCode)
            || request.ReasonCode.Length > 200)
        {
            throw new ArgumentException(
                "Reminder supersession requires exact tenant/event authority, a UTC time, and a bounded reason.",
                nameof(request));
        }

        lock (_gate)
        {
            if (!IsEligibleReminder(
                    request.TenantId,
                    request.EventId,
                    request.RegistrationOrderId,
                    request.SessionId,
                    requireSchedule: false,
                    out _))
            {
                return Task.FromResult(NoReminderRowsChanged);
            }

            dispatch.Status = EmailDispatchStatus.Skipped;
            dispatch.NextAttemptAt = null;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.LastFailureCategory = request.ReasonCode;
            dispatch.LastError = "The reminder was superseded before SMTP provider handoff.";
            dispatch.LastFailureAt = request.SupersededAt;
            dispatch.UpdatedAt = request.SupersededAt;
            return Task.FromResult(OneReminderOutboxRowChanged);
        }
    }

    public Task<EventReminderStateChangeResult> RescheduleEventRemindersInCurrentTransactionAsync(
        EventReminderRescheduleRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TenantId == Guid.Empty
            || request.EventId == Guid.Empty
            || request.ChangedAt.Kind != DateTimeKind.Utc
            || request.LeadTime <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Reminder rescheduling requires exact tenant/event authority, positive lead time, and a UTC time.",
                nameof(request));
        }

        lock (_gate)
        {
            if (!IsEligibleReminder(
                    request.TenantId,
                    request.EventId,
                    request.RegistrationOrderId,
                    request.SessionId,
                    requireSchedule: true,
                    out EventReminderSchedule schedule))
            {
                return Task.FromResult(NoReminderRowsChanged);
            }

            string title = string.IsNullOrWhiteSpace(request.EventTitle)
                ? "the event"
                : request.EventTitle.Trim();
            string timeZoneId = Explore.Domain.Services.Scheduling.ScheduleTimeZoneResolver.NormalizeOrUtc(
                request.EventTimeZoneId);
            string startsAt = EventReminderAuthorityReference.FormatDisplay(schedule.StartsAtUtc, timeZoneId);
            DateTime calculatedDueAt = schedule.StartsAtUtc.UtcDateTime.Subtract(request.LeadTime);

            if (schedule.StartsAtUtc.UtcDateTime <= request.ChangedAt)
            {
                dispatch.Status = EmailDispatchStatus.Skipped;
                dispatch.NextAttemptAt = null;
                dispatch.ProcessingStartedAt = null;
                dispatch.ProcessingLeaseToken = null;
                dispatch.LastFailureCategory = "event_reminder_schedule_changed";
                dispatch.LastError = "The reminder was superseded before SMTP provider handoff.";
                dispatch.LastFailureAt = request.ChangedAt;
                dispatch.UpdatedAt = request.ChangedAt;
                return Task.FromResult(OneReminderOutboxRowChanged);
            }

            dispatch.Status = EmailDispatchStatus.Pending;
            dispatch.NextAttemptAt = calculatedDueAt > request.ChangedAt ? calculatedDueAt : request.ChangedAt;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.Subject = $"Reminder: {title}";
            dispatch.PlainTextBody =
                $"Assalamu alaykum,\n\nThis is a reminder that {title} starts at {startsAt}.\n\nEvent Platform";
            dispatch.HtmlBody =
                $"<p>Assalamu alaykum,</p><p>This is a reminder that <strong>{System.Net.WebUtility.HtmlEncode(title)}</strong> starts at {System.Net.WebUtility.HtmlEncode(startsAt)}.</p><p>Event Platform</p>";
            dispatch.CorrelationId = EventReminderAuthorityReference.Format(
                schedule.SessionId,
                schedule.StartsAtUtc,
                timeZoneId);
            dispatch.LastFailureCategory = null;
            dispatch.LastError = null;
            dispatch.LastFailureAt = null;
            dispatch.UpdatedAt = request.ChangedAt;
            return Task.FromResult(OneReminderOutboxRowChanged);
        }
    }

    public Task<IReadOnlyList<EmailDispatchOutbox>> GetRabbitMqPublishBatch(
        int batchSize,
        DateTime now,
        DateTime retryAttemptsBefore,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<EmailDispatchOutbox>> GetStatusRows(
        Guid tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<EmailDispatchOutbox?> GetByTenantAndId(
        Guid tenantId,
        Guid outboxId,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<EmailDispatchOutbox?> GetByTenantAndPublishEventId(
        Guid tenantId,
        Guid publishEventId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(dispatch.TenantId == tenantId && dispatch.PublishEventId == publishEventId
                ? dispatch
                : null);
        }
    }

    public Task<int> CountDueDispatchAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var isDue = dispatch.Status is EmailDispatchStatus.Pending or EmailDispatchStatus.RetryScheduled
                && (dispatch.NextAttemptAt is null || dispatch.NextAttemptAt <= now);

            return Task.FromResult(isDue ? 1 : 0);
        }
    }

    public Task<DateTime?> GetOldestDueCreatedAtAsync(DateTime now, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            DateTime? createdAt = dispatch.Status is EmailDispatchStatus.Pending or EmailDispatchStatus.RetryScheduled
                && (dispatch.NextAttemptAt is null || dispatch.NextAttemptAt <= now)
                    ? dispatch.CreatedAt
                    : null;
            return Task.FromResult(createdAt);
        }
    }

    public Task<IReadOnlyDictionary<Guid, int>> CountDueDispatchByTenantAsync(
        DateTime now,
        int tenantLimit,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var isDue = dispatch.Status is EmailDispatchStatus.Pending or EmailDispatchStatus.RetryScheduled
                && (dispatch.NextAttemptAt is null || dispatch.NextAttemptAt <= now);
            IReadOnlyDictionary<Guid, int> result = isDue
                ? new Dictionary<Guid, int> { [dispatch.TenantId] = 1 }
                : new Dictionary<Guid, int>();
            return Task.FromResult(result);
        }
    }

    public Task<int> CountRetryScheduledAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(dispatch.Status == EmailDispatchStatus.RetryScheduled ? 1 : 0);
        }
    }

    public Task<int> CountStaleProcessingAsync(
        DateTime processingStartedBefore,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var isStaleProcessing = dispatch.Status == EmailDispatchStatus.Processing
                && dispatch.ProcessingStartedAt is not null
                && dispatch.ProcessingStartedAt <= processingStartedBefore;

            return Task.FromResult(isStaleProcessing ? 1 : 0);
        }
    }

    public Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(dispatch.Status == EmailDispatchStatus.DeadLettered ? 1 : 0);
        }
    }

    public Task<int> CountUnknownAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(dispatch.Status == EmailDispatchStatus.Unknown ? 1 : 0);
        }
    }

    public Task<int> CountParkedAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(dispatch.Status == EmailDispatchStatus.Parked ? 1 : 0);
        }
    }

    public Task<bool> IsOptionalReminderDeferralActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<EmailDispatchProcessorState?> GetProcessorState(CancellationToken cancellationToken) =>
        Task.FromResult<EmailDispatchProcessorState?>(null);

    public Task<EmailDispatchProcessorState> SetProcessorPauseState(
        bool isPaused,
        string? pauseReason,
        Guid? changedBy,
        DateTime changedAt,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<EmailDispatchProcessorState> SetGlobalSmtpRateLimitOverride(
        int? rateLimitPerMinute,
        Guid? changedBy,
        DateTime changedAt,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> IsTenantPaused(Guid tenantId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<EmailDispatchTenantControl?> GetTenantControl(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<EmailDispatchTenantControl?>(null);

    public Task<EmailDispatchTenantControl> SetTenantPauseState(
        Guid tenantId,
        bool isPaused,
        string? pauseReason,
        Guid? changedBy,
        DateTime changedAt,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<bool> TryParkForOperator(
        Guid tenantId,
        Guid outboxId,
        string reason,
        Guid? changedBy,
        DateTime parkedAt,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<bool> TryReplayForOperator(
        Guid tenantId,
        Guid outboxId,
        Guid? changedBy,
        DateTime replayAt,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (dispatch.TenantId != tenantId ||
                dispatch.Id != outboxId ||
                dispatch.ContentRedactedAt is not null)
            {
                return Task.FromResult(false);
            }

            if (dispatch.Status is not (EmailDispatchStatus.DeadLettered
                or EmailDispatchStatus.Parked
                or EmailDispatchStatus.RetryScheduled))
            {
                return Task.FromResult(false);
            }

            dispatch.Status = EmailDispatchStatus.Pending;
            dispatch.NextAttemptAt = null;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.DeadLetteredAt = null;
            dispatch.ParkedAt = null;
            dispatch.ParkReason = null;
            dispatch.UnknownAt = null;
            dispatch.LastFailureCategory = null;
            dispatch.LastError = null;
            dispatch.UpdatedAt = replayAt;
            dispatch.UpdatedBy = changedBy;
            ReplayCount++;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryResolveWithoutReplay(
        Guid tenantId,
        Guid outboxId,
        string reason,
        Guid? changedBy,
        DateTime resolvedAt,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (dispatch.TenantId != tenantId ||
                dispatch.Id != outboxId ||
                dispatch.ContentRedactedAt is not null ||
                dispatch.Status is not (EmailDispatchStatus.DeadLettered
                    or EmailDispatchStatus.Parked
                    or EmailDispatchStatus.Unknown))
            {
                return Task.FromResult(false);
            }

            dispatch.Status = EmailDispatchStatus.Skipped;
            dispatch.LastFailureCategory = "operator_resolved_without_replay";
            dispatch.LastError = reason;
            dispatch.LastFailureAt = resolvedAt;
            dispatch.UpdatedAt = resolvedAt;
            dispatch.UpdatedBy = changedBy;
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryReconcileUnknown(
        Guid tenantId,
        Guid outboxId,
        EmailDispatchUnknownReconciliationOutcome outcome,
        string reason,
        string? providerMessageId,
        Guid? changedBy,
        DateTime reconciledAt,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (dispatch.TenantId != tenantId || dispatch.Id != outboxId || dispatch.Status != EmailDispatchStatus.Unknown)
            {
                return Task.FromResult(false);
            }

            dispatch.Status = outcome == EmailDispatchUnknownReconciliationOutcome.Delivered
                ? EmailDispatchStatus.Sent
                : EmailDispatchStatus.Pending;
            dispatch.SentAt = outcome == EmailDispatchUnknownReconciliationOutcome.Delivered ? reconciledAt : null;
            dispatch.UnknownAt = null;
            dispatch.ProviderMessageId = outcome == EmailDispatchUnknownReconciliationOutcome.Delivered
                ? providerMessageId
                : null;
            dispatch.LastFailureCategory = outcome == EmailDispatchUnknownReconciliationOutcome.Delivered
                ? "operator_reconciled_delivered"
                : "operator_reconciled_not_delivered";
            dispatch.LastError = reason;
            dispatch.UpdatedAt = reconciledAt;
            dispatch.UpdatedBy = changedBy;
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<Guid>> GetRetentionTenantIds(
        DateTime cutoffUtc,
        int maxTenants,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Guid> tenantIds = maxTenants > 0 && IsRetentionEligible(cutoffUtc)
                ? [dispatch.TenantId]
                : [];
            return Task.FromResult(tenantIds);
        }
    }

    public Task<int> CountRetentionRedactionEligible(
        Guid tenantId,
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var isEligible = dispatch.TenantId == tenantId && batchSize > 0 && IsRetentionEligible(cutoffUtc);
            return Task.FromResult(isEligible ? 1 : 0);
        }
    }

    public Task<int> RedactRetentionEligible(
        Guid tenantId,
        DateTime cutoffUtc,
        DateTime redactedAt,
        int batchSize,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var isEligible = dispatch.TenantId == tenantId && batchSize > 0 && IsRetentionEligible(cutoffUtc);
            if (!isEligible)
            {
                return Task.FromResult(0);
            }

            dispatch.RecipientEmail = string.Empty;
            dispatch.Subject = string.Empty;
            dispatch.PlainTextBody = null;
            dispatch.HtmlBody = null;
            dispatch.ReplyTo = null;
            dispatch.LastError = null;
            dispatch.ProviderMessageId = null;
            dispatch.CorrelationId = null;
            dispatch.ContentRedactedAt = redactedAt;
            dispatch.UpdatedAt = redactedAt;

            foreach (var attempt in Attempts)
            {
                attempt.Provider = null;
                attempt.SanitizedErrorMessage = null;
                attempt.ProviderMessageId = null;
                attempt.CorrelationId = null;
            }

            foreach (var receipt in Receipts)
            {
                receipt.ConsumerId = null;
                receipt.FailureMessage = null;
                receipt.ProviderMessageId = null;
            }

            return Task.FromResult(1);
        }
    }

    public Task<int> SuppressAndRedactTenant(
        Guid tenantId,
        Guid? changedBy,
        DateTime redactedAt,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (dispatch.TenantId != tenantId || dispatch.ContentRedactedAt is not null)
            {
                return Task.FromResult(0);
            }

            if (dispatch.Status is EmailDispatchStatus.Pending or EmailDispatchStatus.RetryScheduled or EmailDispatchStatus.Processing)
            {
                dispatch.Status = EmailDispatchStatus.Skipped;
                dispatch.LastFailureCategory = "tenant_deleted";
                dispatch.LastFailureAt = redactedAt;
            }

            dispatch.RecipientEmail = string.Empty;
            dispatch.Subject = string.Empty;
            dispatch.PlainTextBody = null;
            dispatch.HtmlBody = null;
            dispatch.ReplyTo = null;
            dispatch.LastError = null;
            dispatch.ProviderMessageId = null;
            dispatch.CorrelationId = null;
            dispatch.NextAttemptAt = null;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.ContentRedactedAt = redactedAt;
            dispatch.UpdatedAt = redactedAt;
            dispatch.UpdatedBy = changedBy;

            foreach (var attempt in Attempts)
            {
                attempt.Provider = null;
                attempt.SanitizedErrorMessage = null;
                attempt.ProviderMessageId = null;
                attempt.CorrelationId = null;
            }

            foreach (var receipt in Receipts)
            {
                receipt.Status = receipt.Status == EmailDispatchReceiptStatus.Completed
                    ? EmailDispatchReceiptStatus.Completed
                    : EmailDispatchReceiptStatus.Skipped;
                receipt.ConsumerId = null;
                receipt.FailureMessage = null;
                receipt.ProviderMessageId = null;
            }

            return Task.FromResult(1);
        }
    }

    private bool TryClaim(Guid leaseToken, DateTime startedAt)
    {
        if (dispatch.Status != EmailDispatchStatus.Pending)
        {
            return false;
        }

        dispatch.Status = EmailDispatchStatus.Processing;
        dispatch.ProcessingLeaseToken = leaseToken;
        dispatch.ProcessingStartedAt = startedAt;
        return true;
    }

    private static readonly EventReminderStateChangeResult NoReminderRowsChanged = new(0, 0, 0, 0);
    private static readonly EventReminderStateChangeResult OneReminderOutboxRowChanged = new(1, 0, 0, 0);

    private bool IsEligibleReminder(
        Guid tenantId,
        Guid eventId,
        Guid? registrationOrderId,
        Guid? sessionId,
        bool requireSchedule,
        out EventReminderSchedule schedule)
    {
        schedule = default;
        if (dispatch.TenantId != tenantId
            || dispatch.EventId != eventId
            || dispatch.Kind != EmailDispatchKind.EventReminder
            || dispatch.IsDeleted
            || dispatch.ContentRedactedAt is not null
            || dispatch.Status is not (
                EmailDispatchStatus.Pending
                or EmailDispatchStatus.RetryScheduled
                or EmailDispatchStatus.Processing)
            || registrationOrderId.HasValue && dispatch.RegistrationOrderId != registrationOrderId
            || dispatch.Status == EmailDispatchStatus.Processing && HasProviderHandoffFence())
        {
            return false;
        }

        if (!sessionId.HasValue && !requireSchedule)
        {
            return true;
        }

        if (!EventReminderAuthorityReference.TryParse(
                dispatch.CorrelationId,
                out Guid scheduledSessionId,
                out DateTimeOffset scheduledStartUtc,
                out _)
            || sessionId.HasValue && scheduledSessionId != sessionId)
        {
            return false;
        }

        schedule = new EventReminderSchedule(scheduledSessionId, scheduledStartUtc);
        return true;
    }

    private bool HasProviderHandoffFence() =>
        Attempts.Any(attempt =>
            attempt.EmailDispatchOutboxId == dispatch.Id
            && attempt.AttemptNumber == dispatch.AttemptCount
            && attempt.FailureCategory == "provider_handoff_started")
        || Receipts.Any(receipt =>
            receipt.EmailDispatchOutboxId == dispatch.Id
            && receipt.Status == EmailDispatchReceiptStatus.Processing);

    private readonly record struct EventReminderSchedule(Guid SessionId, DateTimeOffset StartsAtUtc);

    public Task<EmailDispatchStaleRecoveryResult> RecoverStaleProcessing(
        EmailDispatchStaleRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (request.BatchSize <= 0
                || dispatch.Status != EmailDispatchStatus.Processing
                || dispatch.ProcessingStartedAt is null
                || dispatch.ProcessingStartedAt > request.ProcessingStartedBefore)
            {
                return Task.FromResult(new EmailDispatchStaleRecoveryResult(0, 0));
            }

            var fenced = Attempts.Any(attempt =>
                    attempt.EmailDispatchOutboxId == dispatch.Id
                    && attempt.AttemptNumber == dispatch.AttemptCount
                    && attempt.FailureCategory == "provider_handoff_started")
                || Receipts.Any(receipt =>
                    receipt.EmailDispatchOutboxId == dispatch.Id
                    && receipt.Status == EmailDispatchReceiptStatus.Processing);
            dispatch.Status = fenced ? EmailDispatchStatus.Unknown : EmailDispatchStatus.RetryScheduled;
            dispatch.UnknownAt = fenced ? request.RecoveredAt : null;
            dispatch.NextAttemptAt = fenced ? null : request.RecoveredAt;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.LastFailureCategory = fenced
                ? request.UnknownFailureCategory
                : request.RetryFailureCategory;
            dispatch.LastError = fenced ? request.UnknownErrorMessage : request.RetryErrorMessage;
            if (fenced)
            {
                foreach (var attempt in Attempts.Where(attempt =>
                             attempt.EmailDispatchOutboxId == dispatch.Id
                             && attempt.AttemptNumber == dispatch.AttemptCount))
                {
                    attempt.Outcome = EmailDispatchAttemptOutcome.Unknown;
                    attempt.CompletedAt = request.RecoveredAt;
                    attempt.FailureCategory = request.UnknownFailureCategory;
                    attempt.SanitizedErrorMessage = request.UnknownErrorMessage;
                }

                foreach (var receipt in Receipts.Where(receipt =>
                             receipt.EmailDispatchOutboxId == dispatch.Id
                             && receipt.Status == EmailDispatchReceiptStatus.Processing))
                {
                    receipt.Status = EmailDispatchReceiptStatus.Unknown;
                    receipt.FailedAt = request.RecoveredAt;
                    receipt.FailureCode = request.UnknownFailureCategory;
                    receipt.FailureMessage = request.UnknownErrorMessage;
                }
            }

            return Task.FromResult(new EmailDispatchStaleRecoveryResult(fenced ? 0 : 1, fenced ? 1 : 0));
        }
    }

    public Task<EmailDispatchPreHandoffReleaseOutcome> ReleaseClaimBeforeProviderHandoff(
        EmailDispatchPreHandoffRelease request,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (dispatch.TenantId == request.TenantId
                && dispatch.Id == request.OutboxId
                && dispatch.Status == EmailDispatchStatus.Processing
                && dispatch.ProcessingLeaseToken == request.ProcessingLeaseToken
                && dispatch.AttemptCount == request.AttemptNumber)
            {
                dispatch.Status = EmailDispatchStatus.RetryScheduled;
                dispatch.NextAttemptAt = request.ReleasedAt;
                dispatch.ProcessingStartedAt = null;
                dispatch.ProcessingLeaseToken = null;
                dispatch.LastFailureCategory = request.FailureCategory;
                dispatch.LastError = request.FailureMessage;
                return Task.FromResult(EmailDispatchPreHandoffReleaseOutcome.Released);
            }

            return Task.FromResult(dispatch.AttemptCount > request.AttemptNumber
                ? EmailDispatchPreHandoffReleaseOutcome.ProviderHandoffFenced
                : EmailDispatchPreHandoffReleaseOutcome.LostClaim);
        }
    }

    public Task MarkRabbitMqPublishSucceeded(Guid id, DateTime publishedAt, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task MarkRabbitMqPublishFailed(
        Guid id,
        string failureCategory,
        DateTime attemptedAt,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task SettleProviderAccepted(
        EmailDispatchAcceptedSettlement settlement,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (dispatch.TenantId != settlement.TenantId
                || dispatch.Id != settlement.OutboxId
                || dispatch.Status != EmailDispatchStatus.Processing
                || dispatch.ProcessingLeaseToken != settlement.ProcessingLeaseToken
                || dispatch.AttemptCount != settlement.AttemptNumber)
            {
                throw new InvalidOperationException("The email dispatch settlement claim fence is stale.");
            }

            var attempt = Attempts.Single(value =>
                value.EmailDispatchOutboxId == settlement.OutboxId &&
                value.AttemptNumber == settlement.AttemptNumber);
            attempt.Outcome = EmailDispatchAttemptOutcome.Succeeded;
            attempt.CompletedAt = settlement.SettledAt;
            attempt.FailureCategory = null;
            attempt.SanitizedErrorMessage = null;
            attempt.ProviderMessageId = settlement.ProviderMessageId;

            dispatch.Status = EmailDispatchStatus.Sent;
            dispatch.SentAt = settlement.SettledAt;
            dispatch.ProviderMessageId = settlement.ProviderMessageId;
            dispatch.ProcessingLeaseToken = null;
            dispatch.ProcessingStartedAt = null;

            var receipt = Receipts.Single(value => value.EmailDispatchOutboxId == settlement.OutboxId);
            receipt.Status = EmailDispatchReceiptStatus.Completed;
            receipt.CompletedAt = settlement.SettledAt;
            receipt.ProviderMessageId = settlement.ProviderMessageId;
        }

        return Task.CompletedTask;
    }

    public Task<EmailDispatchFailureSettlementOutcome> SettleProviderFailure(
        EmailDispatchFailureSettlement settlement,
        CancellationToken cancellationToken)
    {
        if (settlement.Outcome is not (SmtpDeliveryOutcome.TransientFailure or SmtpDeliveryOutcome.ConfigurationFailure))
            throw new ArgumentOutOfRangeException(nameof(settlement), settlement.Outcome, "Only definite SMTP failures can be settled.");
        lock (_gate)
        {
            if (dispatch.TenantId != settlement.TenantId
                || dispatch.Id != settlement.OutboxId
                || dispatch.Status != EmailDispatchStatus.Processing
                || dispatch.ProcessingLeaseToken != settlement.ProcessingLeaseToken
                || dispatch.AttemptCount != settlement.AttemptNumber)
            {
                return Task.FromResult(EmailDispatchFailureSettlementOutcome.StaleClaim);
            }

            var parked = settlement.Outcome == SmtpDeliveryOutcome.ConfigurationFailure;
            var exhausted = !parked && settlement.AttemptNumber >= settlement.MaxAttempts;
            dispatch.Status = parked ? EmailDispatchStatus.Parked
                : exhausted ? EmailDispatchStatus.DeadLettered : EmailDispatchStatus.RetryScheduled;
            dispatch.ParkedAt = parked ? settlement.SettledAt : null;
            dispatch.ParkReason = parked ? EmailDispatchParkReason.CapabilityUnavailable : null;
            dispatch.DeadLetteredAt = exhausted ? settlement.SettledAt : null;
            dispatch.NextAttemptAt = parked || exhausted ? null : settlement.SettledAt.Add(settlement.RetryDelay);
            dispatch.ProcessingLeaseToken = null;
            dispatch.ProcessingStartedAt = null;
            dispatch.LastFailureCategory = settlement.FailureCategory;
            dispatch.LastError = settlement.FailureMessage;

            var attempt = Attempts.Single(value =>
                value.EmailDispatchOutboxId == settlement.OutboxId
                && value.AttemptNumber == settlement.AttemptNumber);
            attempt.Outcome = EmailDispatchAttemptOutcome.Failed;
            attempt.CompletedAt = settlement.SettledAt;
            attempt.FailureCategory = settlement.FailureCategory;
            attempt.SanitizedErrorMessage = settlement.FailureMessage;

            var receipt = Receipts.Single(value => value.EmailDispatchOutboxId == settlement.OutboxId);
            receipt.Status = EmailDispatchReceiptStatus.Failed;
            receipt.FailedAt = settlement.SettledAt;
            receipt.FailureCode = settlement.FailureCategory;
            receipt.FailureMessage = settlement.FailureMessage;
            return Task.FromResult(parked
                ? EmailDispatchFailureSettlementOutcome.Parked
                : exhausted
                ? EmailDispatchFailureSettlementOutcome.DeadLettered
                : EmailDispatchFailureSettlementOutcome.RetryScheduled);
        }
    }

    public Task<EmailDispatchAcceptedReconciliationOutcome> ReconcileProviderAccepted(
        EmailDispatchAcceptedSettlement settlement,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (dispatch.Status == EmailDispatchStatus.Sent &&
                Attempts.Any(value => value.EmailDispatchOutboxId == settlement.OutboxId &&
                    value.AttemptNumber == settlement.AttemptNumber &&
                    value.Outcome == EmailDispatchAttemptOutcome.Succeeded) &&
                Receipts.Any(value => value.EmailDispatchOutboxId == settlement.OutboxId &&
                    value.Status == EmailDispatchReceiptStatus.Completed))
            {
                return Task.FromResult(EmailDispatchAcceptedReconciliationOutcome.Sent);
            }

            if (dispatch.Status != EmailDispatchStatus.Processing
                || dispatch.ProcessingLeaseToken != settlement.ProcessingLeaseToken
                || dispatch.AttemptCount != settlement.AttemptNumber)
            {
                return Task.FromResult(EmailDispatchAcceptedReconciliationOutcome.StaleClaim);
            }

            var attempt = Attempts.Single(value =>
                value.EmailDispatchOutboxId == settlement.OutboxId &&
                value.AttemptNumber == settlement.AttemptNumber);
            attempt.Outcome = EmailDispatchAttemptOutcome.Unknown;
            attempt.CompletedAt = settlement.SettledAt;
            attempt.FailureCategory = "smtp_outcome_unknown";
            attempt.SanitizedErrorMessage = "SMTP acceptance or local settlement could not be confirmed. Automatic resend is disabled pending reconciliation.";
            attempt.ProviderMessageId = null;

            dispatch.Status = EmailDispatchStatus.Unknown;
            dispatch.SentAt = null;
            dispatch.UnknownAt = settlement.SettledAt;
            dispatch.ProviderMessageId = null;
            dispatch.NextAttemptAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.ProcessingStartedAt = null;

            var receipt = Receipts.Single(value => value.EmailDispatchOutboxId == settlement.OutboxId);
            receipt.Status = EmailDispatchReceiptStatus.Unknown;
            receipt.CompletedAt = null;
            receipt.FailedAt = settlement.SettledAt;
            receipt.ProviderMessageId = null;
            receipt.FailureCode = "smtp_outcome_unknown";
        }

        return Task.FromResult(EmailDispatchAcceptedReconciliationOutcome.Unknown);
    }

    private bool IsRetentionEligible(DateTime cutoffUtc) =>
        dispatch.ContentRedactedAt is null &&
        (dispatch.Status == EmailDispatchStatus.Sent && dispatch.SentAt <= cutoffUtc ||
         dispatch.Status == EmailDispatchStatus.Skipped && dispatch.LastFailureAt <= cutoffUtc);
}
