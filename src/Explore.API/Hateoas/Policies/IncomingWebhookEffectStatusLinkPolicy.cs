using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class IncomingWebhookEffectStatusDetailLinkPolicy
    : ILinkPolicy<IncomingWebhookEffectStatusDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        IncomingWebhookEffectStatusDto dto,
        ClaimsPrincipal? user) => [];
}

public sealed class IncomingWebhookEffectStatusCollectionLinkPolicy
    : ICollectionLinkPolicy<IncomingWebhookEffectStatusDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        IncomingWebhookEffectStatusDto dto,
        ClaimsPrincipal? user)
    {
        if (!string.Equals(dto.Status, "DeadLettered", StringComparison.Ordinal))
        {
            yield break;
        }

        yield return new LinkDefinition(
            "redrive",
            RouteNames.RedriveIncomingWebhookEffect,
            new { tenantId = dto.TenantId, effectOutboxId = dto.EffectOutboxId },
            "POST",
            "Redrive incoming Coop effect",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.RedriveIncoming,
                ResourceDescriptors.IncomingWebhookEffectStatus,
                dto);
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
