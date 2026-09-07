// ABOUTME: Atomically revalidates current recipient authority immediately before SMTP provider handoff.
// ABOUTME: Refreshes verified addresses or settles linked outbox, attempt, receipt, and delivery rows as skipped.

using System.Data;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class EmailDispatchEligibilityEvaluator(
    ExploreDbContext dbContext,
    NotificationDeliveryPolicyResolver policyResolver,
    INotificationPreferenceResolver preferenceResolver,
    ISettingMutationLock mutationLock) : IEmailDispatchEligibilityEvaluator
{
    private const string ProviderHandoffStarted = "provider_handoff_started";
    private const string ProviderHandoffMessage = "SMTP provider handoff started; automatic resend is suppressed until the attempt is durably settled.";
    private const string SkipMessage = "Email delivery was suppressed by current dispatch eligibility before provider handoff.";
    private const string SmtpProcessorCode = EmailDispatchOutboxRepository.SmtpProcessorCode;
    // ponytail: one non-PostgreSQL eligibility writer; shard only if measured dispatch throughput requires it.
    private const string EligibilityLockName = "email-dispatch-eligibility";
    private const string SmtpRateDeferred = "smtp_rate_deferred";
    private const string SmtpRateDeferredMessage = "SMTP dispatch was deferred by the persisted rate policy before provider handoff.";

    public async Task<EmailDispatchEligibilityResult> EvaluateAndBeginProviderHandoffAsync(
        EmailDispatchEligibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        return await mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            async token =>
            {
                dbContext.ChangeTracker.Clear();
                FanoutAuthorityHint? fanoutAuthorityHint = await LoadFanoutAuthorityHintAsync(request, token);
                dbContext.ChangeTracker.Clear();
                return await EvaluateWithinTransactionAsync(request, fanoutAuthorityHint, token);
            },
            cancellationToken);
    }

    private async Task<EmailDispatchEligibilityResult> EvaluateWithinTransactionAsync(
        EmailDispatchEligibilityRequest request,
        FanoutAuthorityHint? fanoutAuthorityHint,
        CancellationToken cancellationToken)
    {
        bool isPostgreSql =
            RelationalProviderClassifier.Classify(dbContext.Database) == RelationalProvider.PostgreSql;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            isPostgreSql ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable,
            cancellationToken);

        await using IAsyncDisposable eligibilityLease =
            await RelationalNamedLock.AcquireTransactionAsync(
                dbContext,
                EligibilityLockName,
                cancellationToken);

        await using IAsyncDisposable? eventPrecedenceLease =
            fanoutAuthorityHint is { EventId: { } lockEventId } authorityHint
            && (authorityHint.OccurrenceId.HasValue || authorityHint.Kind == EmailDispatchKind.EventReminder)
            ? await NotificationFanoutPrecedenceLock.AcquireAsync(
                dbContext,
                request.TenantId,
                lockEventId,
                cancellationToken)
            : null;

        var dispatch = await LoadClaimedDispatchAsync(request, cancellationToken);

        if (dispatch is null
            || dispatch.ContentRedactedAt is not null
            || dispatch.Status != EmailDispatchStatus.Processing
            || dispatch.ProcessingLeaseToken != request.ProcessingLeaseToken
            || dispatch.AttemptCount != request.AttemptNumber)
        {
            await transaction.CommitAsync(cancellationToken);
            return new EmailDispatchEligibilityResult(EmailDispatchEligibilityOutcome.LostClaim, null, null);
        }

        var delivery = await dbContext.NotificationDeliveries
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .Include(value => value.DeliveryPolicy)
            .Include(value => value.NotificationIntent)
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.EmailDispatchOutboxId == request.OutboxId &&
                value.ChannelId == (int)NotificationPreferenceChannelEnum.Email,
                cancellationToken);

        if (delivery is null || delivery.NotificationIntent is null || delivery.DeliveryPolicy is null)
        {
            return await SkipAsync(dispatch, delivery, request, "delivery_authority_missing", cancellationToken);
        }

        long originalPolicyRevision = delivery.NotificationIntent.EmailDeliveryPolicyRevision;
        if (fanoutAuthorityHint is { OccurrenceId: { } hintedOccurrenceId })
        {
            if (fanoutAuthorityHint.EventId is not Guid authorityEventId)
            {
                return await SkipAsync(
                    dispatch,
                    delivery,
                    request,
                    "fanout_occurrence_authority_missing",
                    cancellationToken);
            }

            if (delivery.NotificationIntent.FanoutOccurrenceId != hintedOccurrenceId
                || delivery.NotificationIntent.EventId != authorityEventId)
            {
                return await SkipAsync(
                    dispatch,
                    delivery,
                    request,
                    "fanout_occurrence_authority_mismatch",
                    cancellationToken);
            }

            NotificationFanoutOccurrence? occurrence = await dbContext.NotificationFanoutOccurrences
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
                .AsNoTracking()
                .SingleOrDefaultAsync(value =>
                    value.TenantId == request.TenantId
                    && value.Id == hintedOccurrenceId
                    && value.EventId == authorityEventId,
                    cancellationToken);
            if (occurrence is null)
            {
                return await SkipAsync(
                    dispatch,
                    delivery,
                    request,
                    "fanout_occurrence_authority_missing",
                    cancellationToken);
            }

            // Fanout graph creation may be delayed or repaired after email is re-enabled.
            // The validated occurrence, not the recipient graph's creation time, owns its history.
            originalPolicyRevision = occurrence.EmailDeliveryPolicyRevision;
            if (occurrence.State == NotificationFanoutOccurrenceState.Superseded)
            {
                return await SkipAsync(
                    dispatch,
                    delivery,
                    request,
                    NotificationFanoutEmailSuppressionReason.Code,
                    cancellationToken,
                    preserveSupersededDelivery: true,
                    message: NotificationFanoutEmailSuppressionReason.Message);
            }
        }
        else if (delivery.NotificationIntent.FanoutOccurrenceId.HasValue)
        {
            return await SkipAsync(
                dispatch,
                delivery,
                request,
                "fanout_occurrence_authority_mismatch",
                cancellationToken);
        }

        if (dispatch.Kind == EmailDispatchKind.EventReminder)
        {
            string? reminderSkipReason = await ResolveEventReminderAuthoritySkipReasonAsync(
                dispatch,
                delivery.NotificationIntent,
                request.EvaluatedAt,
                cancellationToken);
            if (reminderSkipReason is not null)
            {
                return await SkipAsync(
                    dispatch,
                    delivery,
                    request,
                    reminderSkipReason,
                    cancellationToken);
            }
        }

        var processorPaused = await dbContext.EmailDispatchProcessorStates
            .AsNoTracking()
            .AnyAsync(state => state.ProcessorCode == SmtpProcessorCode && state.IsPaused, cancellationToken);
        if (processorPaused)
        {
            dispatch.Status = EmailDispatchStatus.Pending;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.UpdatedAt = request.EvaluatedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new EmailDispatchEligibilityResult(EmailDispatchEligibilityOutcome.ProcessorPaused, null, "processor_paused");
        }

        if (delivery.StatusId == (int)NotificationDeliveryStatusEnum.Superseded)
        {
            return await SkipAsync(
                dispatch,
                delivery,
                request,
                "delivery_superseded",
                cancellationToken,
                preserveSupersededDelivery: true);
        }

        if (delivery.StatusId is not ((int)NotificationDeliveryStatusEnum.Pending)
            and not ((int)NotificationDeliveryStatusEnum.Queued))
        {
            return await SkipAsync(dispatch, delivery, request, "delivery_state_ineligible", cancellationToken);
        }

        // This retired informational invitation has no token/operation validity bound to recover.
        // Keep it terminal even when administrator authority and email capability are restored.
        if (dispatch.Kind == EmailDispatchKind.TenantAdministratorInvitation)
        {
            return await SkipAsync(
                dispatch, delivery, request, "managed_administrator_invitation_retired", cancellationToken);
        }

        var policy = policyResolver.Resolve(
            delivery.DeliveryPolicyId,
            delivery.DeliveryPolicy.MasterCode,
            delivery.PolicyVersion);
        if (!policy.IsSupported)
        {
            return await SkipAsync(dispatch, delivery, request, policy.SkipReason!, cancellationToken);
        }

        var tenantActive = await dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(tenant =>
                tenant.Id == request.TenantId &&
                tenant.TenantStatusId == (int)TenantStatusEnum.Active,
                cancellationToken);
        if (!tenantActive)
        {
            return await SkipAsync(dispatch, delivery, request, "tenant_inactive", cancellationToken);
        }

        var tenantControl = await dbContext.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(control => control.TenantId == request.TenantId)
            .Select(control => new { control.IsPaused, control.OptionalSuppressedThroughRevision })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenantControl?.IsPaused == true)
        {
            dispatch.Status = EmailDispatchStatus.Pending;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.UpdatedAt = request.EvaluatedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new EmailDispatchEligibilityResult(EmailDispatchEligibilityOutcome.TenantPaused, null, "tenant_paused");
        }

        var tenantUser = await dbContext.TenantUsers
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.UserId == dispatch.RecipientUserId,
                cancellationToken);
        if (tenantUser is null || tenantUser.StatusId != (int)TenantUserStatusEnum.Active)
        {
            return await SkipAsync(dispatch, delivery, request, "recipient_membership_inactive", cancellationToken);
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .Include(value => value.Pii)
            .SingleOrDefaultAsync(value => value.Id == dispatch.RecipientUserId, cancellationToken);
        if (user is null || user.IsDeleted)
        {
            return await SkipAsync(dispatch, delivery, request, "recipient_deleted", cancellationToken);
        }

        if (dispatch.Kind == EmailDispatchKind.OrganizerNotification
            && delivery.NotificationIntent.TemplateKey == EventOrganizerWarningNotificationFactory.TemplateKey)
        {
            if (dispatch.EventId is not Guid eventId)
            {
                return await SkipAsync(
                    dispatch,
                    delivery,
                    request,
                    "report_organizer_authority_missing",
                    cancellationToken);
            }

            DateTime authorityAtUtc =
                await RelationalDatabaseClock.GetUtcNowAsync(dbContext, cancellationToken);
            bool effectiveOwner = await dbContext.EventRoleAssignments
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
                .AsNoTracking()
                .AnyAsync(assignment =>
                    assignment.TenantId == request.TenantId
                    && assignment.EventId == eventId
                    && assignment.UserId == dispatch.RecipientUserId
                    && assignment.RoleId == (int)RoleEnum.EventOwner
                    && assignment.Status == EventRoleAssignmentStatus.Active
                    && assignment.StartsAtUtc <= authorityAtUtc
                    && (assignment.ExpiresAtUtc == null || assignment.ExpiresAtUtc > authorityAtUtc),
                    cancellationToken);
            if (!effectiveOwner)
            {
                return await SkipAsync(
                    dispatch,
                    delivery,
                    request,
                    "report_organizer_authority_inactive",
                    cancellationToken);
            }
        }

        string recipientEmail;
        if (policy.UsesInvitationDestination)
        {
            if (dispatch.RecipientAddressSource != RecipientAddressSource.ManagedTenantAdministratorInvitation
                || dispatch.ManagedTenantProvisioningOperationId is not Guid operationId)
            {
                return await SkipAsync(dispatch, delivery, request, "invitation_authority_missing", cancellationToken);
            }

            var invitationAuthorized = await dbContext.ManagedTenantProvisioningOperations
                .AsNoTracking()
                .AnyAsync(operation =>
                    operation.Id == operationId &&
                    operation.Status == ManagedTenantProvisioningStatus.Succeeded &&
                    operation.TenantId == request.TenantId &&
                    operation.TenantAdministratorUserId == dispatch.RecipientUserId,
                    cancellationToken)
                && await dbContext.TenantUserRoleGrants
                    .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
                    .AsNoTracking()
                    .AnyAsync(grant =>
                        grant.TenantId == request.TenantId
                        && grant.TenantUserId == tenantUser.Id
                        && grant.RoleId == (int)RoleEnum.TenantAdmin
                        && grant.RevokedAt == null,
                        cancellationToken);
            if (!invitationAuthorized || string.IsNullOrWhiteSpace(dispatch.RecipientEmail))
            {
                return await SkipAsync(dispatch, delivery, request, "invitation_authority_invalid", cancellationToken);
            }

            recipientEmail = dispatch.RecipientEmail.Trim();
        }
        else
        {
            if (dispatch.RecipientAddressSource != RecipientAddressSource.TenantUserVerifiedEmail)
            {
                return await SkipAsync(dispatch, delivery, request, "recipient_address_source_mismatch", cancellationToken);
            }

            RecipientEmailAddressResolution emailAddress = RecipientEmailAddressResolver.Resolve(
                user,
                dispatch.RecipientUserId);
            if (!emailAddress.HasVerifiedEmail)
            {
                return await SkipAsync(
                    dispatch,
                    delivery,
                    request,
                    emailAddress.SkipReason!,
                    cancellationToken);
            }

            recipientEmail = emailAddress.Email!;
        }

        var consentReason = await ResolveConsentSkipReasonAsync(policy, delivery, dispatch, cancellationToken);
        if (consentReason is not null)
        {
            return await SkipAsync(dispatch, delivery, request, consentReason, cancellationToken);
        }

        if (policy.HonorsPreference)
        {
            if (string.IsNullOrWhiteSpace(delivery.PreferenceCategoryCode))
            {
                return await SkipAsync(dispatch, delivery, request, "notification_preference_category_missing", cancellationToken);
            }

            var preference = await preferenceResolver.ResolveAsync(
                new NotificationPreferenceResolveRequest(
                    request.TenantId,
                    dispatch.RecipientUserId,
                    null,
                    null,
                    delivery.PreferenceCategoryCode,
                    NotificationPreferenceChannelCodes.Email),
                cancellationToken);
            if (!preference.IsEnabled)
            {
                return await SkipAsync(dispatch, delivery, request, "recipient_notification_preference_disabled", cancellationToken);
            }

            var legacyCategory = ResolveLegacyPreferenceCategory(dispatch.Kind);
            if (legacyCategory is not null)
            {
                var legacyDisabled = await dbContext.UserNotificationPreferences
                    .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
                    .AsNoTracking()
                    .AnyAsync(value =>
                        value.TenantId == request.TenantId &&
                        value.UserId == dispatch.RecipientUserId &&
                        value.Category == legacyCategory &&
                        !value.IsEnabled,
                        cancellationToken);
                if (legacyDisabled)
                {
                    return await SkipAsync(dispatch, delivery, request, "recipient_unsubscribed", cancellationToken);
                }
            }
        }

        var emailPolicy = await EmailDeliveryPolicyReader.ReadAsync(dbContext, request.TenantId, cancellationToken);
        // Never inherit the processor watermark: independently enabled tenants have their own history,
        // even if a later settings change moves them onto the instance transport.
        bool suppressOptional = EmailDeliveryPolicy.ShouldSuppressOptional(
            state: emailPolicy.State, honorsPreference: policy.HonorsPreference,
            occurrenceRevision: originalPolicyRevision,
            suppressedThroughRevision: tenantControl?.OptionalSuppressedThroughRevision);
        if (suppressOptional || emailPolicy.State != EmailDeliveryState.Available)
        {
            return await SettleUnavailablePolicyAsync(
                dispatch,
                delivery,
                request,
                skipOptional: suppressOptional,
                deliveryState: emailPolicy.State,
                cancellationToken);
        }

        var admission = await TryReserveSmtpRateAsync(dispatch, request, cancellationToken);
        if (admission.ProcessorPaused)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new EmailDispatchEligibilityResult(
                EmailDispatchEligibilityOutcome.ProcessorPaused,
                null,
                "processor_paused");
        }

        if (admission.TenantPaused)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new EmailDispatchEligibilityResult(
                EmailDispatchEligibilityOutcome.TenantPaused,
                null,
                "tenant_paused");
        }

        if (!admission.IsAcquired)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new EmailDispatchEligibilityResult(
                EmailDispatchEligibilityOutcome.RateDeferred,
                null,
                SmtpRateDeferred,
                RetryAt: admission.RetryAt);
        }

        dispatch.AttemptCount++;
        dispatch.RecipientEmail = recipientEmail;
        dispatch.UpdatedAt = admission.AdmittedAt;
        var attemptRequest = request with
        {
            AttemptNumber = dispatch.AttemptCount,
            EvaluatedAt = admission.AdmittedAt
        };
        var receipt = await UpsertReceiptAsync(dispatch, attemptRequest, EmailDispatchReceiptStatus.Processing, null, cancellationToken);
        await UpsertAttemptAsync(dispatch, attemptRequest, EmailDispatchAttemptOutcome.Unknown, ProviderHandoffStarted, ProviderHandoffMessage, null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new EmailDispatchEligibilityResult(
            EmailDispatchEligibilityOutcome.Eligible,
            recipientEmail,
            null,
            receipt.Id,
            dispatch.AttemptCount);
    }

    private Task<EmailDispatchOutbox?> LoadClaimedDispatchAsync(
        EmailDispatchEligibilityRequest request,
        CancellationToken cancellationToken)
        => dbContext.EmailDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId && value.Id == request.OutboxId,
                cancellationToken);

    private async Task<SmtpRateAdmission> TryReserveSmtpRateAsync(
        EmailDispatchOutbox dispatch,
        EmailDispatchEligibilityRequest request,
        CancellationToken cancellationToken)
    {
        await using IAsyncDisposable controlLease = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            EmailDispatchOutboxRepository.ClaimAdvisoryLockName,
            cancellationToken);
        await using IAsyncDisposable smtpRateLease = await RelationalNamedLock.AcquireTransactionAsync(
            dbContext,
            "email-dispatch-smtp-rate",
            cancellationToken);
        DateTime databaseNow =
            await RelationalDatabaseClock.GetUtcNowAsync(dbContext, cancellationToken);
        EmailDispatchProcessorState processorState;
        EmailDispatchTenantControl tenantControl;

        processorState = await dbContext.EmailDispatchProcessorStates
            .SingleOrDefaultAsync(
                state => state.ProcessorCode == SmtpProcessorCode,
                cancellationToken)
            ?? new EmailDispatchProcessorState
            {
                Id = Guid.CreateVersion7(),
                ProcessorCode = SmtpProcessorCode,
                UpdatedAt = databaseNow
            };
        if (dbContext.Entry(processorState).State == EntityState.Detached)
        {
            dbContext.EmailDispatchProcessorStates.Add(processorState);
        }

        tenantControl = await dbContext.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(
                control => control.TenantId == dispatch.TenantId,
                cancellationToken)
            ?? new EmailDispatchTenantControl
            {
                Id = Guid.CreateVersion7(),
                TenantId = dispatch.TenantId,
                CreatedAt = databaseNow,
                UpdatedAt = databaseNow
            };
        if (dbContext.Entry(tenantControl).State == EntityState.Detached)
        {
            dbContext.EmailDispatchTenantControls.Add(tenantControl);
        }

        if (processorState.IsPaused)
        {
            dispatch.Status = EmailDispatchStatus.Pending;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.UpdatedAt = databaseNow;
            return new SmtpRateAdmission(false, databaseNow, null, ProcessorPaused: true);
        }

        if (tenantControl.IsPaused)
        {
            dispatch.Status = EmailDispatchStatus.Pending;
            dispatch.ProcessingStartedAt = null;
            dispatch.ProcessingLeaseToken = null;
            dispatch.UpdatedAt = databaseNow;
            return new SmtpRateAdmission(false, databaseNow, null, TenantPaused: true);
        }

        var effectiveGlobalRate = processorState.GlobalSmtpRateLimitPerMinuteOverride
            ?? request.GlobalSmtpRateLimitPerMinute;
        var globalBucket = RefillBucket(
            processorState.SmtpAvailableTokens,
            processorState.SmtpRefillAt,
            effectiveGlobalRate,
            databaseNow);
        var tenantBucket = RefillBucket(
            tenantControl.SmtpAvailableTokens,
            tenantControl.SmtpRefillAt,
            request.TenantSmtpRateLimitPerMinute,
            databaseNow);
        var acquired = globalBucket.AvailableTokens > 0 && tenantBucket.AvailableTokens > 0;

        processorState.SmtpAvailableTokens = acquired ? globalBucket.AvailableTokens - 1 : globalBucket.AvailableTokens;
        processorState.SmtpRefillAt = globalBucket.RefillAt;
        processorState.UpdatedAt = databaseNow;
        tenantControl.SmtpAvailableTokens = acquired ? tenantBucket.AvailableTokens - 1 : tenantBucket.AvailableTokens;
        tenantControl.SmtpRefillAt = tenantBucket.RefillAt;
        tenantControl.UpdatedAt = databaseNow;

        if (acquired)
        {
            return new SmtpRateAdmission(true, databaseNow, null);
        }

        var retryAt = new[]
        {
            globalBucket.AvailableTokens == 0 ? globalBucket.RefillAt : databaseNow,
            tenantBucket.AvailableTokens == 0 ? tenantBucket.RefillAt : databaseNow
        }.Max();
        dispatch.Status = EmailDispatchStatus.RetryScheduled;
        dispatch.NextAttemptAt = retryAt;
        dispatch.ProcessingStartedAt = null;
        dispatch.ProcessingLeaseToken = null;
        dispatch.LastFailureCategory = SmtpRateDeferred;
        dispatch.LastError = SmtpRateDeferredMessage;
        dispatch.LastFailureAt = databaseNow;
        dispatch.UpdatedAt = databaseNow;
        return new SmtpRateAdmission(false, databaseNow, retryAt, ProcessorPaused: false);
    }

    private static SmtpTokenBucket RefillBucket(
        int? availableTokens,
        DateTime? refillAt,
        int ratePerMinute,
        DateTime databaseNow)
    {
        if (availableTokens is null || refillAt is null || databaseNow >= refillAt.Value)
        {
            return new SmtpTokenBucket(ratePerMinute, databaseNow.AddMinutes(1));
        }

        return new SmtpTokenBucket(Math.Min(availableTokens.Value, ratePerMinute), refillAt.Value);
    }

    private async Task<string?> ResolveConsentSkipReasonAsync(
        NotificationDeliveryPolicyResolution policy,
        NotificationDelivery delivery,
        EmailDispatchOutbox dispatch,
        CancellationToken cancellationToken)
    {
        if (policy.ConsentRequirement == EmailDispatchConsentRequirement.None)
        {
            return null;
        }

        if (delivery.NotificationIntent?.ReportId is not Guid reportId)
        {
            return "report_consent_source_missing";
        }

        var report = await dbContext.EventReports
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .AsNoTracking()
            .SingleOrDefaultAsync(value =>
                value.TenantId == dispatch.TenantId &&
                value.Id == reportId &&
                value.ReporterUserId == dispatch.RecipientUserId,
                cancellationToken);
        if (report is null)
        {
            return "report_consent_source_missing";
        }

        return policy.ConsentRequirement switch
        {
            EmailDispatchConsentRequirement.ReportCaseUpdates when
                !string.Equals(delivery.ConsentPurpose, ReportEmailConsentPurposeCodes.CaseUpdates, StringComparison.Ordinal)
                => "report_consent_purpose_mismatch",
            EmailDispatchConsentRequirement.ReportCaseUpdates when !report.ReportCaseUpdatesConsent
                => "report_case_update_consent_withdrawn",
            EmailDispatchConsentRequirement.ReportFollowUpContact when
                !string.Equals(delivery.ConsentPurpose, ReportEmailConsentPurposeCodes.FollowUpContact, StringComparison.Ordinal)
                => "report_consent_purpose_mismatch",
            EmailDispatchConsentRequirement.ReportFollowUpContact when !report.ReportFollowUpContactConsent
                => "report_follow_up_consent_withdrawn",
            _ => null
        };
    }

    private async Task<string?> ResolveEventReminderAuthoritySkipReasonAsync(
        EmailDispatchOutbox dispatch,
        NotificationIntent intent,
        DateTime evaluatedAt,
        CancellationToken cancellationToken)
    {
        if (dispatch.EventId is not Guid eventId
            || dispatch.RegistrationOrderId is not Guid registrationOrderId
            || intent.TemplateKey != "event.reminder"
            || intent.EventId != eventId
            || intent.RecipientUserId != dispatch.RecipientUserId
            || !EventReminderAuthorityReference.TryParse(
                dispatch.CorrelationId,
                out Guid scheduledSessionId,
                out DateTimeOffset scheduledStartUtc,
                out string scheduledTimeZoneId)
            || !string.Equals(
                intent.SafePayloadReference,
                $"registration-order:{registrationOrderId:N}:session:{scheduledSessionId:N}",
                StringComparison.Ordinal))
        {
            return "event_reminder_authority_missing";
        }

        var eventTimeZone = await dbContext.Events
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(value =>
                value.TenantId == dispatch.TenantId
                && value.Id == eventId
                && !value.IsDeleted
                && value.EventStatusId == (int)EventStatusEnum.Published)
            .Select(value => new { value.EventTimeZoneId, value.Timezone })
            .SingleOrDefaultAsync(cancellationToken);
        if (eventTimeZone is null)
        {
            return "event_reminder_authority_inactive";
        }

        string currentTimeZoneId;
        try
        {
            currentTimeZoneId = ScheduleTimeZoneResolver.NormalizeOrUtc(
                eventTimeZone.EventTimeZoneId ?? eventTimeZone.Timezone);
        }
        catch (ArgumentException)
        {
            return "event_reminder_timezone_invalid";
        }

        if (!string.Equals(currentTimeZoneId, scheduledTimeZoneId, StringComparison.Ordinal))
        {
            return "event_reminder_authority_changed";
        }

        bool registrationAuthorityActive = await dbContext.RegistrationOrders
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .AsNoTracking()
            .AnyAsync(parent =>
                parent.TenantId == dispatch.TenantId
                && parent.Id == registrationOrderId
                && parent.EventId == eventId
                && parent.AccountUserId == dispatch.RecipientUserId
                && !parent.IsDeleted
                && parent.RegistrationOrderStatusId == (int)RegistrationOrderStatusEnum.Confirmed
                && !dbContext.EventModerationRecords
                    .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
                    .Any(record =>
                        record.TenantId == dispatch.TenantId
                        && record.EventId == eventId
                        && record.ActionKind == EventModerationActionKind.HeavyRedacted
                        && record.IsIrreversible),
                cancellationToken);
        if (!registrationAuthorityActive)
        {
            return "event_reminder_authority_inactive";
        }

        DateTimeOffset cutoffUtc = new(
            evaluatedAt.Kind == DateTimeKind.Utc ? evaluatedAt : evaluatedAt.ToUniversalTime());
        var currentSession = await (
                from child in dbContext.EventRegistrations
                    .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
                    .AsNoTracking()
                join session in dbContext.EventSessions
                    .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
                    .AsNoTracking()
                    on new { child.TenantId, Id = child.EventSessionId }
                    equals new { session.TenantId, Id = session.Id }
                where child.TenantId == dispatch.TenantId
                       && child.RegistrationOrderId == registrationOrderId
                      && child.EventId == eventId
                      && child.LinkedUserId == dispatch.RecipientUserId
                      && !child.IsDeleted
                      && child.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                      && session.EventId == eventId
                      && !session.IsDeleted
                      && session.EventSessionStatusId == (int)EventSessionStatusEnum.Published
                      && session.StartTime.HasValue
                      && session.StartTime.Value > cutoffUtc
                orderby session.StartTime, session.Id
                select new { SessionId = session.Id, SessionStart = session.StartTime!.Value })
            .FirstOrDefaultAsync(cancellationToken);

        return currentSession is null
            || currentSession.SessionId != scheduledSessionId
            || currentSession.SessionStart.ToUniversalTime() != scheduledStartUtc
                ? "event_reminder_authority_changed"
                : null;
    }

    private async Task<EmailDispatchEligibilityResult> SettleUnavailablePolicyAsync(
        EmailDispatchOutbox dispatch,
        NotificationDelivery delivery,
        EmailDispatchEligibilityRequest request,
        bool skipOptional,
        EmailDeliveryState deliveryState,
        CancellationToken cancellationToken)
    {
        var reason = skipOptional && deliveryState is (EmailDeliveryState.Disabled or EmailDeliveryState.Available)
            ? "email_delivery_disabled" : "email_capability_unavailable";
        dispatch.Status = skipOptional ? EmailDispatchStatus.Skipped : EmailDispatchStatus.Parked;
        dispatch.ParkedAt = skipOptional ? null : request.EvaluatedAt;
        dispatch.ParkReason = skipOptional ? null : EmailDispatchParkReason.CapabilityUnavailable;
        dispatch.NextAttemptAt = null;
        dispatch.ProcessingStartedAt = null;
        dispatch.ProcessingLeaseToken = null;
        dispatch.LastFailureCategory = reason;
        dispatch.LastError = "Email delivery was suppressed before SMTP handoff by the current delivery policy.";
        dispatch.LastFailureAt = request.EvaluatedAt;
        dispatch.UpdatedAt = request.EvaluatedAt;
        delivery.StatusId = skipOptional
            ? (int)NotificationDeliveryStatusEnum.Skipped
            : (int)NotificationDeliveryStatusEnum.Parked;
        delivery.ProviderStatus = skipOptional ? "skipped" : "parked";
        delivery.FailureCategory = reason;
        delivery.CompletedAt = skipOptional ? request.EvaluatedAt : null;
        delivery.UpdatedAt = request.EvaluatedAt;

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Database.CommitTransactionAsync(cancellationToken);
        return new EmailDispatchEligibilityResult(
            Outcome: skipOptional ? EmailDispatchEligibilityOutcome.Skipped : EmailDispatchEligibilityOutcome.Parked,
            RecipientEmail: null,
            SkipReason: reason);
    }

    private async Task<EmailDispatchEligibilityResult> SkipAsync(
        EmailDispatchOutbox dispatch,
        NotificationDelivery? delivery,
        EmailDispatchEligibilityRequest request,
        string reason,
        CancellationToken cancellationToken,
        bool preserveSupersededDelivery = false,
        string message = SkipMessage)
    {
        dispatch.AttemptCount++;
        request = request with { AttemptNumber = dispatch.AttemptCount };
        dispatch.Status = EmailDispatchStatus.Skipped;
        dispatch.NextAttemptAt = null;
        dispatch.ProcessingStartedAt = null;
        dispatch.ProcessingLeaseToken = null;
        dispatch.LastFailureCategory = reason;
        dispatch.LastError = message;
        dispatch.LastFailureAt = request.EvaluatedAt;
        dispatch.UpdatedAt = request.EvaluatedAt;

        if (delivery is not null)
        {
            delivery.StatusId = preserveSupersededDelivery
                ? (int)NotificationDeliveryStatusEnum.Superseded
                : (int)NotificationDeliveryStatusEnum.Skipped;
            delivery.ProviderStatus = preserveSupersededDelivery
                ? NotificationFanoutEmailSuppressionReason.ProviderStatus
                : "skipped";
            delivery.FailureCategory = reason;
            delivery.CompletedAt = request.EvaluatedAt;
            delivery.UpdatedAt = request.EvaluatedAt;
        }

        var receipt = await UpsertReceiptAsync(dispatch, request, EmailDispatchReceiptStatus.Skipped, reason, cancellationToken);
        await UpsertAttemptAsync(
            dispatch,
            request,
            EmailDispatchAttemptOutcome.Skipped,
            reason,
            message,
            request.EvaluatedAt,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Database.CommitTransactionAsync(cancellationToken);
        return new EmailDispatchEligibilityResult(
            EmailDispatchEligibilityOutcome.Skipped,
            null,
            reason,
            receipt.Id,
            dispatch.AttemptCount);
    }

    private async Task<EmailDispatchReceipt> UpsertReceiptAsync(
        EmailDispatchOutbox dispatch,
        EmailDispatchEligibilityRequest request,
        EmailDispatchReceiptStatus status,
        string? reason,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.EmailDispatchReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.EmailDispatchOutboxId == request.OutboxId,
                cancellationToken);
        if (receipt is null)
        {
            receipt = new EmailDispatchReceipt
            {
                Id = Guid.CreateVersion7(),
                TenantId = request.TenantId,
                PublishEventId = dispatch.PublishEventId,
                EmailDispatchOutboxId = dispatch.Id,
                ConsumerId = request.ConsumerId,
                FirstSeenAt = request.EvaluatedAt,
                CreatedAt = request.EvaluatedAt
            };
            await dbContext.EmailDispatchReceipts.AddAsync(receipt, cancellationToken);
        }

        receipt.Status = status;
        receipt.ConsumerId = request.ConsumerId;
        receipt.ProcessingStartedAt = status == EmailDispatchReceiptStatus.Processing ? request.EvaluatedAt : null;
        receipt.CompletedAt = null;
        receipt.FailedAt = status == EmailDispatchReceiptStatus.Skipped ? request.EvaluatedAt : null;
        receipt.FailureCode = reason;
        receipt.FailureMessage = reason is null ? null : SkipMessage;
        receipt.ProviderMessageId = null;
        receipt.UpdatedAt = request.EvaluatedAt;
        return receipt;
    }

    private async Task UpsertAttemptAsync(
        EmailDispatchOutbox dispatch,
        EmailDispatchEligibilityRequest request,
        EmailDispatchAttemptOutcome outcome,
        string failureCategory,
        string message,
        DateTime? completedAt,
        CancellationToken cancellationToken)
    {
        var attempt = await dbContext.EmailDispatchAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .SingleOrDefaultAsync(value =>
                value.TenantId == request.TenantId &&
                value.EmailDispatchOutboxId == request.OutboxId &&
                value.AttemptNumber == request.AttemptNumber,
                cancellationToken);
        if (attempt is null)
        {
            attempt = new EmailDispatchAttempt
            {
                Id = Guid.CreateVersion7(),
                TenantId = request.TenantId,
                EmailDispatchOutboxId = dispatch.Id,
                AttemptNumber = request.AttemptNumber,
                StartedAt = request.EvaluatedAt,
                CreatedAt = request.EvaluatedAt
            };
            await dbContext.EmailDispatchAttempts.AddAsync(attempt, cancellationToken);
        }

        attempt.Outcome = outcome;
        attempt.CompletedAt = completedAt;
        attempt.FailureCategory = failureCategory;
        attempt.SanitizedErrorMessage = message;
        attempt.ProviderMessageId = null;
        attempt.CorrelationId = dispatch.CorrelationId;
        attempt.UpdatedAt = request.EvaluatedAt;
    }

    private static string? ResolveLegacyPreferenceCategory(EmailDispatchKind kind) => kind switch
    {
        EmailDispatchKind.RegistrationConfirmation => NotificationPreferenceCategories.RegistrationConfirmations,
        EmailDispatchKind.EventReminder => NotificationPreferenceCategories.EventReminders,
        EmailDispatchKind.OrganizerNotification => NotificationPreferenceCategories.OrganizerAnnouncements,
        EmailDispatchKind.RegistrationApproved
            or EmailDispatchKind.RegistrationRejected
            or EmailDispatchKind.WaitlistPromoted
            or EmailDispatchKind.RegistrationCancelled
            or EmailDispatchKind.RegistrationRevoked
            or EmailDispatchKind.EventCancelled
            or EmailDispatchKind.EventUpdated => NotificationPreferenceCategories.EventUpdates,
        _ => null
    };

    private Task<FanoutAuthorityHint?> LoadFanoutAuthorityHintAsync(
        EmailDispatchEligibilityRequest request,
        CancellationToken cancellationToken)
    {
        return dbContext.NotificationDeliveries
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(value =>
                value.TenantId == request.TenantId
                && value.EmailDispatchOutboxId == request.OutboxId
                && value.ChannelId == (int)NotificationPreferenceChannelEnum.Email)
            .Select(value => new FanoutAuthorityHint(
                value.NotificationIntent!.FanoutOccurrenceId,
                value.NotificationIntent.EventId,
                value.EmailDispatchOutbox!.Kind))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private sealed record SmtpRateAdmission(
        bool IsAcquired,
        DateTime AdmittedAt,
        DateTime? RetryAt,
        bool ProcessorPaused = false,
        bool TenantPaused = false);
    private sealed record SmtpTokenBucket(int AvailableTokens, DateTime RefillAt);
    private sealed record FanoutAuthorityHint(Guid? OccurrenceId, Guid? EventId, EmailDispatchKind Kind);
}
