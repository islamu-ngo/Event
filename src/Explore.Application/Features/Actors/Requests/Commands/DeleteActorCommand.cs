using MediatR;

namespace Explore.Application.Features.Actors.Requests.Commands;

public sealed record DeleteActorCommand(Guid Id = default) : IRequest<bool>;
