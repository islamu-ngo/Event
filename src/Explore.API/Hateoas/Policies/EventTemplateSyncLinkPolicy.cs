using System.Security.Claims;
using Explore.API.Hateoas.Resources;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventTemplateSyncLinkPolicy : ILinkPolicy<EventTemplateSyncResource>
{
    public IEnumerable<LinkDefinition> GetLinks(EventTemplateSyncResource dto, ClaimsPrincipal? user)
    {
        // Custom-property templates are tenant-administered; the event and template version select the
        // payload, not the authority zone.
        var facts = new TenantScopedAuthorizationFacts(dto.TenantId);

        yield return new LinkDefinition(
            "sync-diff",
            RouteNames.GetEventTemplateSyncDiff,
            new { eventId = dto.EventId, templateVersion = dto.TargetTemplateVersion },
            HttpMethods.Get,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyTemplates.SyncDiff,
                ResourceKinds.CustomPropertyTemplate,
                dto.EventId.ToString(),
                facts: facts);

        if (dto.HasChanges)
        {
            yield return new LinkDefinition(
                "sync-apply",
                RouteNames.ApplyEventTemplateSync,
                new { eventId = dto.EventId },
                HttpMethods.Post,
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.CustomPropertyTemplates.SyncApply,
                    ResourceKinds.CustomPropertyTemplate,
                    dto.EventId.ToString(),
                    facts: facts);
        }

        yield return new LinkDefinition(
            "sync-history",
            RouteNames.GetEventTemplateSyncHistory,
            new { eventId = dto.EventId },
            HttpMethods.Get,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyTemplates.View,
                ResourceKinds.CustomPropertyTemplate,
                dto.EventId.ToString(),
                facts: facts);
    }
}
