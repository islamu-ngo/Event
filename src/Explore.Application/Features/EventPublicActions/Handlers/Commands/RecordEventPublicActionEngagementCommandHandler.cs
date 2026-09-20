using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Telemetry;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventPublicActions.Handlers.Commands;

public sealed class RecordEventPublicActionEngagementCommandHandler(BusinessMetrics metrics)
    : ICommandHandler<RecordEventPublicActionEngagementCommand>
{
    public Task ExecuteAsync(
        RecordEventPublicActionEngagementCommand request,
        CancellationToken cancellationToken)
    {
        metrics.RecordEventPublicActionEngagement(request.ActionKind, request.Surface);
        return Task.CompletedTask;
    }
}
