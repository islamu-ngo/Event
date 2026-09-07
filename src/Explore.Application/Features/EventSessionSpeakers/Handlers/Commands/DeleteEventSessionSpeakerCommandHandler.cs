using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Commands;

public class DeleteEventSessionSpeakerCommandHandler : IRequestHandler<DeleteEventSessionSpeakerCommand, bool>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly HybridCache _cache;

    public DeleteEventSessionSpeakerCommandHandler(
        IEventSessionSpeakerRepository speakerRepository,
        IEventSessionRepository eventSessionRepository,
        HybridCache cache)
    {
        _speakerRepository = speakerRepository;
        _eventSessionRepository = eventSessionRepository;
        _cache = cache;
    }

    public async Task<bool> Handle(DeleteEventSessionSpeakerCommand request, CancellationToken cancellationToken)
    {
        var speaker = await _speakerRepository.GetById(request.Id);

        if (speaker == null)
        {
            return false;
        }

        if (speaker.EventSessionId != request.EventSessionId)
        {
            return false;
        }

        var eventSession = await _eventSessionRepository.GetById(speaker.EventSessionId);
        if (eventSession is null || eventSession.TenantId != speaker.TenantId || eventSession.TenantId != request.TenantId)
        {
            return false;
        }

        await _speakerRepository.Delete(speaker);
        await _cache.RemoveAsync($"event:detail:{eventSession.EventId}", cancellationToken);
        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(eventSession.TenantId), cancellationToken);

        return true;
    }
}
