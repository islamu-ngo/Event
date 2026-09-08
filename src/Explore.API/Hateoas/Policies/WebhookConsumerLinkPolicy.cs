using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class WebhookConsumerDetailLinkPolicy : ILinkPolicy<WebhookConsumerDto>
{
    public IEnumerable<LinkDefinition> GetLinks(WebhookConsumerDto dto, ClaimsPrincipal? user)
    {
        yield return new LinkDefinition(
            LinkRelations.Self,
            RouteNames.GetWebhookConsumerById,
            new { consumerId = dto.Id },
            "GET",
            "Webhook consumer")
            .RequirePermission(AuthorizationActions.Webhooks.View, ResourceDescriptors.WebhookConsumer, dto);

        if (dto.StatusId != (int)Explore.Domain.WebhookConsumerStatus.Archived)
        {
            yield return new LinkDefinition(
                LinkRelations.ChangeProviderMode,
                RouteNames.UpdateWebhookConsumerProviderMode,
                new { consumerId = dto.Id },
                "PATCH",
                "Change provider mode",
                RequiresAuth: true)
                .RequirePermission(AuthorizationActions.Webhooks.Update,
                    ResourceDescriptors.WebhookConsumer,
                    dto);
        }
    }

    public static LinkDefinition CreateProviderPortalLink(WebhookConsumerDto dto) =>
        new LinkDefinition(
            LinkRelations.OpenProviderPortal,
            RouteNames.OpenSvixAppPortal,
            null,
            "POST",
            "Open provider portal",
            RequiresAuth: true)
        .RequirePermission(AuthorizationActions.Webhooks.OpenProviderPortal,
            ResourceDescriptors.WebhookConsumer,
            dto);

    public static LinkDefinition CreateProviderBindingRepairLink(WebhookConsumerDto dto) =>
        new LinkDefinition(
            LinkRelations.RepairProviderBinding,
            RouteNames.RepairWebhookProviderBinding,
            new { consumerId = dto.Id },
            "POST",
            "Repair provider binding",
            RequiresAuth: true)
        .RequirePermission(AuthorizationActions.Webhooks.ManageProvider,
            ResourceDescriptors.WebhookConsumer,
            dto);
}

public sealed class WebhookConsumerCollectionLinkPolicy(ILinkPolicy<WebhookConsumerDto> detailPolicy)
    : ICollectionLinkPolicy<WebhookConsumerDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(WebhookConsumerDto dto, ClaimsPrincipal? user) =>
        detailPolicy.GetLinks(dto, user);

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
            RouteNames.CreateWebhookConsumer,
            null,
            "POST",
            "Create webhook consumer",
            RequiresAuth: true)
            .RequirePermission(AuthorizationActions.Webhooks.Create,
                ResourceKinds.Webhook,
                authorizationContext.AuthorizationResourceId,
                facts: authorizationContext.AuthorizationFacts);
    }
}
