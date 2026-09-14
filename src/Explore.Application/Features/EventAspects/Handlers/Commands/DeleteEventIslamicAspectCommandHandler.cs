namespace Explore.Application.Features.EventAspects.Handlers.Commands;

using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Application.Contracts.Operations;

/// <summary>
/// Handler for deleting the Islamic aspect from an event.
/// </summary>
public class DeleteEventIslamicAspectCommandHandler : ICommandHandler<DeleteEventIslamicAspectCommand, bool>
{
    private readonly IEventIslamicAspectRepository _islamicAspectRepository;

    public DeleteEventIslamicAspectCommandHandler(IEventIslamicAspectRepository islamicAspectRepository)
    {
        _islamicAspectRepository = islamicAspectRepository;
    }

    public async Task<bool> ExecuteAsync(DeleteEventIslamicAspectCommand request, CancellationToken cancellationToken)
    {
        var aspect = await _islamicAspectRepository.GetById(request.EventId);

        if (aspect == null)
        {
            return false;
        }

        await _islamicAspectRepository.Delete(aspect);

        return true;
    }
}
