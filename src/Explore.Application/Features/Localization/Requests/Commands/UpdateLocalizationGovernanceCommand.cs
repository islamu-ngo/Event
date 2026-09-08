using Explore.Application.DTOs.Localization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Localization.Requests.Commands;

public sealed record UpdateLocalizationGovernanceCommand : IRequest<BaseCommandResponse<Guid>>
{
    public UpdateLocalizationGovernanceDto Dto { get; init; } = new();
}
