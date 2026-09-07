using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.EventReporting;

public sealed record EventReportSubmissionResult(
    bool Success,
    Guid? ReportId,
    string Message,
    IReadOnlyList<string> Errors,
    string? FailureCode)
{
    public static EventReportSubmissionResult Successful(Guid? reportId, string? message)
        => new(true, reportId, message ?? "Event report submitted.", [], null);

    public static EventReportSubmissionResult Failed(
        string? message,
        IEnumerable<string>? errors = null,
        string? failureCode = null)
        => new(false, null, message ?? "Event report could not be submitted.", errors?.ToArray() ?? [], failureCode);
}

public sealed record EventReportPageResult(
    IReadOnlyList<HalResourceOfMyEventReportDto> Reports,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext)
{
    public static EventReportPageResult Empty(int pageNumber, int pageSize)
        => new([], Math.Max(1, pageNumber), Math.Max(1, pageSize), 0, 0, false, false);
}

public sealed record EventReportConsentUpdateResult(
    bool Success,
    HalResourceOfMyEventReportDto? Report,
    string Message)
{
    public static EventReportConsentUpdateResult Successful(HalResourceOfMyEventReportDto report)
        => new(true, report, "Email preferences saved.");

    public static EventReportConsentUpdateResult Failed()
        => new(false, null, "Email preferences could not be saved.");
}
