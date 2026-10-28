using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ModerationCurrentPolicyLinkPolicy : ILinkPolicy<ModerationCurrentPolicyView>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationCurrentPolicyView dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetModerationCurrentPolicy, new { scope = dto.Scope });
        yield return LinkDefinition.Action("refresh", RouteNames.GetModerationCurrentPolicy,
            HttpMethods.Get, new { scope = dto.Scope });
        yield return LinkDefinition.Related("reference-catalogue", RouteNames.GetModerationReferenceCatalogue);
        if (!dto.NativeAuthoringQualified) yield break;
        yield return LinkDefinition.Action("draft-publication", RouteNames.PublishModerationPolicy,
            HttpMethods.Post, new { policyId = dto.PolicyId });
        yield return LinkDefinition.Action("publication-preparation", RouteNames.PrepareModerationPolicyPublication,
            HttpMethods.Post, new { policyId = dto.PolicyId });
    }
}

public sealed class ModerationCurrentPolicyCollectionLinkPolicy : ICollectionLinkPolicy<ModerationCurrentPolicyView>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationCurrentPolicyView dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
