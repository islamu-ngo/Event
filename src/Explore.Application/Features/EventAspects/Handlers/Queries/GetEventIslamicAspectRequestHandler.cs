namespace Explore.Application.Features.EventAspects.Handlers.Queries;

using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Features.EventAspects.Requests.Queries;
using Explore.Application.Contracts.Operations;

/// <summary>
/// Handler for retrieving the Islamic aspect of an event.
/// </summary>
public class GetEventIslamicAspectRequestHandler :
    IQueryHandler<GetEventIslamicAspectRequest, EventIslamicAspectDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventIslamicAspectRepository _islamicAspectRepository;

    public GetEventIslamicAspectRequestHandler(
        IEventRepository eventRepository,
        IEventIslamicAspectRepository islamicAspectRepository)
    {
        _eventRepository = eventRepository;
        _islamicAspectRepository = islamicAspectRepository;
    }

    public async Task<EventIslamicAspectDto?> QueryAsync(GetEventIslamicAspectRequest request, CancellationToken cancellationToken)
    {
        var parentEvent = await _eventRepository.GetById(request.EventId);
        if (parentEvent is null || !await _eventRepository.IsPubliclyEligibleAsync(
                parentEvent.TenantId,
                parentEvent.Id,
                cancellationToken))
            return null;

        return await GetAspectAsync(request.EventId);
    }

    private async Task<EventIslamicAspectDto?> GetAspectAsync(Guid eventId)
    {
        var aspect = await _islamicAspectRepository.GetByEventIdWithDetails(eventId);

        if (aspect == null)
        {
            return null;
        }

        return EventMapper.ToDetail(aspect);
    }
}

public sealed class GetManagedEventIslamicAspectRequestHandler(
    IEventIslamicAspectRepository islamicAspectRepository)
    : IQueryHandler<GetManagedEventIslamicAspectRequest, EventIslamicAspectDto?>
{
    public async Task<EventIslamicAspectDto?> QueryAsync(
        GetManagedEventIslamicAspectRequest request,
        CancellationToken cancellationToken)
    {
        var aspect = await islamicAspectRepository.GetByEventIdWithDetails(request.EventId);
        return EventMapper.ToDetail(aspect);
    }
}
