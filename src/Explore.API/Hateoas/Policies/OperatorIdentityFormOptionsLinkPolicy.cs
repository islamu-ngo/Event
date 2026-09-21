using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class OperatorIdentityFormOptionsLinkPolicy : ILinkPolicy<OperatorIdentityFormOptionsDto>
{
    public IEnumerable<LinkDefinition> GetLinks(OperatorIdentityFormOptionsDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetOperatorIdentityFormOptions).Authenticated();
        yield return LinkDefinition.Action("refresh", RouteNames.GetOperatorIdentityFormOptions, HttpMethods.Get);
    }
}

public sealed class OperatorIdentityFormOptionsCollectionLinkPolicy : ICollectionLinkPolicy<OperatorIdentityFormOptionsDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(OperatorIdentityFormOptionsDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
