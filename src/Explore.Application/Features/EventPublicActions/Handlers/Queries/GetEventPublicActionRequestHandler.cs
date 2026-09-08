using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.EventPublicActions.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using MediatR;

namespace Explore.Application.Features.EventPublicActions.Handlers.Queries;

public sealed class GetEventPublicActionRequestHandler(
    IEventRepository eventRepository,
    IEventPublicActionRepository actionRepository,
    IMapper mapper)
    : IRequestHandler<GetEventPublicActionRequest, EventPublicActionDto?>
{
    public async Task<EventPublicActionDto?> Handle(
        GetEventPublicActionRequest request,
        CancellationToken cancellationToken)
    {
        var @event = await eventRepository.GetById(request.EventId);
        if (@event is null
            || @event.EventStatusId != (int)EventStatusEnum.Published
            || @event.VisibilityTypeId != (int)VisibilityTypeEnum.Public
            || @event.ParticipationConfiguration is null)
        {
            return null;
        }

        if (!await eventRepository.IsPubliclyEligibleAsync(
                @event.TenantId,
                @event.Id,
                cancellationToken))
        {
            return null;
        }

        var action = await actionRepository.GetDetailsAsync(
            request.ActionId,
            trackChanges: false,
            cancellationToken);
        return action is not null
            && action.EventId == request.EventId
            && action.HealthStateId == (int)EventPublicActionHealthStateEnum.Active
            && EventAuthorityRules.IsPublicActionAllowed(
                @event.ParticipationConfiguration.ParticipationHandlingModeId,
                action.EventPublicActionKindId)
                ? mapper.Map<EventPublicActionDto>(action)
                : null;
    }
}
