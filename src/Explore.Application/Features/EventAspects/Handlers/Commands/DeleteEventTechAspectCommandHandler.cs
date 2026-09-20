namespace Explore.Application.Features.EventAspects.Handlers.Commands;

using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Application.Contracts.Operations;

/// <summary>
/// Handler for deleting the Tech aspect from an event.
/// </summary>
public class DeleteEventTechAspectCommandHandler : ICommandHandler<DeleteEventTechAspectCommand, bool>
{
    private readonly IEventTechAspectRepository _techAspectRepository;

    public DeleteEventTechAspectCommandHandler(IEventTechAspectRepository techAspectRepository)
    {
        _techAspectRepository = techAspectRepository;
    }

    public async Task<bool> ExecuteAsync(DeleteEventTechAspectCommand request, CancellationToken cancellationToken)
    {
        var aspect = await _techAspectRepository.GetById(request.EventId);

        if (aspect == null)
        {
            return false;
        }

        await _techAspectRepository.Delete(aspect);

        return true;
    }
}
