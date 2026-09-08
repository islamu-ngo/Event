using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class AssignEventReportCommandHandler(
    IEventReportRepository eventReportRepository,
    ITenantUserRepository tenantUserRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService) : IRequestHandler<AssignEventReportCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(AssignEventReportCommand request, CancellationToken cancellationToken)
    {
        var validationResult = await new AssignEventReportCommandValidator().ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                request.ReportId,
                "Event report assignment request is invalid.",
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

        if (!await tenantUserRepository.IsActiveTenantUserAsync(tenantId, request.AssigneeUserId, cancellationToken))
        {
            return Failure(request.ReportId, "Assignee is not active in the current tenant.", ["Assignee must be an active tenant user."], EventReportFailureCodes.AssigneeUnavailable);
        }

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

            if (reportCase.ConcurrencyStamp != request.ExpectedCaseConcurrencyStamp)
            {
                return Failure(request.ReportId, "Event report case was changed by another request.", ["Refresh the report case and try again."], EventReportFailureCodes.CaseConcurrencyConflict);
            }

            if (reportCase.Status is not (EventReportCaseStatus.Open or EventReportCaseStatus.Assigned))
            {
                return Failure(request.ReportId, "Only open or assigned report cases can be assigned.", ["Only open or assigned report cases can be assigned."], EventReportFailureCodes.CaseInvalidStatus);
            }

            if (report.IsTerminal)
            {
                return Failure(request.ReportId, "Terminal event reports cannot be assigned.", ["Terminal event reports cannot be assigned."], EventReportFailureCodes.ReportInvalidStatus);
            }

            var now = DateTime.UtcNow;
            reportCase.Assign(request.AssigneeUserId, now);
            report.UpdateStatus(EventReportStatus.UnderReview, now);

            await eventReportRepository.Update(report);

            return Success(report.Id, "Event report case assigned successfully.");
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
