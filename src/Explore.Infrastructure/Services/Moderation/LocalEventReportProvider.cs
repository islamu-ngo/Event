using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;

namespace Explore.Infrastructure.Services.Moderation;

public sealed class LocalEventReportProvider : IEventReportProvider, IReportDecisionExecutor
{
    public Task<EventReportProviderSyncResult> SyncReportAsync(
        EventReportProviderEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EventReportProviderSyncResult.Success());
    }

    public Task<ReportDecisionExecutionResult> ExecuteAsync(
        ReportDecisionExecutionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReportDecisionExecutionResult.Success());
    }
}
