namespace Explore.Application.Features.EventAspects.Handlers.Queries;

using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Features.EventAspects.Requests.Queries;
using MediatR;

/// <summary>
/// Handler for retrieving the Tech aspect of an event.
/// </summary>
public class GetEventTechAspectRequestHandler :
    IRequestHandler<GetEventTechAspectRequest, EventTechAspectDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventTechAspectRepository _techAspectRepository;
    private readonly IMapper _mapper;

    public GetEventTechAspectRequestHandler(
        IEventRepository eventRepository,
        IEventTechAspectRepository techAspectRepository,
        IMapper mapper)
    {
        _eventRepository = eventRepository;
        _techAspectRepository = techAspectRepository;
        _mapper = mapper;
    }

    public async Task<EventTechAspectDto?> Handle(GetEventTechAspectRequest request, CancellationToken cancellationToken)
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

        return _mapper.Map<EventTechAspectDto>(aspect);
    }
}

public sealed class GetManagedEventTechAspectRequestHandler(
    IEventTechAspectRepository techAspectRepository,
    IMapper mapper)
    : IRequestHandler<GetManagedEventTechAspectRequest, EventTechAspectDto?>
{
    public async Task<EventTechAspectDto?> Handle(
        GetManagedEventTechAspectRequest request,
        CancellationToken cancellationToken)
    {
        var aspect = await techAspectRepository.GetByEventId(request.EventId);
        return aspect is null ? null : mapper.Map<EventTechAspectDto>(aspect);
    }
}
