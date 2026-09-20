namespace Explore.Application.Features.Appearance.Requests.Commands;

using Explore.Application.Contracts.Operations;

public sealed record DeleteUiThemeCommand(Guid Id = default) : ICommand<bool>;
