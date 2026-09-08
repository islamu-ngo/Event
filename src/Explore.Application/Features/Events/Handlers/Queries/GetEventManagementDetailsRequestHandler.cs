using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Queries;

public sealed class GetEventManagementDetailsRequestHandler : IRequestHandler<GetEventManagementDetailsRequest, EventDto?>
{
    private readonly IEventRepository _eventRepository;
    private readonly IEventDetailsProjectionService _detailsProjectionService;

    public GetEventManagementDetailsRequestHandler(
        IEventRepository eventRepository,
        IEventDetailsProjectionService detailsProjectionService)
    {
        _eventRepository = eventRepository;
        _detailsProjectionService = detailsProjectionService;
    }

    public async Task<EventDto?> Handle(GetEventManagementDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventDto = await _detailsProjectionService.BuildAsync(request.Id, cancellationToken);
        if (eventDto is null)
            return null;

        var isPubliclyEligible = await _eventRepository.IsPubliclyEligibleAsync(
            eventDto.TenantId,
            eventDto.Id,
            cancellationToken);

        eventDto.IsPubliclyEligible = isPubliclyEligible;
        eventDto.IsManagementView = true;
        await _detailsProjectionService.ResolveImageUrlsAsync(eventDto, cancellationToken);
        return eventDto;
    }
}
