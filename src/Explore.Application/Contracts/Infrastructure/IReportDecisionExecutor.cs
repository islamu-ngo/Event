using Explore.Application.Features.EventReporting.Models;

namespace Explore.Application.Contracts.Infrastructure;

public interface IReportDecisionExecutor
{
    Task<ReportDecisionExecutionResult> ExecuteAsync(
        ReportDecisionExecutionEnvelope envelope,
        CancellationToken cancellationToken = default);
}
