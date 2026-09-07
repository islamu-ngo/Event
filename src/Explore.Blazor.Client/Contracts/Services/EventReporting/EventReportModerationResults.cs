using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.EventReporting;

public sealed record ModerationReportQueueQueryState
{
    public string? StatusCode { get; init; }
    public string? CaseStatusCode { get; init; }
    public string? PriorityCode { get; init; }
    public string? QueueCode { get; init; }
    public Guid? AssignedModeratorUserId { get; init; }
    public bool UnassignedOnly { get; init; }
    public bool OpenOnly { get; init; } = true;
    public string? ReasonCode { get; init; }
    public string? SortBy { get; init; } = "created_at";
    public bool SortDescending { get; init; } = true;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public ModerationReportQueueQueryState Normalize()
        => this with
        {
            StatusCode = NormalizeText(StatusCode),
            CaseStatusCode = NormalizeText(CaseStatusCode),
            PriorityCode = NormalizeText(PriorityCode),
            QueueCode = NormalizeText(QueueCode),
            ReasonCode = NormalizeText(ReasonCode),
            SortBy = NormalizeText(SortBy),
            PageNumber = Math.Max(1, PageNumber),
            PageSize = Math.Clamp(PageSize, 5, 100)
        };

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ModerationReportQueuePageResult(
    IReadOnlyList<HalResourceOfModerationReportQueueItemDto> Reports,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPrevious,
    bool HasNext)
{
    public static ModerationReportQueuePageResult Empty(int pageNumber, int pageSize)
        => new([], Math.Max(1, pageNumber), Math.Max(1, pageSize), 0, 0, false, false);
}
