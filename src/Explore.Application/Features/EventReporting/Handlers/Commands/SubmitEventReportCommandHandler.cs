// ABOUTME: Handles authenticated event-report submissions with duplicate, quota, and status checks.
// ABOUTME: Persists report metadata, target, encrypted evidence, local case, and outbox intent atomically.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Policies;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class SubmitEventReportCommandHandler(
    IEventRepository eventRepository,
    IEventReportRepository eventReportRepository,
    IGenericRepository<EventReportTarget, Guid> targetRepository,
    IGenericRepository<EventReportEvidence, Guid> evidenceRepository,
    IGenericRepository<EventReportCase, Guid> caseRepository,
    IActorRepository actorRepository,
    IUserRepository userRepository,
    IOutboxRepository outboxRepository,
    INotificationPreferenceResolver notificationPreferenceResolver,
    IRecipientNotificationMaterializer recipientNotificationMaterializer,
    ReportReceiptNotificationFactory reportReceiptNotificationFactory,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IEventReportingIntakeGuard intakeGuard,
    ICurrentUserService currentUserService,
    IPrivacyErasureStateRepository privacyErasureStateRepository,
    IEventReportEvidenceProtector evidenceProtector,
    BusinessMetrics metrics,
    IOptions<EventReportSubmissionOptions> optionsAccessor,
    ISettingMutationLock mutationLock) : IRequestHandler<SubmitEventReportCommand, BaseCommandResponse<Guid>>
{
    private const string PrivacyErasureFencedFailureCode = "privacy_erasure_fenced";

    public async Task<BaseCommandResponse<Guid>> Handle(SubmitEventReportCommand request, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return Failure(
                null,
                Guid.Empty,
                "Tenant context could not be resolved.",
                ["Tenant context is required."],
                EventReportFailureCodes.TenantUnresolved,
                "tenant_unresolved");
        }

        if (!Enum.IsDefined(request.SubmissionChannel))
        {
            return Failure(
                tenantId,
                Guid.Empty,
                "Event report submission channel is invalid.",
                ["Event report submission channel is invalid."],
                "event_report_submission_channel_invalid",
                "validation_failed");
        }

        Guid? reporterUserId = currentUserService.UserId;
        if (await IsFencedAsync(reporterUserId, cancellationToken))
            return FencedFailure();

        if (request.Request is null)
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    tenantId,
                    Guid.Empty,
                    "Event report submission failed due to validation errors.",
                    ["Request is required."],
                    EventReportFailureCodes.ValidationFailed,
                    "validation_failed"),
                cancellationToken);
        }

        if (request.SubmissionChannel == EventReportSubmissionChannel.General)
        {
            EventReportingIntakeDecision intakeDecision =
                await intakeGuard.ResolveAsync(tenantId, cancellationToken);
            if (!intakeDecision.IntakeEnabled)
            {
                return await MaskIfFencedAsync(
                    reporterUserId,
                    Failure(
                        tenantId,
                        Guid.Empty,
                        intakeDecision.Message,
                        [intakeDecision.Message],
                        intakeDecision.ReasonCode,
                        "intake_disabled"),
                    cancellationToken);
            }
        }

        SubmitEventReportDto submission = request.Request with
        {
            SubcategoryCode = ResolveSubcategory(
                request.SubmissionChannel,
                request.Request.SubcategoryCode)
        };
        SubmitEventReportCommand canonicalRequest = request with
        {
            Request = submission
        };
        var options = NormalizeOptions(optionsAccessor.Value);

        var validationResult = await new SubmitEventReportCommandValidator(options)
            .ValidateAsync(canonicalRequest, cancellationToken);
        if (!validationResult.IsValid)
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    null,
                    Guid.Empty,
                    "Event report submission failed due to validation errors.",
                    validationResult.Errors.Select(error => error.ErrorMessage),
                    EventReportReasonCodePolicy.InvalidReasonCodeFailureCode,
                    "validation_failed"),
                cancellationToken);
        }
        if (options.RequireAuthenticatedReporter && reporterUserId is null)
        {
            return Failure(
                tenantId,
                Guid.Empty,
                "Reporter user could not be resolved.",
                ["Authenticated reporter user id is required."],
                EventReportFailureCodes.UserUnresolved,
                "user_unresolved");
        }

        var @event = await eventRepository.GetById(submission.EventId);
        if (@event is null || @event.TenantId != tenantId)
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    tenantId,
                    Guid.Empty,
                    "Event was not found.",
                    ["Event was not found."],
                    EventReportFailureCodes.EventNotFound,
                    "event_not_found"),
                cancellationToken);
        }

        if (!await eventRepository.IsPubliclyEligibleAsync(
                tenantId,
                @event.Id,
                cancellationToken))
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    tenantId,
                    Guid.Empty,
                    "Event was not found.",
                    ["Event was not found."],
                    EventReportFailureCodes.EventNotFound,
                    "event_not_found"),
                cancellationToken);
        }

        if (@event.EventStatusId != (int)EventStatusEnum.Published)
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    tenantId,
                    Guid.Empty,
                    "Only published events can be reported.",
                    ["Only published events can be reported."],
                    EventReportFailureCodes.EventInvalidStatus,
                    "invalid_status"),
                cancellationToken);
        }

        var reporterActor = reporterUserId.HasValue
            ? await actorRepository.GetActorByUserIdAndTenantId(reporterUserId.Value, tenantId, cancellationToken)
            : null;

        if (reporterUserId.HasValue && reporterActor is null)
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    tenantId,
                    Guid.Empty,
                    "Reporter actor could not be resolved.",
                    ["Reporter actor is required."],
                    EventReportFailureCodes.ReporterActorUnresolved,
                    "actor_unresolved"),
                cancellationToken);
        }

        if (!EventReportReasonCodePolicy.TryNormalize(
                submission.ReasonCode,
                out var reasonCode,
                out var reasonError))
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    tenantId,
                    Guid.Empty,
                    "Reason code is invalid.",
                    [reasonError ?? "ReasonCode is invalid."],
                    EventReportReasonCodePolicy.InvalidReasonCodeFailureCode,
                    "validation_failed"),
                cancellationToken);
        }

        var now = DateTime.UtcNow;
        return await mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            policyToken => unitOfWork.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            if (await IsFencedAsync(reporterUserId, transactionCancellationToken))
                return FencedFailure();

            if (!await eventRepository.IsPubliclyEligibleAsync(
                    tenantId,
                    @event.Id,
                    transactionCancellationToken))
            {
                return Failure(
                    tenantId,
                    Guid.Empty,
                    "Event was not found.",
                    ["Event was not found."],
                    EventReportFailureCodes.EventNotFound,
                    "event_not_found");
            }

            var duplicateWindowStart =
                now.AddHours(-options.DuplicateWindowHours);
            var duplicateExists = await eventReportRepository
                .ExistsByReporterAndEventAsync(
                    tenantId,
                    @event.Id,
                    reporterUserId,
                    reporterActor?.Id,
                    request.ReporterIpHash,
                    request.ReporterUserAgentHash,
                    reasonCode,
                    submission.SubcategoryCode,
                    duplicateWindowStart,
                    transactionCancellationToken);

            if (duplicateExists)
            {
                return await MaskIfFencedAsync(
                    reporterUserId,
                    Failure(
                        tenantId,
                        Guid.Empty,
                        "A matching event report was already submitted recently.",
                        ["A matching report already exists in the duplicate prevention window."],
                        EventReportFailureCodes.Duplicate,
                        "duplicate"),
                    transactionCancellationToken);
            }

            var reporterWindowStart = now.AddHours(-1);
            var reporterCount = await eventReportRepository
                .CountByReporterSinceAsync(
                    tenantId,
                    reporterUserId,
                    reporterActor?.Id,
                    request.ReporterIpHash,
                    request.ReporterUserAgentHash,
                    reporterWindowStart,
                    transactionCancellationToken);

            if (options.MaxReportsPerUserPerHour > 0
                && reporterCount >= options.MaxReportsPerUserPerHour)
            {
                metrics.RecordEventReportSubmission(
                    tenantId.ToString(),
                    "failed",
                    FailureCodes.QuotaExceeded);
                BaseCommandResponse<Guid> response = BaseCommandResponse.Quota<Guid>(
                    "Reporter event-report quota exceeded.",
                    new QuotaExceededDetails(
                        "reporting.max_reports_per_user_per_hour",
                        options.MaxReportsPerUserPerHour,
                        reporterCount,
                        reporterCount + 1,
                        "event_report_reporter",
                        tenantId));
                return await MaskIfFencedAsync(
                    reporterUserId,
                    response,
                    transactionCancellationToken);
            }

            var reporterEventWindowStart = now.AddDays(-1);
            var reporterEventCount = await eventReportRepository
                .CountByReporterAndEventSinceAsync(
                    tenantId,
                    @event.Id,
                    reporterUserId,
                    reporterActor?.Id,
                    request.ReporterIpHash,
                    request.ReporterUserAgentHash,
                    reporterEventWindowStart,
                    transactionCancellationToken);
            if (options.MaxReportsPerEventPerUserPerDay > 0
                && reporterEventCount
                    >= options.MaxReportsPerEventPerUserPerDay)
            {
                metrics.RecordEventReportSubmission(
                    tenantId.ToString(),
                    "failed",
                    FailureCodes.QuotaExceeded);
                BaseCommandResponse<Guid> response = BaseCommandResponse.Quota<Guid>(
                    "Event report quota exceeded.",
                    new QuotaExceededDetails(
                        "reporting.max_reports_per_event_per_user_per_day",
                        options.MaxReportsPerEventPerUserPerDay,
                        reporterEventCount,
                        reporterCount + 1,
                        "event_report_reporter_event",
                        tenantId));
                return await MaskIfFencedAsync(
                    reporterUserId,
                    response,
                    transactionCancellationToken);
            }

            var reporterUser = reporterUserId.HasValue
                ? await userRepository.GetUserWithDetails(reporterUserId.Value, transactionCancellationToken)
                : null;
            if (reporterUserId.HasValue)
            {
                if (reporterUser is null || reporterUser.Id != reporterUserId.Value || reporterUser.IsDeleted)
                {
                    return await MaskIfFencedAsync(
                        reporterUserId,
                        Failure(
                            tenantId,
                            Guid.Empty,
                            "Reporter user could not be resolved.",
                            ["Persisted reporter user is required."],
                            EventReportFailureCodes.UserUnresolved,
                            "user_unresolved"),
                        transactionCancellationToken);
                }
            }

            var receiptEmailPreference = reporterUserId.HasValue
                ? await notificationPreferenceResolver.ResolveAsync(
                    new NotificationPreferenceResolveRequest(
                        tenantId,
                        reporterUserId.Value,
                        OrganizationId: null,
                        GroupId: null,
                        NotificationPreferenceCategoryCodes.TrustSafety,
                        NotificationPreferenceChannelCodes.Email),
                    transactionCancellationToken)
                : null;

            var priority = ResolvePriority(submission.SeverityHint);
            var encryptedReporterText = evidenceProtector.Protect(submission.ReporterText);
            var report = EventReport.Create(
                tenantId,
                @event.Id,
                reporterUserId,
                reporterActor?.Id,
                reporterUserId.HasValue ? EventReporterKind.AuthenticatedUser : EventReporterKind.Anonymous,
                EventReportSourceKind.UserReport,
                reasonCode,
                submission.SubcategoryCode,
                priority,
                submission.SeverityHint,
                reportCaseUpdatesConsent:
                    submission.ReportCaseUpdatesConsent && reporterUserId.HasValue,
                reportFollowUpContactConsent:
                    submission.ReportFollowUpContactConsent && reporterUserId.HasValue,
                submission.ReporterLocale,
                request.ReporterIpHash,
                request.ReporterUserAgentHash,
                now);
            var target = EventReportTarget.CreateEventTarget(tenantId, report.Id, @event.Id);
            var evidence = EventReportEvidence.CreateReporterText(
                tenantId,
                report.Id,
                encryptedReporterText,
                EventReportEvidenceClassification.Sensitive,
                now.AddDays(options.ReporterTextRetentionDays),
                reporterUserId,
                now);
            var reportCase = EventReportCase.Create(
                tenantId,
                report.Id,
                options.DefaultQueueCode,
                priority,
                now.AddHours(options.CaseSlaHours),
                now);
            var outboxMessage = EventReportOutboxMessageFactory.CreateProviderSyncRequestedMessage(report, reportCase, request.CorrelationId);
            RecipientNotificationMaterialization? receiptMaterialization = reporterUser is null
                ? null
                : reportReceiptNotificationFactory.Create(
                    report,
                    options.CaseSlaHours,
                    RecipientEmailAddressResolver.Resolve(reporterUser, reporterUser.Id),
                    receiptEmailPreference!.IsEnabled,
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    now);

            await eventReportRepository.Create(report);
            await targetRepository.Create(target);
            await evidenceRepository.Create(evidence);
            await caseRepository.Create(reportCase);
            await outboxRepository.Create(outboxMessage);
            if (receiptMaterialization is not null)
            {
                await recipientNotificationMaterializer.MaterializeInCurrentTransactionAsync(
                    receiptMaterialization,
                    transactionCancellationToken);
            }

            metrics.RecordEventReportSubmission(tenantId.ToString(), "succeeded");
            return Success(report.Id, "Event report submitted successfully.");
        }, policyToken), cancellationToken);
    }

    private async Task<bool> IsFencedAsync(Guid? userId, CancellationToken cancellationToken) =>
        userId is Guid subjectId &&
        await privacyErasureStateRepository.GetBySubjectAsync(subjectId, cancellationToken) is not null;

    private async Task<BaseCommandResponse<Guid>> MaskIfFencedAsync(
        Guid? userId,
        BaseCommandResponse<Guid> response,
        CancellationToken cancellationToken) =>
        await IsFencedAsync(userId, cancellationToken) ? FencedFailure() : response;

    private static BaseCommandResponse<Guid> FencedFailure() =>
        BaseCommandResponse.Failure<Guid>(
            PrivacyErasureFencedFailureCode,
            "Event report submission failed.");

    private static EventReportSubmissionOptions NormalizeOptions(EventReportSubmissionOptions options)
    {
        return new EventReportSubmissionOptions
        {
            RequireAuthenticatedReporter = options.RequireAuthenticatedReporter,
            MaxReportsPerUserPerHour = Math.Max(0, options.MaxReportsPerUserPerHour),
            MaxReportsPerEventPerUserPerDay = Math.Max(0, options.MaxReportsPerEventPerUserPerDay),
            DuplicateWindowHours = Math.Max(1, options.DuplicateWindowHours),
            ReporterTextRetentionDays = Math.Max(1, options.ReporterTextRetentionDays),
            MaxReporterTextLength = Math.Max(1, options.MaxReporterTextLength),
            DefaultQueueCode = string.IsNullOrWhiteSpace(options.DefaultQueueCode) ? "default" : options.DefaultQueueCode.Trim(),
            CaseSlaHours = Math.Clamp(
                options.CaseSlaHours,
                EventReportSubmissionOptions.MinCaseSlaHours,
                EventReportSubmissionOptions.MaxCaseSlaHours),
            ReporterFingerprintPepper = string.IsNullOrWhiteSpace(options.ReporterFingerprintPepper)
                ? null
                : options.ReporterFingerprintPepper.Trim()
        };
    }

    private static EventReportPriority ResolvePriority(EventReportSeverityHint? severityHint)
    {
        return severityHint switch
        {
            EventReportSeverityHint.Low => EventReportPriority.Low,
            EventReportSeverityHint.High => EventReportPriority.High,
            EventReportSeverityHint.Critical => EventReportPriority.Urgent,
            _ => EventReportPriority.Normal
        };
    }

    private static string? ResolveSubcategory(
        EventReportSubmissionChannel channel,
        string? requestedSubcategory) =>
        channel switch
        {
            EventReportSubmissionChannel.Correction =>
                EventReportReasonCodePolicy.EventCorrectionSuggestionSubcategory,
            EventReportSubmissionChannel.UnsafeExternalLink =>
                EventReportReasonCodePolicy.UnsafeExternalLinkSubcategory,
            EventReportSubmissionChannel.LegalOrCopyright =>
                EventReportReasonCodePolicy.LegalOrCopyrightComplaintSubcategory,
            _ => requestedSubcategory
        };

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private BaseCommandResponse<Guid> Failure(
        Guid? tenantId,
        Guid id,
        string message,
        IEnumerable<string> errors,
        string? failureCode,
        string failureCategory)
    {
        metrics.RecordEventReportSubmission(tenantId?.ToString(), "failed", failureCategory);

        string normalizedMessage = string.IsNullOrWhiteSpace(message)
            ? "Event report submission failed."
            : message;
        string[] normalizedErrors = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .ToArray();
        if (normalizedErrors.Length == 0)
        {
            normalizedErrors = [normalizedMessage];
        }

        return failureCode is null
            ? BaseCommandResponse.Validation<Guid>(normalizedErrors, normalizedMessage, id)
            : BaseCommandResponse.Failure<Guid>(failureCode, normalizedMessage, normalizedErrors, id);
    }
}
