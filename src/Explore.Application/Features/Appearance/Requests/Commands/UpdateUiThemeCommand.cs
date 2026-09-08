namespace Explore.Application.Features.Appearance.Requests.Commands;

using Explore.Application.DTOs.Appearance;
using Explore.Application.Responses;
using MediatR;

public sealed record UpdateUiThemeCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid Id { get; init; }
    public required UpdateUiThemeDto UiThemeDto { get; init; }
}
