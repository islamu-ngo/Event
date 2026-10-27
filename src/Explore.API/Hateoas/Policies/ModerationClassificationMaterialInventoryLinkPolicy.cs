using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ModerationClassificationMaterialInventoryLinkPolicy : ILinkPolicy<ModerationClassificationMaterialInventoryView>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationClassificationMaterialInventoryView dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Action("refresh", RouteNames.ReadModerationClassificationMaterialInventory, HttpMethods.Post);
        if (!dto.NativeOwnerQualified) yield break;
        yield return LinkDefinition.Action("record-material-source", RouteNames.RecordModerationClassificationMaterial, HttpMethods.Post);
    }
}

public sealed class ModerationClassificationMaterialInventoryCollectionLinkPolicy : ICollectionLinkPolicy<ModerationClassificationMaterialInventoryView>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationClassificationMaterialInventoryView dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
