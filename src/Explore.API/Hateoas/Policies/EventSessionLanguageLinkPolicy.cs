namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Hateoas;

public sealed class EventSessionLanguageDetailLinkPolicy : ILinkPolicy<EventSessionLanguageDto>
{
    public IEnumerable<LinkDefinition> GetLinks(EventSessionLanguageDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetEventSessionLanguages,
            new { eventSessionId = dto.EventSessionId },
            HttpMethods.Get,
            "Session languages");

        yield return new LinkDefinition(
            "session",
            RouteNames.GetEventSessionById,
            new { id = dto.EventSessionId },
            HttpMethods.Get,
            dto.EventSessionTitle ?? "Event session");

        yield return CreateEditLink(dto.Id, dto.EventSessionId, dto.TenantId, dto.EventId);
    }

    private static LinkDefinition CreateEditLink(
        int id,
        Guid eventSessionId,
        Guid tenantId,
        Guid eventId) =>
        new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSessionLanguage,
            new { id },
            HttpMethods.Patch,
            "Update session language",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Update,
                ResourceKinds.EventSession,
                eventSessionId.ToString(),
                new AuthorizationScope(tenantId.ToString()),
                new EventScopedAuthorizationFacts(tenantId, eventId, eventSessionId));
}

public sealed class EventSessionLanguageCollectionLinkPolicy : ICollectionLinkPolicy<EventSessionLanguageListDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(EventSessionLanguageListDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            "session",
            RouteNames.GetEventSessionById,
            new { id = dto.EventSessionId },
            HttpMethods.Get,
            dto.EventSessionTitle ?? "Event session");

        yield return new LinkDefinition(
            LinkRelations.Edit,
            RouteNames.UpdateEventSessionLanguage,
            new { id = dto.Id },
            HttpMethods.Patch,
            "Update session language",
            RequiresAuth: true)
            .RequirePermission(
                AuthorizationActions.Update,
                ResourceKinds.EventSession,
                dto.EventSessionId.ToString(),
                new AuthorizationScope(dto.TenantId.ToString()),
                new EventScopedAuthorizationFacts(dto.TenantId, dto.EventId, dto.EventSessionId));
    }
}
