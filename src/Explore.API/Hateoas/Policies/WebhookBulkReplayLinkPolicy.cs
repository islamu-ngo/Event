using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class WebhookBulkReplayDetailLinkPolicy : ILinkPolicy<WebhookBulkReplayOperationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(
        WebhookBulkReplayOperationDto dto,
        ClaimsPrincipal? user)
    {
        yield return Self(dto);
        yield return Collection();
        if (string.Equals(dto.StatusCode, "QUEUED", StringComparison.Ordinal))
        {
            yield return Cancel(dto);
        }
    }

    internal static LinkDefinition Self(WebhookBulkReplayOperationDto dto) =>
        new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookBulkReplayById,
            new { operationId = dto.Id },
            "GET",
            "Webhook bulk replay")
            .RequirePermission(AuthorizationActions.Webhooks.BulkReplay,
                ResourceDescriptors.WebhookBulkReplayOperation,
                dto);

    internal static LinkDefinition Collection() =>
        new LinkDefinition(
            LinkRelations.Collection,
            RouteNames.GetWebhookBulkReplays,
            null,
            "GET",
            "Webhook bulk replays",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.BulkReplay, ResourceKinds.Webhook);

    internal static LinkDefinition Cancel(WebhookBulkReplayOperationDto dto) =>
        new LinkDefinition(
            LinkRelations.Cancel,
            RouteNames.CancelWebhookBulkReplay,
            new { operationId = dto.Id },
            "POST",
            "Cancel webhook bulk replay")
            .RequirePermission(AuthorizationActions.Webhooks.BulkReplay,
                ResourceDescriptors.WebhookBulkReplayOperation,
                dto);
}

public sealed class WebhookBulkReplayCollectionLinkPolicy
    : ICollectionLinkPolicy<WebhookBulkReplayOperationDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(
        WebhookBulkReplayOperationDto dto,
        ClaimsPrincipal? user)
    {
        yield return WebhookBulkReplayDetailLinkPolicy.Self(dto);
        if (string.Equals(dto.StatusCode, "QUEUED", StringComparison.Ordinal))
        {
            yield return WebhookBulkReplayDetailLinkPolicy.Cancel(dto);
        }
    }

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user)
    {
        yield return WebhookBulkReplayDetailLinkPolicy.Collection() with { Rel = LinkRelations.Self };
        yield return new LinkDefinition(
            LinkRelations.BulkReplayPreview,
            RouteNames.PreviewWebhookBulkReplay,
            null,
            "GET",
            "Preview webhook bulk replay",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.BulkReplay, ResourceKinds.Webhook);
        yield return new LinkDefinition(
            LinkRelations.BulkReplays,
            RouteNames.ScheduleWebhookBulkReplay,
            null,
            "POST",
            "Schedule webhook bulk replay",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.BulkReplay, ResourceKinds.Webhook);
    }
}
