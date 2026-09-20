using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateAnalyticsGovernanceSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required Guid UserId { get; init; }
    public required PatchAnalyticsGovernanceSettingsDto Patch { get; init; }
}
