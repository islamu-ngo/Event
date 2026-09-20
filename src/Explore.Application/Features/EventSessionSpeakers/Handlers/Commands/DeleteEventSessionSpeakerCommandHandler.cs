using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Commands;

public class DeleteEventSessionSpeakerCommandHandler : ICommandHandler<DeleteEventSessionSpeakerCommand, bool>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;

    public DeleteEventSessionSpeakerCommandHandler(
        IEventSessionSpeakerRepository speakerRepository,
        IEventSessionRepository eventSessionRepository,
        ITenantContext tenantContext,
        HybridCache cache)
    {
        _speakerRepository = speakerRepository;
        _eventSessionRepository = eventSessionRepository;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    public async Task<bool> ExecuteAsync(DeleteEventSessionSpeakerCommand request, CancellationToken cancellationToken)
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
        if (eventSession is null || eventSession.TenantId != speaker.TenantId || eventSession.TenantId != _tenantContext.TenantId)
        {
            return false;
        }

        await _speakerRepository.Delete(speaker);
        await _cache.RemoveAsync($"event:detail:{eventSession.EventId}", cancellationToken);
        await _cache.RemoveByTagAsync(CacheTags.EventListByTenant(eventSession.TenantId), cancellationToken);

        return true;
    }
}
