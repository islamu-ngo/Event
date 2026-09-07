namespace Explore.Application.Features.Appearance.Requests.Commands;

using MediatR;

public sealed record DeleteUiThemeCommand(Guid Id = default) : IRequest<bool>;
