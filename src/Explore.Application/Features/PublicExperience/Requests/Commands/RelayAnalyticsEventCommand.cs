using Explore.Application.DTOs.Analytics;
using MediatR;

namespace Explore.Application.Features.PublicExperience.Requests.Commands;

public sealed record RelayAnalyticsEventCommand : IRequest<bool>
{
    public Guid? AuthenticatedUserId { get; init; }
    public RelayAnalyticsEventDto Payload { get; init; } = new();
}
