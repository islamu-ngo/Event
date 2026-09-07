using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class WebhookProviderPublicationDetailLinkPolicy
    : ILinkPolicy<WebhookProviderPublicationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        WebhookProviderPublicationDto dto,
        ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookProviderPublicationById,
            new { publicationId = dto.Id },
            "GET",
            "Provider publication")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery,
                ResourceDescriptors.WebhookProviderPublication,
                dto);

        yield return new LinkDefinition(
            LinkRelations.Related,
            RouteNames.GetWebhookMessageById,
            new { messageId = dto.WebhookMessageId },
            "GET",
            "Webhook message")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery,
                ResourceDescriptors.WebhookProviderPublication,
                dto);

        if (!string.Equals(dto.ProviderKindCode, "SVIX", StringComparison.Ordinal))
        {
            yield break;
        }

        if (string.Equals(dto.StatusCode, "MANUAL_RECONCILIATION", StringComparison.Ordinal))
        {
            yield return new LinkDefinition(
                LinkRelations.Reconcile,
                RouteNames.ReconcileWebhookProviderPublication,
                new { publicationId = dto.Id },
                "POST",
                "Reconcile provider publication")
                .RequirePermission(AuthorizationActions.Webhooks.ReconcilePublication,
                    ResourceDescriptors.WebhookProviderPublication,
                    dto);
        }

        if (string.Equals(dto.StatusCode, "MANUAL_RECONCILIATION", StringComparison.Ordinal) ||
            string.Equals(dto.StatusCode, "DEAD_LETTERED", StringComparison.Ordinal))
        {
            yield return new LinkDefinition(
                LinkRelations.Abandon,
                RouteNames.AbandonWebhookProviderPublication,
                new { publicationId = dto.Id },
                "POST",
                "Abandon provider publication")
                .RequirePermission(AuthorizationActions.Webhooks.AbandonPublication,
                    ResourceDescriptors.WebhookProviderPublication,
                    dto);
        }
    }
}

public sealed class WebhookProviderPublicationCollectionLinkPolicy
    : ICollectionLinkPolicy<WebhookProviderPublicationDto>
{
    private readonly WebhookProviderPublicationDetailLinkPolicy _detailPolicy = new();

    public IEnumerable<LinkDefinition> GetItemLinks(
        WebhookProviderPublicationDto dto,
        ClaimsPrincipal? user) => _detailPolicy.GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookProviderPublications,
            null,
            "GET",
            "Provider publications",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceKinds.Webhook);
    }
}
