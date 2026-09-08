using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Telemetry;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Handlers.Commands;

public sealed class RecordEventPublicActionEngagementCommandHandler(BusinessMetrics metrics)
    : IRequestHandler<RecordEventPublicActionEngagementCommand, Unit>
{
    public Task<Unit> Handle(
        RecordEventPublicActionEngagementCommand request,
        CancellationToken cancellationToken)
    {
        metrics.RecordEventPublicActionEngagement(request.ActionKind, request.Surface);
        return Task.FromResult(Unit.Value);
    }
}
