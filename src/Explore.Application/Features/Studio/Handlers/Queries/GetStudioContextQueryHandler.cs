using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Studio;
using Explore.Application.Exceptions;
using Explore.Application.Features.AiAssistant.Actors;
using Explore.Application.Features.Studio.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Studio.Handlers.Queries;

public sealed class GetStudioContextQueryHandler(
    IUserContext userContext,
    ITenantContext tenantContext,
    IAiAssistantActorContextService actorContexts,
    IEventRepository events,
    IAuthorizationProvider authorization)
    : IRequestHandler<GetStudioContextQuery, StudioContextDto>
{
    public async Task<StudioContextDto> Handle(GetStudioContextQuery request, CancellationToken cancellationToken)
    {
        Guid userId = userContext.GetRequiredUserId();
        Guid tenantId = tenantContext.TenantId;
        AiAssistantActorContextResolution actor = await actorContexts.ResolveAuthorizedActorAsync(
            tenantId,
            userId,
            request.ActorId,
            cancellationToken);

        if (!actor.Succeeded)
        {
            throw new AuthorizationException(ResourceKinds.Actor, AuthorizationActions.View);
        }

        var context = new StudioContextDto { SelectedActorId = actor.ActorId };
        if (actor.ActorId is not { } actorId)
        {
            return context;
        }

        IReadOnlyList<Event> managedEvents = await events.GetEventsByActorWithDetails(actorId, cancellationToken);
        List<AuthorizationRequest> checks = managedEvents
            .Where(IsPlatformManaged)
            .SelectMany(eventEntity => new[]
            {
                new AuthorizationRequest(
                    ResourceKinds.Event,
                    eventEntity.Id.ToString("D"),
                    AuthorizationActions.Events.ManageRegistrations,
                    Scope: ResourceDescriptors.EventAuthorizationTarget.GetScope(eventEntity),
                    Facts: ResourceDescriptors.EventAuthorizationTarget.GetFacts(eventEntity)),
                new AuthorizationRequest(
                    ResourceKinds.Event,
                    eventEntity.Id.ToString("D"),
                    AuthorizationActions.Events.ManageRegistrationChannels,
                    Scope: ResourceDescriptors.EventAuthorizationTarget.GetScope(eventEntity),
                    Facts: ResourceDescriptors.EventAuthorizationTarget.GetFacts(eventEntity)),
                new AuthorizationRequest(
                    ResourceKinds.Event,
                    eventEntity.Id.ToString("D"),
                    AuthorizationActions.Events.ViewRegistrationProviderHealth,
                    Scope: ResourceDescriptors.EventAuthorizationTarget.GetScope(eventEntity),
                    Facts: ResourceDescriptors.EventAuthorizationTarget.GetFacts(eventEntity))
            })
            .ToList();
        if (checks.Count == 0)
        {
            return context;
        }

        try
        {
            IReadOnlyList<AuthorizationDecision> decisions = await authorization.AuthorizeBatchAsync(checks, cancellationToken);
            var allowedLinkRelations = new HashSet<string>(StringComparer.Ordinal);
            if (decisions.Where((_, index) => index % 3 == 0).Any(decision => decision.IsAllowed))
            {
                allowedLinkRelations.Add(LinkRelations.ViewRegistrationOrders);
                allowedLinkRelations.Add(LinkRelations.ViewParticipants);
            }

            if (decisions.Where((_, index) => index % 3 == 1).Any(decision => decision.IsAllowed))
            {
                allowedLinkRelations.Add(LinkRelations.ManageRegistrationChannels);
            }

            if (decisions.Where((_, index) => index % 3 == 2).Any(decision => decision.IsAllowed))
            {
                allowedLinkRelations.Add(LinkRelations.ViewRegistrationProviderHealth);
            }

            context = context with { AllowedLinkRelations = allowedLinkRelations };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return context;
        }

        return context;
    }

    private static bool IsPlatformManaged(Event eventEntity) =>
        eventEntity.ParticipationConfiguration?.ParticipationHandlingModeId ==
        (int)ParticipationHandlingModeEnum.PlatformManaged;
}
