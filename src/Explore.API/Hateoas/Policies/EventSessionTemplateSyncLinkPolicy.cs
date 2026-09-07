using System.Security.Claims;
using Explore.API.Hateoas.Resources;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class EventSessionTemplateSyncLinkPolicy : ILinkPolicy<EventSessionTemplateSyncResource>
{
    public IEnumerable<LinkDefinition> GetLinks(EventSessionTemplateSyncResource dto, ClaimsPrincipal? user)
    {
        // Custom-property templates are tenant-administered; the session and template version select
        // the payload, not the authority zone.
        var facts = new TenantScopedAuthorizationFacts(dto.TenantId);

        yield return new LinkDefinition(
            "sync-diff",
            RouteNames.GetEventSessionTemplateSyncDiff,
            new { sessionId = dto.SessionId, templateVersion = dto.TargetTemplateVersion },
            HttpMethods.Get,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyTemplates.SyncDiff,
                ResourceKinds.CustomPropertyTemplate,
                dto.SessionId.ToString(),
                facts: facts);

        if (dto.HasChanges)
        {
            yield return new LinkDefinition(
                "sync-apply",
                RouteNames.ApplyEventSessionTemplateSync,
                new { sessionId = dto.SessionId },
                HttpMethods.Post,
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.CustomPropertyTemplates.SyncApply,
                    ResourceKinds.CustomPropertyTemplate,
                    dto.SessionId.ToString(),
                    facts: facts);
        }

        yield return new LinkDefinition(
            "sync-history",
            RouteNames.GetEventSessionTemplateSyncHistory,
            new { sessionId = dto.SessionId },
            HttpMethods.Get,
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.CustomPropertyTemplates.View,
                ResourceKinds.CustomPropertyTemplate,
                dto.SessionId.ToString(),
                facts: facts);
    }
}
