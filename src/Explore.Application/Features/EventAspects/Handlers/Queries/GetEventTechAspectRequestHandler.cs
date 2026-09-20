namespace Explore.Application.Features.EventAspects.Handlers.Queries;

using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Features.EventAspects.Requests.Queries;
using Explore.Application.Contracts.Operations;

/// <summary>
/// Handler for retrieving the Tech aspect of an event.
/// </summary>
public class GetEventTechAspectRequestHandler :
    IQueryHandler<GetEventTechAspectRequest, EventTechAspectDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventTechAspectRepository _techAspectRepository;

    public GetEventTechAspectRequestHandler(
        IEventRepository eventRepository,
        IEventTechAspectRepository techAspectRepository)
    {
        _eventRepository = eventRepository;
        _techAspectRepository = techAspectRepository;
    }

    public async Task<EventTechAspectDto?> QueryAsync(GetEventTechAspectRequest request, CancellationToken cancellationToken)
    {
        var parentEvent = await _eventRepository.GetById(request.EventId);
        if (parentEvent is null || !await _eventRepository.IsPubliclyEligibleAsync(
                parentEvent.TenantId,
                parentEvent.Id,
                cancellationToken))
            return null;

        return await GetAspectAsync(request.EventId);
    }

    private async Task<EventTechAspectDto?> GetAspectAsync(Guid eventId)
    {
        var aspect = await _techAspectRepository.GetByEventId(eventId);

        if (aspect == null)
        {
            return null;
        }

        return EventMapper.ToDetail(aspect);
    }
}

public sealed class GetManagedEventTechAspectRequestHandler(
    IEventTechAspectRepository techAspectRepository)
    : IQueryHandler<GetManagedEventTechAspectRequest, EventTechAspectDto?>
{
    public async Task<EventTechAspectDto?> QueryAsync(
        GetManagedEventTechAspectRequest request,
        CancellationToken cancellationToken)
    {
        var aspect = await techAspectRepository.GetByEventId(request.EventId);
        return EventMapper.ToDetail(aspect);
    }
}
