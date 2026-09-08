using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Actors.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.Actors.Handlers.Commands;

public class DeleteActorCommandHandler : IRequestHandler<DeleteActorCommand, bool>
{
    private readonly IActorRepository _actorRepository;

    public DeleteActorCommandHandler(IActorRepository actorRepository)
    {
        _actorRepository = actorRepository;
    }

    public async Task<bool> Handle(DeleteActorCommand request, CancellationToken cancellationToken)
    {
        var actor = await _actorRepository.GetById(request.Id);

        if (actor == null)
        {
            return false;
        }

        await _actorRepository.Delete(actor);

        return true;
    }
}
