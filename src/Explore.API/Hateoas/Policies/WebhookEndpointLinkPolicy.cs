using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class WebhookEndpointDetailLinkPolicy : ILinkPolicy<WebhookEndpointDto>
{
    public IEnumerable<LinkDefinition> GetLinks(WebhookEndpointDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookEndpointById,
            new { endpointId = dto.Id },
            "GET",
            "Webhook endpoint")
            .RequirePermission(AuthorizationActions.Webhooks.View, ResourceDescriptors.WebhookEndpoint, dto);

        if (!string.Equals(dto.StatusCode, "ARCHIVED", StringComparison.Ordinal))
        {
            yield return new LinkDefinition(
                LinkRelations.Update,
                RouteNames.UpdateWebhookEndpoint,
                new { endpointId = dto.Id },
                "PATCH",
                "Update webhook endpoint")
                .RequirePermission(AuthorizationActions.Webhooks.Update, ResourceDescriptors.WebhookEndpoint, dto);

            yield return new LinkDefinition(
                LinkRelations.RotateSecret,
                RouteNames.RotateWebhookEndpointSecret,
                new { endpointId = dto.Id },
                "POST",
                "Rotate webhook endpoint secret")
                .RequirePermission(AuthorizationActions.Webhooks.RotateSecret, ResourceDescriptors.WebhookEndpoint, dto);

            if (string.Equals(dto.StatusCode, "ACTIVE", StringComparison.Ordinal) &&
                SupportsLocalDelivery(dto.ProviderModeCode))
            {
                yield return new LinkDefinition(
                    LinkRelations.Test,
                    RouteNames.TestWebhookEndpoint,
                    new { endpointId = dto.Id },
                    "POST",
                    "Test webhook endpoint")
                    .RequirePermission(AuthorizationActions.Webhooks.Test, ResourceDescriptors.WebhookEndpoint, dto);
            }

            if (string.Equals(dto.StatusCode, "ACTIVE", StringComparison.Ordinal) &&
                SupportsLocalDelivery(dto.ProviderModeCode))
            {
                yield return new LinkDefinition(
                    LinkRelations.Pause,
                    RouteNames.PauseWebhookEndpoint,
                    new { endpointId = dto.Id },
                    "POST",
                    "Pause webhook endpoint")
                    .RequirePermission(AuthorizationActions.Webhooks.Pause, ResourceDescriptors.WebhookEndpoint, dto);
            }

            if ((string.Equals(dto.StatusCode, "AUTO_PAUSED", StringComparison.Ordinal) ||
                    string.Equals(dto.StatusCode, "DISABLED", StringComparison.Ordinal)) &&
                SupportsLocalDelivery(dto.ProviderModeCode))
            {
                yield return new LinkDefinition(
                    LinkRelations.Resume,
                    RouteNames.ResumeWebhookEndpoint,
                    new { endpointId = dto.Id },
                    "POST",
                    "Resume webhook endpoint")
                    .RequirePermission(AuthorizationActions.Webhooks.Resume, ResourceDescriptors.WebhookEndpoint, dto);
            }

            yield return new LinkDefinition(
                LinkRelations.Delete,
                RouteNames.DeleteWebhookEndpoint,
                new { endpointId = dto.Id },
                "DELETE",
                "Delete webhook endpoint")
                .RequirePermission(AuthorizationActions.Webhooks.Delete, ResourceDescriptors.WebhookEndpoint, dto);
        }
    }

    private static bool SupportsLocalDelivery(string providerModeCode) =>
        string.Equals(providerModeCode, "LOCAL", StringComparison.Ordinal) ||
        string.Equals(providerModeCode, "COMPOSITE", StringComparison.Ordinal);
}

public sealed class WebhookEndpointCollectionLinkPolicy : ICollectionLinkPolicy<WebhookEndpointDto>
{
    private readonly WebhookEndpointDetailLinkPolicy _detailPolicy = new();

    public IEnumerable<LinkDefinition> GetItemLinks(WebhookEndpointDto dto, ClaimsPrincipal? user) =>
        _detailPolicy.GetLinks(dto, user);

    public IEnumerable<LinkDefinition> GetCollectionLinks(
        ClaimsPrincipal? user,
        ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is null)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.Create,
            RouteNames.CreateWebhookEndpoint,
            null,
            "POST",
            "Create webhook endpoint",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.Create,
                ResourceKinds.Webhook,
                authorizationContext.AuthorizationResourceId,
                facts: authorizationContext.AuthorizationFacts);
    }
}
