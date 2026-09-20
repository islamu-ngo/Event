using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Analytics;

namespace Explore.Application.Features.PublicExperience.Requests.Commands;

public sealed record RelayAnalyticsEventCommand : ICommand<bool>
{
    public Guid? AuthenticatedUserId { get; init; }
    public RelayAnalyticsEventDto Payload { get; init; } = new();
}
