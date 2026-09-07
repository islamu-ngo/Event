using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventReporting.Handlers.Commands;

public sealed class UpdateMyReportCommunicationConsentCommandHandler(
    IEventReportRepository eventReportRepository,
    IPrivacyErasureStateRepository privacyErasureStateRepository,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    IAuthorizationProvider authorizationProvider,
    HybridCache cache,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateMyReportCommunicationConsentCommand, BaseCommandResponse<Guid>>
{
    private const string PrivacyErasureFencedFailureCode = "privacy_erasure_fenced";

    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateMyReportCommunicationConsentCommand request,
        CancellationToken cancellationToken)
    {
        var reporterUserId = currentUserService.UserId;
        if (await IsFencedAsync(reporterUserId, cancellationToken))
        {
            return FencedFailure();
        }

        var validationResult = await new UpdateMyReportCommunicationConsentCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    request.ReportId,
                    "Event report communication consent request is invalid.",
                    validationResult.Errors.Select(error => error.ErrorMessage),
                    EventReportFailureCodes.ValidationFailed),
                cancellationToken);
        }

        var tenantId = tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return await MaskIfFencedAsync(
                reporterUserId,
                Failure(
                    request.ReportId,
                    "Tenant context could not be resolved.",
                    ["Tenant context is required."],
                    EventReportFailureCodes.TenantUnresolved),
                cancellationToken);
        }

        if (reporterUserId is not { } resolvedReporterUserId)
        {
            return Failure(
                request.ReportId,
                "Reporter user could not be resolved.",
                ["Authenticated reporter user id is required."],
                EventReportFailureCodes.UserUnresolved);
        }

        var decision = await authorizationProvider.AuthorizeAsync(
            new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(ResourceKinds.User, AuthorizationActions.Users.Update),
                resolvedReporterUserId.ToString()),
            cancellationToken);
        if (!decision.IsAllowed)
        {
            throw new AuthorizationException(
                ResourceKinds.User,
                AuthorizationActions.Users.Update);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var consent = request.Request.Consent;
        var changed = false;
        var response = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await IsFencedAsync(reporterUserId, token))
            {
                return FencedFailure();
            }

            var report = await eventReportRepository.GetByIdForUpdateAsync(
                tenantId,
                request.ReportId,
                token);

            if (report is null
                || report.TenantId != tenantId
                || report.ReporterUserId != resolvedReporterUserId)
            {
                return await MaskIfFencedAsync(
                    reporterUserId,
                    Failure(
                        request.ReportId,
                        "Event report was not found.",
                        ["Event report was not found."],
                        EventReportFailureCodes.ReportNotFound),
                    token);
            }

            var attemptChanged = report.ReportCaseUpdatesConsent != consent.ReportCaseUpdatesConsent
                || report.ReportFollowUpContactConsent != consent.ReportFollowUpContactConsent;
            if (!attemptChanged)
            {
                return await MaskIfFencedAsync(
                    reporterUserId,
                    Success(report.Id, "Event report communication consent is unchanged."),
                    token);
            }

            if (await IsFencedAsync(reporterUserId, token))
            {
                return FencedFailure();
            }

            changed = true;
            report.ChangeReporterCommunicationConsent(
                consent.ReportCaseUpdatesConsent,
                consent.ReportFollowUpContactConsent,
                now);
            await eventReportRepository.Update(report);

            return Success(report.Id, "Event report communication consent updated successfully.");
        }, cancellationToken);

        if (changed)
        {
            await cache.RemoveAsync(
                $"event-reporting:my-report:{tenantId:N}:{resolvedReporterUserId:N}:{request.ReportId:N}",
                cancellationToken);
        }

        return response;
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

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
            "Event report communication consent update failed.");

    private static BaseCommandResponse<Guid> Failure(
        Guid id,
        string message,
        IEnumerable<string> errors,
        string failureCode) =>
        BaseCommandResponse.Failure<Guid>(failureCode, message, errors, id);

}
