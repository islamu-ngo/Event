using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.EventReporting;

public interface IEventReportingService
{
    Task<HalResourceOfEventReportOptionsDto?> GetOptionsAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task<EventReportSubmissionResult> SubmitAsync(
        SubmitEventReportDto request,
        CancellationToken cancellationToken = default);

    Task<EventReportPageResult> GetMyReportsAsync(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<HalResourceOfMyEventReportDto?> GetMyReportAsync(
        Guid reportId,
        CancellationToken cancellationToken = default);

    Task<EventReportConsentUpdateResult> UpdateCommunicationConsentAsync(
        HalResourceOfMyEventReportDto report,
        bool reportCaseUpdatesConsent,
        bool reportFollowUpContactConsent,
        CancellationToken cancellationToken = default);
}
