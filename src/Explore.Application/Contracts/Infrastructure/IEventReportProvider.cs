using Explore.Application.Features.EventReporting.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface IEventReportProvider
{
    Task<EventReportProviderSyncResult> SyncReportAsync(
        EventReportProviderEnvelope envelope,
        CancellationToken cancellationToken = default);
}
