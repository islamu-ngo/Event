using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.EventReporting;

public interface IEventReportModerationService
{
    Task<ModerationReportQueuePageResult> GetQueueAsync(
        Guid eventId,
        ModerationReportQueueQueryState query,
        CancellationToken cancellationToken = default);

    Task<HalResourceOfModerationReportDetailDto?> GetDetailAsync(
        Guid eventId,
        Guid reportId,
        CancellationToken cancellationToken = default);

    Task<ModerationReportActionResult> TriageAsync(
        Guid eventId,
        Guid reportId,
        TriageModerationReportRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ModerationReportActionResult> AssignAsync(
        Guid eventId,
        Guid reportId,
        AssignModerationReportRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ModerationReportActionResult> DecideAsync(
        Guid eventId,
        Guid reportId,
        DecideModerationReportRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ModerationReportActionResult> ExecuteDecisionAsync(
        Guid eventId,
        Guid reportId,
        ExecuteModerationReportDecisionRequestDto request,
        CancellationToken cancellationToken = default);
}
