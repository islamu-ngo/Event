using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.EventPublicActions.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventPublicActions.Handlers.Queries;

public sealed class GetEventPublicActionsRequestHandler(
    IEventRepository eventRepository,
    IEventPublicActionRepository actionRepository)
    : IQueryHandler<GetEventPublicActionsRequest, IReadOnlyList<EventPublicActionDto>>
{
    public async Task<IReadOnlyList<EventPublicActionDto>> QueryAsync(
        GetEventPublicActionsRequest request,
        CancellationToken cancellationToken)
    {
        var @event = await eventRepository.GetById(request.EventId);
        if (@event is null
            || @event.EventStatusId != (int)EventStatusEnum.Published
            || @event.VisibilityTypeId != (int)VisibilityTypeEnum.Public
            || @event.ParticipationConfiguration is null)
        {
            return [];
        }

        if (!await eventRepository.IsPubliclyEligibleAsync(
                @event.TenantId,
                @event.Id,
                cancellationToken))
        {
            return [];
        }

        var actions = await actionRepository.ListByEventAsync(
            request.EventId,
            trackChanges: false,
            cancellationToken);
        return actions.Where(action =>
                action.HealthStateId == (int)EventPublicActionHealthStateEnum.Active
                && EventAuthorityRules.IsPublicActionAllowed(
                    @event.ParticipationConfiguration.ParticipationHandlingModeId,
                    action.EventPublicActionKindId))
            .Select(EventMapper.ToDetail).ToList();
    }
}
