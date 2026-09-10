using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class ExecuteReportDecisionCommandHandler(
    IEventReportRepository eventReportRepository,
    IEventReportDecisionExecutionRepository executionRepository,
    IEventModerationRecordRepository moderationRecordRepository,
    IEventRoleAssignmentRepository eventRoleAssignmentRepository,
    ITenantUserRepository tenantUserRepository,
    IUserRepository userRepository,
    INotificationPreferenceResolver notificationPreferenceResolver,
    IRecipientNotificationMaterializer recipientNotificationMaterializer,
    ReportOutcomeNotificationFactory reportOutcomeNotificationFactory,
    ReportNeedsMoreInformationNotificationFactory reportNeedsMoreInformationNotificationFactory,
    EventOrganizerWarningNotificationFactory organizerWarningNotificationFactory,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    IMediator mediator,
    ISettingMutationLock mutationLock) : IRequestHandler<ExecuteReportDecisionCommand, BaseCommandResponse<Guid>>
{
    private static readonly TimeSpan ProcessingLeaseDuration = TimeSpan.FromMinutes(10);

    public async Task<BaseCommandResponse<Guid>> Handle(
        ExecuteReportDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var validationResult = await new ExecuteReportDecisionCommandValidator().ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.DecisionId,
                "Event report decision execution request is invalid.",
                validationResult.Errors.Select(error => error.ErrorMessage),
                EventReportFailureCodes.ValidationFailed);
        }

        if (tenantContext.TenantId == Guid.Empty)
        {
            return Failure(request.DecisionId, "Tenant context could not be resolved.", ["Tenant context is required."], EventReportFailureCodes.TenantUnresolved);
        }

        Guid tenantId = tenantContext.TenantId;
        EventReport? preflightReport = await eventReportRepository.GetByIdAsync(tenantId, request.ReportId, cancellationToken);
        TargetValidation preflight = ValidateTarget(preflightReport, request);
        if (!preflight.Response.IsSuccess)
        {
            return preflight.Response;
        }

        if (preflight.Decision!.DecisionSource == EventReportDecisionSource.LocalModerator)
        {
            if (currentUserService.UserId is not { } moderatorUserId)
            {
                return Failure(request.DecisionId, "Moderator user could not be resolved.", ["Authenticated moderator user id is required."], EventReportFailureCodes.UserUnresolved);
            }

            if (!await tenantUserRepository.IsActiveTenantUserAsync(tenantId, moderatorUserId, cancellationToken))
            {
                return Failure(request.DecisionId, "Moderator is not active in the current tenant.", ["Moderator must be an active tenant user."], EventReportFailureCodes.ModeratorUnavailable);
            }
        }

        if (preflight.Execution!.State == EventReportDecisionExecutionState.Completed)
        {
            return Success(request.DecisionId, "Event report decision was already executed.");
        }

        Guid enforcementLeaseToken = Guid.CreateVersion7();
        DateTime enforcementClaimedAtUtc = DateTime.UtcNow;
        ExecutionClaimResult claim = await ClaimExecutionAsync(
            tenantId,
            request,
            enforcementLeaseToken,
            enforcementClaimedAtUtc,
            cancellationToken);
        if (!claim.Response.IsSuccess || claim.IsCompleted)
        {
            return claim.Response;
        }

        if (claim.EnforcementClaimed)
        {
            BaseCommandResponse<Guid> enforcement = await EnsureEnforcementReceiptAsync(
                tenantId,
                request,
                claim.Decision!,
                enforcementLeaseToken,
                cancellationToken);
            if (!enforcement.IsSuccess)
            {
                return enforcement;
            }
        }

        Guid completionLeaseToken = Guid.CreateVersion7();
        DateTime completionClaimedAtUtc = DateTime.UtcNow;
        EventReportDecisionExecutionClaimOutcome completionClaim = await executionRepository.TryClaimCompletionAsync(
            tenantId,
            request.DecisionId,
            completionLeaseToken,
            completionClaimedAtUtc,
            completionClaimedAtUtc.Add(ProcessingLeaseDuration),
            cancellationToken);
        switch (completionClaim)
        {
            case EventReportDecisionExecutionClaimOutcome.Completed:
                return Success(request.DecisionId, "Event report decision was already executed.");
            case EventReportDecisionExecutionClaimOutcome.Claimed:
            case EventReportDecisionExecutionClaimOutcome.SameLease:
                break;
            case EventReportDecisionExecutionClaimOutcome.CompletionPending:
            case EventReportDecisionExecutionClaimOutcome.Unavailable:
                return Failure(
                    request.DecisionId,
                    "Event report decision execution is already in progress.",
                    ["Another executor currently owns the decision completion lease."],
                    EventReportFailureCodes.DecisionExecutionInProgress);
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(completionClaim),
                    completionClaim,
                    "Unsupported report-decision completion claim outcome.");
        }

        DateTime preparationAuthorityAtUtc = DateTime.UtcNow;
        PreparedCompletion prepared;
        try
        {
            prepared = await PrepareCompletionAsync(
                tenantId,
                request,
                preparationAuthorityAtUtc,
                cancellationToken);
        }
        catch (DecisionCompletionPreparationException ex)
        {
            await executionRepository.TryReleaseCompletionClaimAsync(
                tenantId,
                request.DecisionId,
                completionLeaseToken,
                ex.FailureCode,
                DateTime.UtcNow,
                cancellationToken);
            return Failure(request.DecisionId, ex.Message, [ex.Message], ex.FailureCode);
        }

        DateTime completedAtUtc = DateTime.UtcNow;
        try
        {
            return await CompleteDecisionAsync(
                tenantId,
                request,
                completionLeaseToken,
                completedAtUtc,
                prepared,
                cancellationToken);
        }
        catch (DecisionCompletionPreparationException ex)
        {
            await executionRepository.TryReleaseCompletionClaimAsync(
                tenantId,
                request.DecisionId,
                completionLeaseToken,
                ex.FailureCode,
                DateTime.UtcNow,
                cancellationToken);
            return Failure(request.DecisionId, ex.Message, [ex.Message], ex.FailureCode);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await executionRepository.TryReleaseCompletionClaimAsync(
                tenantId,
                request.DecisionId,
                completionLeaseToken,
                EventReportFailureCodes.DecisionCompletionFailed,
                DateTime.UtcNow,
                CancellationToken.None);
            throw;
        }
    }

    private async Task<ExecutionClaimResult> ClaimExecutionAsync(
        Guid tenantId,
        ExecuteReportDecisionCommand request,
        Guid leaseToken,
        DateTime claimedAtUtc,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            EventReport? report = await eventReportRepository.GetByIdAsync(tenantId, request.ReportId, token);
            TargetValidation target = ValidateTarget(report, request);
            if (!target.Response.IsSuccess)
            {
                return ExecutionClaimResult.Failed(target.Response);
            }

            EventReportDecisionExecution execution = target.Execution!;
            if (execution.State == EventReportDecisionExecutionState.Completed)
            {
                return ExecutionClaimResult.Completed(Success(request.DecisionId, "Event report decision was already executed."));
            }

            if (target.Case.ConcurrencyStamp != request.ExpectedCaseConcurrencyStamp)
            {
                return ExecutionClaimResult.Failed(Failure(
                    request.DecisionId,
                    "Event report case was changed by another request.",
                    ["Refresh the report case and try again."],
                    EventReportFailureCodes.CaseConcurrencyConflict));
            }

            if (execution.State == EventReportDecisionExecutionState.CompletionPending)
            {
                return ExecutionClaimResult.CompletionPending(target.Decision!);
            }

            EventReportDecisionExecutionClaimOutcome outcome = await executionRepository.TryClaimEnforcementAsync(
                tenantId,
                request.DecisionId,
                leaseToken,
                claimedAtUtc,
                claimedAtUtc.Add(ProcessingLeaseDuration),
                token);
            return outcome switch
            {
                EventReportDecisionExecutionClaimOutcome.Claimed
                    or EventReportDecisionExecutionClaimOutcome.SameLease =>
                    ExecutionClaimResult.Enforcement(target.Decision!),
                EventReportDecisionExecutionClaimOutcome.CompletionPending =>
                    ExecutionClaimResult.CompletionPending(target.Decision!),
                EventReportDecisionExecutionClaimOutcome.Completed =>
                    ExecutionClaimResult.Completed(Success(request.DecisionId, "Event report decision was already executed.")),
                EventReportDecisionExecutionClaimOutcome.Unavailable => ExecutionClaimResult.Failed(Failure(
                    request.DecisionId,
                    "Event report decision execution is already in progress.",
                    ["Another executor currently owns the enforcement lease."],
                    EventReportFailureCodes.DecisionExecutionInProgress)),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(outcome),
                    outcome,
                    "Unsupported report-decision enforcement claim outcome.")
            };
        }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> EnsureEnforcementReceiptAsync(
        Guid tenantId,
        ExecuteReportDecisionCommand request,
        EventReportDecision decision,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        EventReportDecisionEnforcementReceiptKind receiptKind = MapReceiptKind(decision.DecisionKind);
        if (receiptKind is not (EventReportDecisionEnforcementReceiptKind.LightModeration
            or EventReportDecisionEnforcementReceiptKind.HeavyRedaction))
        {
            return await RecordReceiptAsync(tenantId, request, leaseToken, receiptKind, null, cancellationToken);
        }

        EventModerationRecord? moderationRecord = await moderationRecordRepository.GetBySourceReportDecisionAsync(
            tenantId,
            request.ReportId,
            request.DecisionId,
            cancellationToken);
        if (moderationRecord is null)
        {
            BaseCommandResponse<Guid> enforcementResponse;
            try
            {
                enforcementResponse = await ExecuteModerationEnforcementAsync(decision, request, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await executionRepository.TryReleaseEnforcementClaimAsync(
                    tenantId,
                    request.DecisionId,
                    leaseToken,
                    EventReportFailureCodes.DecisionExecutionFailed,
                    DateTime.UtcNow,
                    CancellationToken.None);
                throw;
            }

            moderationRecord = await moderationRecordRepository.GetBySourceReportDecisionAsync(
                tenantId,
                request.ReportId,
                request.DecisionId,
                cancellationToken);
            if (moderationRecord is null)
            {
                string failureCode = enforcementResponse.FailureCode ?? EventReportFailureCodes.DecisionEnforcementReceiptMissing;
                await executionRepository.TryReleaseEnforcementClaimAsync(
                    tenantId,
                    request.DecisionId,
                    leaseToken,
                    failureCode,
                    DateTime.UtcNow,
                    cancellationToken);
                return Failure(
                    request.DecisionId,
                    "Event report decision enforcement did not produce its exact receipt.",
                    enforcementResponse.Errors ?? ["The exact decision-bound moderation receipt was not found."],
                    failureCode);
            }
        }

        if (!IsExpectedModerationReceipt(moderationRecord, decision, request))
        {
            await executionRepository.TryReleaseEnforcementClaimAsync(
                tenantId,
                request.DecisionId,
                leaseToken,
                EventReportFailureCodes.DecisionEnforcementReceiptMismatch,
                DateTime.UtcNow,
                cancellationToken);
            return Failure(
                request.DecisionId,
                "Event report decision enforcement receipt does not match the captured decision.",
                ["The decision-bound moderation receipt kind or authority fields do not match."],
                EventReportFailureCodes.DecisionEnforcementReceiptMismatch);
        }

        return await RecordReceiptAsync(
            tenantId,
            request,
            leaseToken,
            receiptKind,
            moderationRecord.Id,
            cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> RecordReceiptAsync(
        Guid tenantId,
        ExecuteReportDecisionCommand request,
        Guid leaseToken,
        EventReportDecisionEnforcementReceiptKind receiptKind,
        Guid? receiptId,
        CancellationToken cancellationToken)
    {
        EventReportDecisionExecutionTransitionOutcome recorded = await executionRepository.TryRecordEnforcementReceiptAsync(
            tenantId,
            request.DecisionId,
            leaseToken,
            receiptKind,
            receiptId,
            DateTime.UtcNow,
            cancellationToken);
        return recorded switch
        {
            EventReportDecisionExecutionTransitionOutcome.Applied
                or EventReportDecisionExecutionTransitionOutcome.AlreadyApplied =>
                Success(request.DecisionId, "Event report decision enforcement receipt recorded."),
            EventReportDecisionExecutionTransitionOutcome.Conflict => Failure(
                request.DecisionId,
                "Event report decision enforcement lease was lost before receipt persistence.",
                ["Retry the decision execution so the durable receipt can be reconciled."],
                EventReportFailureCodes.DecisionExecutionInProgress),
            _ => throw new ArgumentOutOfRangeException(
                nameof(recorded),
                recorded,
                "Unsupported report-decision enforcement transition outcome.")
        };
    }

    private Task<BaseCommandResponse<Guid>> ExecuteModerationEnforcementAsync(
        EventReportDecision decision,
        ExecuteReportDecisionCommand request,
        CancellationToken cancellationToken)
    {
        string correlationId = NormalizeCorrelationId(request);
        return decision.DecisionKind switch
        {
            EventReportDecisionKind.LightModerate => mediator.Send(new ModerateEventCommand
            {
                Id = request.EventId,
                ReasonCode = decision.ReasonCode,
                CorrelationId = correlationId,
                SourceReportId = request.ReportId,
                SourceReportDecisionId = request.DecisionId
            }, cancellationToken),
            EventReportDecisionKind.HeavyRedact => mediator.Send(new HeavyRedactEventCommand
            {
                Id = request.EventId,
                ReasonCode = decision.ReasonCode,
                CorrelationId = correlationId,
                SourceReportId = request.ReportId,
                SourceReportDecisionId = request.DecisionId
            }, cancellationToken),
            _ => Task.FromResult(Success(request.DecisionId, "No event moderation command is required."))
        };
    }

    private async Task<PreparedCompletion> PrepareCompletionAsync(
        Guid tenantId,
        ExecuteReportDecisionCommand request,
        DateTime completionAuthorityAtUtc,
        CancellationToken cancellationToken)
    {
        EventReport? report = await eventReportRepository.GetByIdAsync(tenantId, request.ReportId, cancellationToken);
        TargetValidation target = ValidateTarget(report, request);
        if (!target.Response.IsSuccess)
        {
            throw new DecisionCompletionPreparationException(
                target.Response.FailureCode ?? EventReportFailureCodes.DecisionExecutionInvalidState,
                target.Response.Message ?? "The report decision target is no longer valid.");
        }

        EventReportDecision decision = target.Decision!;
        await ValidateRecordedReceiptAsync(
            tenantId,
            request,
            decision,
            target.Execution!,
            cancellationToken);
        List<PreparedRecipientIds> organizerRecipients = [];
        if (decision.DecisionKind == EventReportDecisionKind.WarnOrganizer)
        {
            IReadOnlyList<EventRoleAssignment> owners = await eventRoleAssignmentRepository.GetEffectiveOwnersForEventAsync(
                tenantId,
                request.EventId,
                completionAuthorityAtUtc,
                cancellationToken);
            List<Guid> activeOwnerUserIds = [];
            foreach (EventRoleAssignment owner in owners.GroupBy(value => value.UserId).Select(group => group.First()))
            {
                if (!owner.User.IsDeleted
                    && await tenantUserRepository.IsActiveTenantUserAsync(tenantId, owner.UserId, cancellationToken))
                {
                    activeOwnerUserIds.Add(owner.UserId);
                }
            }

            if (activeOwnerUserIds.Count == 0)
            {
                throw new DecisionCompletionPreparationException(
                    EventReportFailureCodes.DecisionOrganizerUnavailable,
                    "No active event owner is available to receive the required organizer warning.");
            }

            foreach (Guid ownerUserId in activeOwnerUserIds.Distinct().Order())
            {
                organizerRecipients.Add(PreparedRecipientIds.Create(ownerUserId));
            }
        }

        PreparedRecipientIds? reporterRecipient;
        if (decision.DecisionKind == EventReportDecisionKind.NeedsMoreInfo)
        {
            reporterRecipient = await PrepareRequiredReporterRecipientAsync(
                tenantId,
                report!,
                cancellationToken);
        }
        else
        {
            reporterRecipient = HasFinalReporterOutcome(decision.DecisionKind)
                && report!.ReporterUserId is { } reporterUserId
                    ? PreparedRecipientIds.Create(reporterUserId)
                    : null;
        }

        return new PreparedCompletion(organizerRecipients, reporterRecipient);
    }

    private async Task<PreparedRecipientIds> PrepareRequiredReporterRecipientAsync(
        Guid tenantId,
        EventReport report,
        CancellationToken cancellationToken)
    {
        if (report.ReporterUserId is not { } reporterUserId
            || reporterUserId == Guid.Empty
            || !await tenantUserRepository.IsActiveTenantUserAsync(tenantId, reporterUserId, cancellationToken))
        {
            throw RequiredReporterUnavailable();
        }

        User? reporter = await userRepository.GetUserWithDetails(reporterUserId, cancellationToken);
        if (reporter is null || reporter.IsDeleted)
        {
            throw RequiredReporterUnavailable();
        }

        return PreparedRecipientIds.Create(reporterUserId);
    }

    private async Task ValidateRecordedReceiptAsync(
        Guid tenantId,
        ExecuteReportDecisionCommand request,
        EventReportDecision decision,
        EventReportDecisionExecution execution,
        CancellationToken cancellationToken)
    {
        EventReportDecisionEnforcementReceiptKind expectedKind = MapReceiptKind(decision.DecisionKind);
        if (execution.State != EventReportDecisionExecutionState.CompletionPending
            || execution.EnforcementReceiptKind != expectedKind)
        {
            throw new DecisionCompletionPreparationException(
                EventReportFailureCodes.DecisionEnforcementReceiptMismatch,
                "The durable enforcement receipt does not match the captured decision.");
        }

        if (expectedKind is not (EventReportDecisionEnforcementReceiptKind.LightModeration
            or EventReportDecisionEnforcementReceiptKind.HeavyRedaction))
        {
            if (execution.EnforcementReceiptId is not null || execution.ModerationRecordId is not null)
            {
                throw new DecisionCompletionPreparationException(
                    EventReportFailureCodes.DecisionEnforcementReceiptMismatch,
                    "The durable enforcement receipt contains an unexpected moderation record.");
            }

            return;
        }

        EventModerationRecord? moderationRecord = await moderationRecordRepository.GetBySourceReportDecisionAsync(
            tenantId,
            request.ReportId,
            request.DecisionId,
            cancellationToken);
        if (moderationRecord is null
            || moderationRecord.Id != execution.EnforcementReceiptId
            || moderationRecord.Id != execution.ModerationRecordId
            || !IsExpectedModerationReceipt(moderationRecord, decision, request))
        {
            throw new DecisionCompletionPreparationException(
                EventReportFailureCodes.DecisionEnforcementReceiptMismatch,
                "The exact decision-bound moderation receipt is missing or no longer matches enforcement.");
        }
    }

    private async Task<BaseCommandResponse<Guid>> CompleteDecisionAsync(
        Guid tenantId,
        ExecuteReportDecisionCommand request,
        Guid completionLeaseToken,
        DateTime completedAtUtc,
        PreparedCompletion prepared,
        CancellationToken cancellationToken)
    {
        return await mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            policyToken => unitOfWork.ExecuteSerializableAsync(async token =>
        {
            EventReport? report = await eventReportRepository.GetByIdForUpdateAsync(tenantId, request.ReportId, token);
            TargetValidation target = ValidateTarget(report, request);
            if (!target.Response.IsSuccess)
            {
                throw new DecisionCompletionPreparationException(
                    target.Response.FailureCode ?? EventReportFailureCodes.DecisionExecutionInvalidState,
                    target.Response.Message ?? "The report decision target is no longer valid.");
            }

            EventReportDecisionExecution execution = target.Execution!;
            if (execution.State == EventReportDecisionExecutionState.Completed)
            {
                return Success(request.DecisionId, "Event report decision was already executed.");
            }

            if (execution.State != EventReportDecisionExecutionState.CompletionPending
                || execution.ProcessingLeaseToken != completionLeaseToken
                || execution.ProcessingLeaseExpiresAtUtc is not { } expiresAtUtc
                || expiresAtUtc <= DateTime.UtcNow)
            {
                throw new DecisionCompletionPreparationException(
                    EventReportFailureCodes.DecisionExecutionInProgress,
                    "The report-decision completion lease is no longer active.");
            }

            if (target.Case.ConcurrencyStamp != request.ExpectedCaseConcurrencyStamp)
            {
                throw new DecisionCompletionPreparationException(
                    EventReportFailureCodes.CaseConcurrencyConflict,
                    "The report case changed after enforcement and cannot be completed from stale state.");
            }

            await ValidateRecordedReceiptAsync(
                tenantId,
                request,
                target.Decision!,
                execution,
                token);
            DateTime authorityAtUtc = DateTime.UtcNow;
            CurrentRecipientMaterializations currentRecipients = await ResolveCurrentRecipientsAsync(
                tenantId,
                request,
                report!,
                target.Decision!,
                prepared,
                authorityAtUtc,
                completedAtUtc,
                token);
            ApplyCompletion(report!, target.Case, target.Decision!, completedAtUtc);
            execution.Complete(completionLeaseToken, completedAtUtc);

            foreach (RecipientNotificationMaterialization warning in currentRecipients.OrganizerWarnings)
            {
                await recipientNotificationMaterializer.MaterializeInCurrentTransactionAsync(warning, token);
            }

            if (currentRecipients.ReporterNotification is not null)
            {
                await recipientNotificationMaterializer.MaterializeInCurrentTransactionAsync(currentRecipients.ReporterNotification, token);
            }

            await eventReportRepository.Update(report!);
            return Success(request.DecisionId, "Event report decision executed successfully.");
        }, policyToken), cancellationToken);
    }

    private async Task<CurrentRecipientMaterializations> ResolveCurrentRecipientsAsync(
        Guid tenantId,
        ExecuteReportDecisionCommand request,
        EventReport report,
        EventReportDecision decision,
        PreparedCompletion prepared,
        DateTime authorityAtUtc,
        DateTime materializedAtUtc,
        CancellationToken cancellationToken)
    {
        List<RecipientNotificationMaterialization> organizerWarnings = [];
        if (decision.DecisionKind == EventReportDecisionKind.WarnOrganizer)
        {
            IReadOnlyList<EventRoleAssignment> owners = await eventRoleAssignmentRepository.GetEffectiveOwnersForEventAsync(
                tenantId,
                request.EventId,
                authorityAtUtc,
                cancellationToken);
            List<Guid> activeOwnerUserIds = [];
            foreach (Guid userId in owners
                         .Where(owner => !owner.User.IsDeleted)
                         .Select(owner => owner.UserId)
                         .Distinct()
                         .Order())
            {
                if (await tenantUserRepository.IsActiveTenantUserAsync(tenantId, userId, cancellationToken))
                {
                    activeOwnerUserIds.Add(userId);
                }
            }

            IReadOnlyList<Guid> preparedOwnerUserIds = prepared.OrganizerRecipients
                .Select(recipient => recipient.UserId)
                .Order()
                .ToArray();
            if (!activeOwnerUserIds.SequenceEqual(preparedOwnerUserIds))
            {
                throw new DecisionCompletionPreparationException(
                    EventReportFailureCodes.DecisionRecipientAuthorityChanged,
                    "Event-owner authority changed while the organizer warning was being prepared.");
            }

            foreach (EventRoleAssignment owner in owners
                         .Where(owner => activeOwnerUserIds.Contains(owner.UserId))
                         .GroupBy(owner => owner.UserId)
                         .Select(group => group.First())
                         .OrderBy(owner => owner.UserId))
            {
                PreparedRecipientIds ids = prepared.OrganizerRecipients.Single(value => value.UserId == owner.UserId);
                NotificationPreferenceDecision preference = await notificationPreferenceResolver.ResolveAsync(
                    new NotificationPreferenceResolveRequest(
                        tenantId,
                        owner.UserId,
                        OrganizationId: null,
                        GroupId: null,
                        NotificationPreferenceCategoryCodes.TrustSafety,
                        NotificationPreferenceChannelCodes.Email),
                    cancellationToken);
                organizerWarnings.Add(organizerWarningNotificationFactory.Create(
                    report,
                    decision,
                    owner.UserId,
                    RecipientEmailAddressResolver.Resolve(owner.User, owner.UserId),
                    preference.IsEnabled,
                    ids.IntentId,
                    ids.InAppNotificationId,
                    ids.InAppDeliveryId,
                    ids.EmailDeliveryId,
                    ids.EmailDispatchOutboxId,
                    materializedAtUtc));
            }
        }

        RecipientNotificationMaterialization? reporterNotification = null;
        if (prepared.ReporterRecipient is { } reporterIds
            && report.ReporterUserId == reporterIds.UserId
            && await tenantUserRepository.IsActiveTenantUserAsync(tenantId, reporterIds.UserId, cancellationToken))
        {
            User? reporter = await userRepository.GetUserWithDetails(reporterIds.UserId, cancellationToken);
            if (reporter is not null && !reporter.IsDeleted)
            {
                NotificationPreferenceDecision preference = await notificationPreferenceResolver.ResolveAsync(
                    new NotificationPreferenceResolveRequest(
                        tenantId,
                        reporterIds.UserId,
                        OrganizationId: null,
                        GroupId: null,
                        NotificationPreferenceCategoryCodes.TrustSafety,
                        NotificationPreferenceChannelCodes.Email),
                    cancellationToken);
                RecipientEmailAddressResolution emailAddress = RecipientEmailAddressResolver.Resolve(
                    reporter,
                    reporterIds.UserId);
                reporterNotification = decision.DecisionKind == EventReportDecisionKind.NeedsMoreInfo
                    ? reportNeedsMoreInformationNotificationFactory.Create(
                        report,
                        decision,
                        emailAddress,
                        preference.IsEnabled,
                        reporterIds.IntentId,
                        reporterIds.InAppNotificationId,
                        reporterIds.InAppDeliveryId,
                        reporterIds.EmailDeliveryId,
                        reporterIds.EmailDispatchOutboxId,
                        materializedAtUtc)
                    : reportOutcomeNotificationFactory.Create(
                        report,
                        decision,
                        emailAddress,
                        preference.IsEnabled,
                        reporterIds.IntentId,
                        reporterIds.InAppNotificationId,
                        reporterIds.InAppDeliveryId,
                        reporterIds.EmailDeliveryId,
                        reporterIds.EmailDispatchOutboxId,
                        materializedAtUtc);
            }
        }

        if (decision.DecisionKind == EventReportDecisionKind.NeedsMoreInfo
            && reporterNotification is null)
        {
            throw RequiredReporterUnavailable();
        }

        return new CurrentRecipientMaterializations(organizerWarnings, reporterNotification);
    }

    private static void ApplyCompletion(
        EventReport report,
        EventReportCase reportCase,
        EventReportDecision decision,
        DateTime utcNow)
    {
        switch (decision.DecisionKind)
        {
            case EventReportDecisionKind.NoViolation:
                report.UpdateStatus(EventReportStatus.Dismissed, utcNow);
                reportCase.Close(utcNow);
                break;
            case EventReportDecisionKind.Duplicate:
                report.MarkDuplicate(
                    decision.DuplicateGroupId
                    ?? throw new InvalidOperationException("Duplicate report decisions require a persisted duplicate group."),
                    utcNow);
                reportCase.Close(utcNow);
                break;
            case EventReportDecisionKind.LightModerate:
            case EventReportDecisionKind.HeavyRedact:
            case EventReportDecisionKind.WarnOrganizer:
                report.UpdateStatus(EventReportStatus.Actioned, utcNow);
                reportCase.Close(utcNow);
                break;
            case EventReportDecisionKind.NeedsMoreInfo:
                report.UpdateStatus(EventReportStatus.UnderReview, utcNow);
                reportCase.MarkWaitingReporter(utcNow);
                break;
            case EventReportDecisionKind.Escalate:
                report.UpdateStatus(EventReportStatus.Escalated, utcNow);
                reportCase.MarkWaitingExternal(utcNow);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.DecisionKind, "Unsupported event report decision kind.");
        }
    }

    private static EventReportDecisionEnforcementReceiptKind MapReceiptKind(
        EventReportDecisionKind decisionKind) => decisionKind switch
        {
            EventReportDecisionKind.NoViolation or EventReportDecisionKind.Duplicate =>
                EventReportDecisionEnforcementReceiptKind.NoAction,
            EventReportDecisionKind.LightModerate => EventReportDecisionEnforcementReceiptKind.LightModeration,
            EventReportDecisionKind.HeavyRedact => EventReportDecisionEnforcementReceiptKind.HeavyRedaction,
            EventReportDecisionKind.WarnOrganizer => EventReportDecisionEnforcementReceiptKind.OrganizerWarning,
            EventReportDecisionKind.Escalate or EventReportDecisionKind.NeedsMoreInfo =>
                EventReportDecisionEnforcementReceiptKind.NonTerminal,
            _ => throw new ArgumentOutOfRangeException(nameof(decisionKind), decisionKind, "Unsupported report decision kind.")
        };

    private static bool IsExpectedModerationReceipt(
        EventModerationRecord record,
        EventReportDecision decision,
        ExecuteReportDecisionCommand request)
    {
        EventModerationActionKind expectedKind = decision.DecisionKind switch
        {
            EventReportDecisionKind.LightModerate => EventModerationActionKind.LightModerated,
            EventReportDecisionKind.HeavyRedact => EventModerationActionKind.HeavyRedacted,
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision.DecisionKind, "A moderation receipt was not expected.")
        };
        return record.EventId == request.EventId
            && record.SourceReportId == request.ReportId
            && record.SourceReportDecisionId == request.DecisionId
            && record.ActionKind == expectedKind
            && record.ReasonCode == decision.ReasonCode
            && record.IsIrreversible == (expectedKind == EventModerationActionKind.HeavyRedacted);
    }

    private static bool HasFinalReporterOutcome(EventReportDecisionKind decisionKind) => decisionKind is
        EventReportDecisionKind.NoViolation
        or EventReportDecisionKind.Duplicate
        or EventReportDecisionKind.LightModerate
        or EventReportDecisionKind.HeavyRedact
        or EventReportDecisionKind.WarnOrganizer;

    private static DecisionCompletionPreparationException RequiredReporterUnavailable() => new(
        EventReportFailureCodes.DecisionRecipientAuthorityChanged,
        "An active persisted reporter is required before requesting more information.");

    private static TargetValidation ValidateTarget(
        EventReport? report,
        ExecuteReportDecisionCommand request)
    {
        if (report is null)
        {
            return TargetValidation.Invalid(Failure(request.DecisionId, "Event report was not found.", ["Event report was not found."], EventReportFailureCodes.ReportNotFound));
        }

        if (report.EventId != request.EventId)
        {
            return TargetValidation.Invalid(Failure(request.DecisionId, "Event report does not belong to the requested event.", ["Event report does not belong to the requested event."], EventReportFailureCodes.EventMismatch));
        }

        EventReportCase? reportCase = report.Cases.FirstOrDefault(candidate => candidate.Id == request.CaseId);
        if (reportCase is null)
        {
            return TargetValidation.Invalid(Failure(request.DecisionId, "Event report case was not found.", ["Event report case was not found."], EventReportFailureCodes.CaseNotFound));
        }

        EventReportDecision? decision = report.Decisions.FirstOrDefault(candidate => candidate.Id == request.DecisionId);
        if (decision is null)
        {
            return TargetValidation.Invalid(Failure(request.DecisionId, "Event report decision was not found.", ["Event report decision was not found."], EventReportFailureCodes.DecisionNotFound));
        }

        if (decision.ReportId != report.Id || decision.CaseId != reportCase.Id)
        {
            return TargetValidation.Invalid(Failure(request.DecisionId, "Event report decision does not belong to the requested report case.", ["Event report decision does not belong to the requested report case."], EventReportFailureCodes.DecisionInvalid));
        }

        if (decision.Execution is null
            || decision.Execution.TenantId != report.TenantId
            || decision.Execution.ReportId != report.Id
            || decision.Execution.DecisionId != decision.Id)
        {
            return TargetValidation.Invalid(Failure(
                request.DecisionId,
                "Event report decision execution state was not found.",
                ["The decision does not have its required durable execution state."],
                EventReportFailureCodes.DecisionExecutionMissing));
        }

        if (decision.Execution.State != EventReportDecisionExecutionState.Completed
            && (reportCase.CurrentDecisionId != decision.Id
                || reportCase.Status != EventReportCaseStatus.DecisionReady))
        {
            return TargetValidation.Invalid(Failure(
                request.DecisionId,
                "Only the current decision-ready report decision can be executed.",
                ["The decision is stale or the report case is not decision-ready."],
                EventReportFailureCodes.DecisionExecutionInvalidState));
        }

        return TargetValidation.Valid(report, reportCase, decision, decision.Execution);
    }

    private static string NormalizeCorrelationId(ExecuteReportDecisionCommand request) =>
        string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"report-decision:{request.DecisionId:N}"
            : request.CorrelationId.Trim();

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message, id)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors, id);

    private sealed record TargetValidation(
        BaseCommandResponse<Guid> Response,
        EventReport? Report,
        EventReportCase? Case,
        EventReportDecision? Decision,
        EventReportDecisionExecution? Execution)
    {
        public static TargetValidation Invalid(BaseCommandResponse<Guid> response) => new(response, null, null, null, null);

        public static TargetValidation Valid(
            EventReport report,
            EventReportCase reportCase,
            EventReportDecision decision,
            EventReportDecisionExecution execution) =>
            new(Success(decision.Id, "Event report decision target is valid."), report, reportCase, decision, execution);
    }

    private sealed record ExecutionClaimResult(
        BaseCommandResponse<Guid> Response,
        EventReportDecision? Decision,
        bool EnforcementClaimed,
        bool IsCompleted)
    {
        public static ExecutionClaimResult Failed(BaseCommandResponse<Guid> response) => new(response, null, false, false);
        public static ExecutionClaimResult Completed(BaseCommandResponse<Guid> response) => new(response, null, false, true);
        public static ExecutionClaimResult CompletionPending(EventReportDecision decision) => new(Success(decision.Id, "Decision enforcement was already receipted."), decision, false, false);
        public static ExecutionClaimResult Enforcement(EventReportDecision decision) => new(Success(decision.Id, "Decision enforcement claimed."), decision, true, false);
    }

    private sealed record PreparedCompletion(
        IReadOnlyList<PreparedRecipientIds> OrganizerRecipients,
        PreparedRecipientIds? ReporterRecipient);

    private sealed record PreparedRecipientIds(
        Guid UserId,
        Guid IntentId,
        Guid InAppNotificationId,
        Guid InAppDeliveryId,
        Guid EmailDeliveryId,
        Guid EmailDispatchOutboxId)
    {
        public static PreparedRecipientIds Create(Guid userId) => new(
            userId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
    }

    private sealed record CurrentRecipientMaterializations(
        IReadOnlyList<RecipientNotificationMaterialization> OrganizerWarnings,
        RecipientNotificationMaterialization? ReporterNotification);

    private sealed class DecisionCompletionPreparationException(string failureCode, string message)
        : Exception(message)
    {
        public string FailureCode { get; } = failureCode;
    }
}
