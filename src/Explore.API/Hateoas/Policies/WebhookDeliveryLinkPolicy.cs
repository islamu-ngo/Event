using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Hateoas;
using Explore.Domain;

namespace Explore.API.Hateoas.Policies;

public sealed class WebhookMessageDetailLinkPolicy(TimeProvider timeProvider) : ILinkPolicy<WebhookMessageDto>
{
    public IEnumerable<LinkDefinition> GetLinks(WebhookMessageDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookMessageById,
            new { messageId = dto.Id },
            "GET",
            "Webhook message")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceDescriptors.WebhookMessage, dto);

        yield return new LinkDefinition(
            LinkRelations.DeliveryAttempts,
            RouteNames.GetWebhookDeliveryAttempts,
            new { messageId = dto.Id },
            "GET",
            "Webhook delivery attempts")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceDescriptors.WebhookMessage, dto);

        yield return new LinkDefinition(
            LinkRelations.ProviderPublications,
            RouteNames.GetWebhookProviderPublications,
            new { messageId = dto.Id },
            "GET",
            "Provider publications")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceDescriptors.WebhookMessage, dto);

        if (dto.PayloadClearedAt is null && dto.PayloadRetentionUntil > timeProvider.GetUtcNow().UtcDateTime)
        {
            yield return new LinkDefinition(
                LinkRelations.Payload,
                RouteNames.GetWebhookMessagePayload,
                new { messageId = dto.Id },
                "GET",
                "Webhook payload")
                .RequirePermission(AuthorizationActions.Webhooks.ViewPayload, ResourceDescriptors.WebhookMessage, dto);
        }
    }
}

public sealed class WebhookMessageCollectionLinkPolicy(TimeProvider timeProvider)
    : ICollectionLinkPolicy<WebhookMessageDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(WebhookMessageDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookMessageById,
            new { messageId = dto.Id },
            "GET",
            "Webhook message")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceDescriptors.WebhookMessage, dto);

        yield return new LinkDefinition(
            LinkRelations.DeliveryAttempts,
            RouteNames.GetWebhookDeliveryAttempts,
            new { messageId = dto.Id },
            "GET",
            "Webhook delivery attempts")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceDescriptors.WebhookMessage, dto);

        yield return new LinkDefinition(
            LinkRelations.ProviderPublications,
            RouteNames.GetWebhookProviderPublications,
            new { messageId = dto.Id },
            "GET",
            "Provider publications")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceDescriptors.WebhookMessage, dto);

        if (dto.PayloadClearedAt is null && dto.PayloadRetentionUntil > timeProvider.GetUtcNow().UtcDateTime)
        {
            yield return new LinkDefinition(
                LinkRelations.Payload,
                RouteNames.GetWebhookMessagePayload,
                new { messageId = dto.Id },
                "GET",
                "Webhook payload")
                .RequirePermission(AuthorizationActions.Webhooks.ViewPayload, ResourceDescriptors.WebhookMessage, dto);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(
        ClaimsPrincipal? user,
        ICollectionAuthorizationContext? authorizationContext)
    {
        if (authorizationContext is null)
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookMessages,
            null,
            "GET",
            "Webhook messages",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery,
                ResourceKinds.Webhook,
                authorizationContext.AuthorizationResourceId,
                facts: authorizationContext.AuthorizationFacts);

        if (!HasTenantOwner(authorizationContext.AuthorizationFacts))
        {
            yield break;
        }

        yield return new LinkDefinition(
            LinkRelations.ProviderPublications,
            RouteNames.GetWebhookProviderPublications,
            null,
            "GET",
            "Provider publications",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery,
                ResourceKinds.Webhook,
                authorizationContext.AuthorizationResourceId,
                facts: authorizationContext.AuthorizationFacts);

        yield return new LinkDefinition(
            LinkRelations.BulkReplayPreview,
            RouteNames.PreviewWebhookBulkReplay,
            null,
            "GET",
            "Preview webhook bulk replay",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.BulkReplay,
                ResourceKinds.Webhook,
                authorizationContext.AuthorizationResourceId,
                facts: authorizationContext.AuthorizationFacts);

        yield return new LinkDefinition(
            LinkRelations.BulkReplays,
            RouteNames.GetWebhookBulkReplays,
            null,
            "GET",
            "Webhook bulk replays",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.BulkReplay,
                ResourceKinds.Webhook,
                authorizationContext.AuthorizationResourceId,
                facts: authorizationContext.AuthorizationFacts);
    }

    // Provider publications and bulk replays are tenant-owned surfaces; delegated owners never see them.
    private static bool HasTenantOwner(IAuthorizationFacts? facts) =>
        facts is WebhookOwnershipAuthorizationFacts ownership
        && ownership.Kind == WebhookConsumerKind.Tenant;
}

public sealed class WebhookDeliveryAttemptDetailLinkPolicy : ILinkPolicy<WebhookDeliveryAttemptDto>
{
    public IEnumerable<LinkDefinition> GetLinks(WebhookDeliveryAttemptDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookDeliveryAttemptById,
            new { attemptId = dto.Id },
            "GET",
            "Webhook delivery attempt")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceDescriptors.WebhookDeliveryAttempt, dto);

        yield return new LinkDefinition(
            LinkRelations.Related,
            RouteNames.GetWebhookMessageById,
            new { messageId = dto.MessageId },
            "GET",
            "Webhook message")
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery, ResourceDescriptors.WebhookDeliveryAttempt, dto);

        if ((string.Equals(dto.OutcomeCode, "FAILED", StringComparison.Ordinal) ||
                string.Equals(dto.OutcomeCode, "ABANDONED", StringComparison.Ordinal))
            && string.Equals(dto.EndpointStatusName, "Active", StringComparison.Ordinal))
        {
            yield return new LinkDefinition(
                LinkRelations.Retry,
                RouteNames.RetryWebhookDeliveryAttempt,
                new { attemptId = dto.Id },
                "POST",
                "Retry webhook delivery attempt")
                .RequirePermission(AuthorizationActions.Webhooks.Retry, ResourceDescriptors.WebhookDeliveryAttempt, dto);
        }
    }
}

public sealed class WebhookDeliveryAttemptCollectionLinkPolicy : ICollectionLinkPolicy<WebhookDeliveryAttemptDto>
{
    private readonly WebhookDeliveryAttemptDetailLinkPolicy _detailPolicy = new();

    public IEnumerable<LinkDefinition> GetItemLinks(WebhookDeliveryAttemptDto dto, ClaimsPrincipal? user) =>
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
            LinkRelations.Self,
            RouteNames.GetWebhookDeliveryAttempts,
            null,
            "GET",
            "Webhook delivery attempts",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.ViewDelivery,
                ResourceKinds.Webhook,
                authorizationContext.AuthorizationResourceId,
                facts: authorizationContext.AuthorizationFacts);
    }
}
