using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Services;

public sealed class EventReportModerationService(
    IModerationReportClient apiClient,
    ILogger<EventReportModerationService> logger)
    : IEventReportModerationService
{
    public async Task<ModerationReportQueuePageResult> GetQueueAsync(
        Guid eventId,
        ModerationReportQueueQueryState query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();

        try
        {
            var response = await apiClient.GetModerationReportQueueAsync(
                eventId,
                statuses: ToSingleValueCollection(normalized.StatusCode),
                caseStatuses: ToSingleValueCollection(normalized.CaseStatusCode),
                priority: normalized.PriorityCode,
                queueCode: normalized.QueueCode,
                assignedModeratorUserId: normalized.AssignedModeratorUserId,
                unassignedOnly: normalized.UnassignedOnly ? true : null,
                openOnly: normalized.OpenOnly,
                reasonCode: normalized.ReasonCode,
                sortBy: normalized.SortBy,
                sortDescending: normalized.SortDescending,
                pageNumber: normalized.PageNumber,
                pageSize: normalized.PageSize,
                cancellationToken: cancellationToken);

            return new ModerationReportQueuePageResult(
                response.GetItems().ToArray(),
                response.PageNumber ?? normalized.PageNumber,
                response.PageSize ?? normalized.PageSize,
                response.TotalCount ?? 0,
                response.TotalPages ?? 0,
                response.HasPrevious == true,
                response.HasNext == true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not load moderation report queue for event {EventId} page {PageNumber}",
                eventId,
                normalized.PageNumber);

            return ModerationReportQueuePageResult.Empty(normalized.PageNumber, normalized.PageSize);
        }
    }

    public async Task<HalResourceOfModerationReportDetailDto?> GetDetailAsync(
        Guid eventId,
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetModerationReportDetailAsync(
                eventId,
                reportId,
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not load moderation report detail for event {EventId} report {ReportId}",
                eventId,
                reportId);

            return null;
        }
    }

    public Task<ModerationReportActionResult> TriageAsync(
        Guid eventId,
        Guid reportId,
        TriageModerationReportRequestDto request,
        CancellationToken cancellationToken = default)
        => ExecuteActionAsync(
            eventId,
            reportId,
            "triage",
            () => apiClient.TriageModerationReportAsync(
                eventId,
                reportId,
                request,
                cancellationToken: cancellationToken));

    public Task<ModerationReportActionResult> AssignAsync(
        Guid eventId,
        Guid reportId,
        AssignModerationReportRequestDto request,
        CancellationToken cancellationToken = default)
        => ExecuteActionAsync(
            eventId,
            reportId,
            "assign",
            () => apiClient.AssignModerationReportAsync(
                eventId,
                reportId,
                request,
                cancellationToken: cancellationToken));

    public Task<ModerationReportActionResult> DecideAsync(
        Guid eventId,
        Guid reportId,
        DecideModerationReportRequestDto request,
        CancellationToken cancellationToken = default)
        => ExecuteActionAsync(
            eventId,
            reportId,
            "decide",
            () => apiClient.DecideModerationReportAsync(
                eventId,
                reportId,
                request,
                cancellationToken: cancellationToken));

    public Task<ModerationReportActionResult> ExecuteDecisionAsync(
        Guid eventId,
        Guid reportId,
        ExecuteModerationReportDecisionRequestDto request,
        CancellationToken cancellationToken = default)
        => ExecuteActionAsync(
            eventId,
            reportId,
            "execute",
            () => apiClient.ExecuteModerationReportDecisionAsync(
                eventId,
                reportId,
                request,
                cancellationToken: cancellationToken));

    private async Task<ModerationReportActionResult> ExecuteActionAsync(
        Guid eventId,
        Guid reportId,
        string actionName,
        Func<Task<BaseCommandResponseOfGuid>> executeAsync)
    {
        try
        {
            var response = await executeAsync();
            return response.Success == true
                ? ModerationReportActionResult.Successful(response)
                : ModerationReportActionResult.Failed(response.Message, response.Errors, response.FailureCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException<BaseCommandResponseOfGuid> ex) when (ex.Result is not null)
        {
            logger.LogWarning(
                ex,
                "Moderation report action {ActionName} failed for event {EventId} report {ReportId}",
                actionName,
                eventId,
                reportId);

            return ModerationReportActionResult.Failed(ex.Result.Message, ex.Result.Errors, ex.Result.FailureCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Moderation report action {ActionName} could not be completed for event {EventId} report {ReportId}",
                actionName,
                eventId,
                reportId);

            return ModerationReportActionResult.Failed("Moderation report action could not be completed.");
        }
    }

    private static IReadOnlyCollection<string>? ToSingleValueCollection(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : [value];
}
