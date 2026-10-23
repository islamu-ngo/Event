using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class ModerationReferenceCatalogueLinkPolicy : ILinkPolicy<ModerationReferenceCatalogueView>
{
    public IEnumerable<LinkDefinition> GetLinks(ModerationReferenceCatalogueView dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetModerationReferenceCatalogue);
        yield return LinkDefinition.Action("refresh", RouteNames.GetModerationReferenceCatalogue,
            HttpMethods.Get, requiresAuth: false);
    }
}

public sealed class ModerationReferenceCatalogueCollectionLinkPolicy
    : ICollectionLinkPolicy<ModerationReferenceCatalogueView>
{
    public IEnumerable<LinkDefinition> GetItemLinks(ModerationReferenceCatalogueView dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
