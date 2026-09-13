using Explore.Application.DTOs.Localization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Requests.Commands;

public sealed record ImportLocalizationBundleCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required ImportLocalizationBundleDto Dto { get; init; }
}
