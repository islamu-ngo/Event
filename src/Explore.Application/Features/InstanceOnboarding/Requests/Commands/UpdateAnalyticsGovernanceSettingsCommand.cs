using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateAnalyticsGovernanceSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required Guid UserId { get; init; }
    public required PatchAnalyticsGovernanceSettingsDto Patch { get; init; }
}
