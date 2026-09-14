using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Actors.Requests.Commands;

public sealed record DeleteActorCommand(Guid Id = default) : ICommand<bool>;
