namespace Explore.Application.Features.Appearance.Requests.Commands;

using Explore.Application.DTOs.Appearance;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

public sealed record CreateUiThemeCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required CreateUiThemeDto UiThemeDto { get; init; }
}
