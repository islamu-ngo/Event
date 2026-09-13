using Explore.Application.DTOs.Localization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Requests.Commands;

public sealed record UpdateLocalizationGovernanceCommand : ICommand<BaseCommandResponse<Guid>>
{
    public UpdateLocalizationGovernanceDto Dto { get; init; } = new();
}
