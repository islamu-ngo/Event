using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Actors.Requests.Commands;

namespace Explore.Application.Features.Actors.Handlers.Commands;

public class DeleteActorCommandHandler : ICommandHandler<DeleteActorCommand, bool>
{
    private readonly IActorRepository _actorRepository;

    public DeleteActorCommandHandler(IActorRepository actorRepository)
    {
        _actorRepository = actorRepository;
    }

    public async Task<bool> ExecuteAsync(DeleteActorCommand request, CancellationToken cancellationToken)
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
