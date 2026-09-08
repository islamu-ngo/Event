using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.EventReporting;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Services;

public sealed class EventReportingService(
    IEventReportsClient apiClient,
    ILogger<EventReportingService> logger)
    : IEventReportingService
{
    private const string UpdateCommunicationConsentRelation = "update-communication-consent";

    public async Task<HalResourceOfEventReportOptionsDto?> GetOptionsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetEventReportOptionsAsync(eventId, cancellationToken: cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            logger.LogInformation("Event-report options were not found for event {EventId}", eventId);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load event-report options for event {EventId}", eventId);
            return null;
        }
    }

    public async Task<EventReportSubmissionResult> SubmitAsync(
        SubmitEventReportDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.SubmitEventReportAsync(request, cancellationToken: cancellationToken);
            return response.Success == true
                ? EventReportSubmissionResult.Successful(response.Id, response.Message)
                : EventReportSubmissionResult.Failed(response.Message, response.Errors, response.FailureCode);
        }
        catch (ApiException<ProblemDetails> ex)
        {
            logger.LogWarning(
                ex,
                "Event-report submission failed with ProblemDetails status {StatusCode} for event {EventId}",
                ex.StatusCode,
                request.EventId);

            return EventReportSubmissionResult.Failed(
                ex.Result.Detail ?? ex.Result.Title,
                failureCode: TryGetProblemCode(ex.Result));
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Event-report submission failed with API status {StatusCode} for event {EventId}",
                ex.StatusCode,
                request.EventId);

            return EventReportSubmissionResult.Failed("Event report could not be submitted.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected event-report submission failure for event {EventId}", request.EventId);
            return EventReportSubmissionResult.Failed("Event report could not be submitted.");
        }
    }

    public async Task<EventReportPageResult> GetMyReportsAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await apiClient.GetMyEventReportsAsync(
                pageNumber,
                pageSize,
                cancellationToken: cancellationToken);

            return new EventReportPageResult(
                response.GetItems().ToArray(),
                response.PageNumber ?? pageNumber,
                response.PageSize ?? pageSize,
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
            logger.LogWarning(ex, "Could not load reporter-owned event reports page {PageNumber}", pageNumber);
            return EventReportPageResult.Empty(pageNumber, pageSize);
        }
    }

    public async Task<HalResourceOfMyEventReportDto?> GetMyReportAsync(
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetMyEventReportAsync(reportId, cancellationToken: cancellationToken);
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
            logger.LogWarning(ex, "Could not load reporter-owned event report {ReportId}", reportId);
            return null;
        }
    }

    public async Task<EventReportConsentUpdateResult> UpdateCommunicationConsentAsync(
        HalResourceOfMyEventReportDto report,
        bool reportCaseUpdatesConsent,
        bool reportFollowUpContactConsent,
        CancellationToken cancellationToken = default)
    {
        if (report.Id is not { } reportId
            || reportId == Guid.Empty
            || !HasMatchingConsentUpdateLink(report, reportId))
        {
            logger.LogWarning("Rejected event-report consent update without a matching HAL affordance");
            return EventReportConsentUpdateResult.Failed();
        }

        try
        {
            var updated = await apiClient.UpdateMyEventReportCommunicationConsentAsync(
                reportId,
                new UpdateMyReportCommunicationConsentDto
                {
                    Consent = new ReportCommunicationConsentUpdateDto
                    {
                        ReportCaseUpdatesConsent = reportCaseUpdatesConsent,
                        ReportFollowUpContactConsent = reportFollowUpContactConsent
                    }
                },
                cancellationToken: cancellationToken);

            if (updated.Id != reportId)
            {
                logger.LogWarning(
                    "Event-report consent update returned a mismatched report resource for report {ReportId}",
                    reportId);
                return EventReportConsentUpdateResult.Failed();
            }

            return EventReportConsentUpdateResult.Successful(updated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "Event-report consent update failed with API status {StatusCode} for report {ReportId}",
                ex.StatusCode,
                reportId);
            return EventReportConsentUpdateResult.Failed();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected event-report consent update failure for report {ReportId}", reportId);
            return EventReportConsentUpdateResult.Failed();
        }
    }

    private static bool HasMatchingConsentUpdateLink(HalResourceOfMyEventReportDto report, Guid reportId)
    {
        if (report._links?.TryGetValue(UpdateCommunicationConsentRelation, out var link) != true
            || !string.Equals(link.Method, "PATCH", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(link.Href)
            || !string.Equals(link.Href, link.Href.Trim(), StringComparison.Ordinal)
            || !Uri.TryCreate(link.Href, UriKind.RelativeOrAbsolute, out var uri))
        {
            return false;
        }

        var hrefWithoutQueryOrFragment = link.Href.Split(['?', '#'], 2)[0];
        string path;
        if (uri.IsAbsoluteUri)
        {
            if (uri.Scheme is not ("http" or "https"))
            {
                return false;
            }

            var schemeSeparator = hrefWithoutQueryOrFragment.IndexOf("://", StringComparison.Ordinal);
            var pathStart = schemeSeparator < 0
                ? -1
                : hrefWithoutQueryOrFragment.IndexOf('/', schemeSeparator + 3);
            if (pathStart < 0)
            {
                return false;
            }

            path = hrefWithoutQueryOrFragment[pathStart..];
        }
        else
        {
            path = hrefWithoutQueryOrFragment;
        }

        var expectedPath = $"/api/event-reports/my/{reportId:D}/communication-consent";
        return string.Equals(path.TrimEnd('/'), expectedPath, StringComparison.OrdinalIgnoreCase)
               && path.Length - path.TrimEnd('/').Length <= 1;
    }

    private static string? TryGetProblemCode(ProblemDetails problem)
    {
        if (problem.AdditionalProperties.TryGetValue("code", out var value))
        {
            return value?.ToString();
        }

        return null;
    }
}
