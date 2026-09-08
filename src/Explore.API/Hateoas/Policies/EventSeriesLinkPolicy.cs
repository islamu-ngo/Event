namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Hateoas;

public sealed class EventSeriesDetailLinkPolicy : ILinkPolicy<EventSeriesDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventSeriesDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventSeriesById,
            new { id = dto.Id },
            HttpMethods.Get,
            dto.Title);

        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetEventSeries,
            null,
            HttpMethods.Get,
            "Event series");

        yield return new LinkDefinition(
            "actor",
            RouteNames.GetActorById,
            new { id = dto.ActorId },
            HttpMethods.Get,
            dto.ActorDisplayName ?? "Organizer");

        yield return CreateEditLink(dto.Id, dto.ActorId, dto.TenantId);
    }

    private static LinkDefinition CreateEditLink(Guid id, Guid actorId, Guid tenantId) =>
        new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSeries,
            new { id },
            HttpMethods.Patch,
            "Update event series",
            RequiresAuth: true)
        .RequirePermission(
            AuthorizationActions.Update,
            ResourceKinds.Actor,
            actorId.ToString(),
            new AuthorizationScope(tenantId.ToString()),
            new ActorAuthorizationFacts(tenantId, actorId));
}

public sealed class EventSeriesCollectionLinkPolicy : ICollectionLinkPolicy<EventSeriesListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventSeriesListDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetEventSeriesById,
            new { id = dto.Id },
            HttpMethods.Get,
            dto.Title);

        yield return new LinkDefinition(
            "actor",
            RouteNames.GetActorById,
            new { id = dto.ActorId },
            HttpMethods.Get,
            dto.ActorDisplayName ?? "Organizer");

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSeries,
            new { id = dto.Id },
            HttpMethods.Patch,
            "Update event series",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Update,
                ResourceKinds.Actor,
                dto.ActorId.ToString(),
                new AuthorizationScope(dto.TenantId.ToString()),
                new ActorAuthorizationFacts(dto.TenantId, dto.ActorId));
    }
}
