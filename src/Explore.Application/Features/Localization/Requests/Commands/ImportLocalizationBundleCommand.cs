using Explore.Application.DTOs.Localization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Localization.Requests.Commands;

public sealed record ImportLocalizationBundleCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required ImportLocalizationBundleDto Dto { get; init; }
}
