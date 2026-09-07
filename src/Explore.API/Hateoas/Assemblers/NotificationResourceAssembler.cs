namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Notification;

/// <summary>
/// Resource assembler for Notification entities.
/// Converts NotificationDto and NotificationListDto to HAL resources with appropriate links.
/// </summary>
public sealed class NotificationResourceAssembler : ResourceAssemblerBase<NotificationDto, NotificationListDto>
{
    public NotificationResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<NotificationDto> detailLinkPolicy,
        ICollectionLinkPolicy<NotificationListDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }

    protected override Dictionary<string, object>? GetEmbeddedResources(
        NotificationDto dto,
        Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        return null;
    }
}
