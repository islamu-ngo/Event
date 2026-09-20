using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventTags.Requests.Commands;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTags.Handlers.Commands;

public class DeleteEventTagsCommandHandler : ICommandHandler<DeleteEventTagsCommand, bool>
{
    private readonly IEventTagsRepository _eventTagsRepository;

    public DeleteEventTagsCommandHandler(IEventTagsRepository eventTagsRepository)
    {
        _eventTagsRepository = eventTagsRepository;
    }

    public async Task<bool> ExecuteAsync(DeleteEventTagsCommand request, CancellationToken cancellationToken)
    {
        var eventTags = await _eventTagsRepository.GetById(request.Id);

        if (eventTags == null)
        {
            return false;
        }

        await _eventTagsRepository.Delete(eventTags);
        return true;
    }
}
