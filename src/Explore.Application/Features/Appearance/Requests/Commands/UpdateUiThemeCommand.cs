namespace Explore.Application.Features.Appearance.Requests.Commands;

using Explore.Application.DTOs.Appearance;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

public sealed record UpdateUiThemeCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid Id { get; init; }
    public required UpdateUiThemeDto UiThemeDto { get; init; }
}
