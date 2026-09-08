namespace Explore.Application.Features.Appearance.Requests.Commands;

using Explore.Application.DTOs.Appearance;
using Explore.Application.Responses;
using MediatR;

public sealed record UpdateCurrentUserAppearancePreferencesCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required UpdateUserAppearancePreferencesDto Preferences { get; init; }
}
