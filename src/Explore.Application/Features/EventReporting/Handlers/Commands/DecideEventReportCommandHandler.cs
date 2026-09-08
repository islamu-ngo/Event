using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class DecideEventReportCommandHandler(
    IEventReportRepository eventReportRepository,
    ITenantUserRepository tenantUserRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService) : IRequestHandler<DecideEventReportCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DecideEventReportCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await new DecideEventReportCommandValidator().ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.ReportId,
                "Event report decision request is invalid.",
                validationResult.Errors.Select(error => error.ErrorMessage),
                EventReportFailureCodes.ValidationFailed);
        }

        if (tenantContext.TenantId == Guid.Empty)
        {
            return Failure(request.ReportId, "Tenant context could not be resolved.", ["Tenant context is required."], EventReportFailureCodes.TenantUnresolved);
        }

        if (currentUserService.UserId is not { } moderatorUserId)
        {
            return Failure(request.ReportId, "Moderator user could not be resolved.", ["Authenticated moderator user id is required."], EventReportFailureCodes.UserUnresolved);
        }

        var tenantId = tenantContext.TenantId;
        if (!await tenantUserRepository.IsActiveTenantUserAsync(tenantId, moderatorUserId, cancellationToken))
        {
            return Failure(request.ReportId, "Moderator is not active in the current tenant.", ["Moderator must be an active tenant user."], EventReportFailureCodes.ModeratorUnavailable);
        }

        Guid decisionId = Guid.CreateVersion7();
        Guid executionId = Guid.CreateVersion7();
        DateTime capturedAtUtc = DateTime.UtcNow;

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var report = await eventReportRepository.GetByIdForUpdateAsync(tenantId, request.ReportId, token);
            if (report is null)
            {
                return Failure(request.ReportId, "Event report was not found.", ["Event report was not found."], EventReportFailureCodes.ReportNotFound);
            }

            if (report.EventId != request.EventId)
            {
                return Failure(request.ReportId, "Event report does not belong to the requested event.", ["Event report does not belong to the requested event."], EventReportFailureCodes.EventMismatch);
            }

            var reportCase = report.Cases.FirstOrDefault(candidate => candidate.Id == request.CaseId);
            if (reportCase is null)
            {
                return Failure(request.ReportId, "Event report case was not found.", ["Event report case was not found."], EventReportFailureCodes.CaseNotFound);
            }

            EventReportDecision? exactRetry = report.Decisions.FirstOrDefault(candidate => candidate.Id == decisionId);
            if (exactRetry is not null)
            {
                bool exactAuthority = reportCase.CurrentDecisionId == exactRetry.Id
                    && exactRetry.TenantId == tenantId
                    && exactRetry.ReportId == report.Id
                    && exactRetry.CaseId == reportCase.Id
                    && exactRetry.DecisionSource == EventReportDecisionSource.LocalModerator
                    && exactRetry.DecisionKind == request.DecisionKind
                    && exactRetry.ModeratorUserId == moderatorUserId;
                if (!exactAuthority)
                {
                    return Failure(
                        request.ReportId,
                        "The reconciled report decision no longer owns this case.",
                        ["Refresh the report case before recording another decision."],
                        EventReportFailureCodes.DecisionInvalid);
                }

                if (exactRetry.Execution.State == EventReportDecisionExecutionState.Completed
                    || reportCase.Status == EventReportCaseStatus.DecisionReady)
                {
                    return Success(exactRetry.Id, "Event report decision was already recorded.");
                }

                return Failure(
                    request.ReportId,
                    "The reconciled report decision is not executable from the current case state.",
                    ["Refresh the report case before retrying."],
                    EventReportFailureCodes.DecisionExecutionInvalidState);
            }

            if (reportCase.ConcurrencyStamp != request.ExpectedCaseConcurrencyStamp)
            {
                return Failure(request.ReportId, "Event report case was changed by another request.", ["Refresh the report case and try again."], EventReportFailureCodes.CaseConcurrencyConflict);
            }

            bool initialDecision = reportCase.CurrentDecisionId is null;
            if ((initialDecision && reportCase.Status != EventReportCaseStatus.Assigned)
                || !reportCase.CanSelectNewDecision())
            {
                return Failure(
                    request.ReportId,
                    "The report case cannot receive a new current decision.",
                    ["Only an undecided assigned case or a completed nonterminal decision can receive a new decision."],
                    EventReportFailureCodes.CaseInvalidStatus);
            }

            if (reportCase.AssignedModeratorUserId != moderatorUserId)
            {
                return Failure(request.ReportId, "Report case is assigned to another moderator.", ["Only the assigned moderator can decide this report case."], EventReportFailureCodes.AssignmentMismatch);
            }

            if (report.IsTerminal)
            {
                return Failure(request.ReportId, "Terminal event reports cannot receive new decisions.", ["Terminal event reports cannot receive new decisions."], EventReportFailureCodes.ReportInvalidStatus);
            }

            if (request.DecisionKind == EventReportDecisionKind.Duplicate && request.DuplicateGroupId is null)
            {
                return Failure(request.ReportId, "Duplicate report decisions require a duplicate group.", ["DuplicateGroupId is required."], EventReportFailureCodes.DuplicateGroupRequired);
            }

            var decision = EventReportDecision.Create(
                tenantId,
                reportCase.Id,
                report.Id,
                EventReportDecisionSource.LocalModerator,
                request.DecisionKind,
                request.ReasonCode,
                request.SafeNote,
                moderatorUserId,
                externalDecisionId: null,
                capturedAtUtc,
                decisionId: decisionId,
                executionId: executionId,
                duplicateGroupId: request.DuplicateGroupId);

            reportCase.SelectDecision(decision, capturedAtUtc);
            report.Decisions.Add(decision);
            await eventReportRepository.PersistDecisionCaptureAsync(report, decision, token);

            return Success(decision.Id, "Event report decision recorded successfully.");
        }, cancellationToken);
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string message,
        IEnumerable<string> errors,
        string? failureCode = null) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message, id)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors, id);
}
