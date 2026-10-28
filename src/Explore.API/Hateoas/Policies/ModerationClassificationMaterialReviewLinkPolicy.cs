using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ModerationClassificationMaterialReviewLinkPolicy : ILinkPolicy<ModerationClassificationMaterialReviewView>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationClassificationMaterialReviewView dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetModerationClassificationMaterialReview, new { sourceId = dto.SourceId });
        yield return LinkDefinition.Action("refresh", RouteNames.GetModerationClassificationMaterialReview,
            HttpMethods.Get, new { sourceId = dto.SourceId });
        if (!dto.NativeReviewerQualified) yield break;
        yield return LinkDefinition.Action("accept-material-source", RouteNames.AcceptModerationClassificationMaterial, HttpMethods.Post);
    }
}

public sealed class ModerationClassificationMaterialReviewCollectionLinkPolicy : ICollectionLinkPolicy<ModerationClassificationMaterialReviewView>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationClassificationMaterialReviewView dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
