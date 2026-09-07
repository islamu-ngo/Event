namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Studio;
using Explore.Application.Hateoas;

public sealed class StudioContextLinkPolicy : ILinkPolicy<StudioContextDto>
{
    public IEnumerable<LinkDefinition> GetLinks(StudioContextDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetStudioContext,
            null,
            HttpMethods.Get,
            "Studio context",
            RequiresAuth: true);

        Guid? selectedActorId = dto.SelectedActorId;
        if (dto.AllowedLinkRelations.Contains(LinkRelations.ViewRegistrationOrders)
            && selectedActorId is Guid actorId)
        {
            yield return new LinkDefinition(
                LinkRelations.ViewRegistrationOrders,
                RouteNames.GetManagedEventsByActor,
                new { actorId },
                HttpMethods.Get,
                "View registration orders",
                RequiresAuth: true);
        }

        if (dto.AllowedLinkRelations.Contains(LinkRelations.ViewParticipants)
            && selectedActorId is Guid participantActorId)
        {
            yield return new LinkDefinition(
                LinkRelations.ViewParticipants,
                RouteNames.GetManagedEventsByActor,
                new { actorId = participantActorId },
                HttpMethods.Get,
                "View participants",
                RequiresAuth: true);
        }

        if (dto.AllowedLinkRelations.Contains(LinkRelations.ViewRegistrationProviderHealth)
            && selectedActorId is Guid healthActorId)
        {
            yield return new LinkDefinition(
                LinkRelations.ViewRegistrationProviderHealth,
                RouteNames.GetManagedEventsByActor,
                new { actorId = healthActorId },
                HttpMethods.Get,
                "View registration provider health",
                RequiresAuth: true);
        }

        if (dto.AllowedLinkRelations.Contains(LinkRelations.ManageRegistrationChannels)
            && selectedActorId is Guid channelsActorId)
        {
            yield return new LinkDefinition(
                LinkRelations.ManageRegistrationChannels,
                RouteNames.GetManagedEventsByActor,
                new { actorId = channelsActorId },
                HttpMethods.Get,
                "Manage registration channels",
                RequiresAuth: true);
        }
    }
}

/// <summary>Studio context is a per-user singleton; its collection shape carries no affordances.</summary>
public sealed class StudioContextCollectionLinkPolicy : ICollectionLinkPolicy<StudioContextDto>;
