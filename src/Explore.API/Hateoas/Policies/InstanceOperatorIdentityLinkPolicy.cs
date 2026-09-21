namespace Explore.API.Hateoas.Policies;

using System.Security.Claims;
using Explore.Application.Constants;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Link policy for the instance operator identity document resource.
/// </summary>
public sealed class InstanceOperatorIdentityLinkPolicy : ILinkPolicy<InstanceOperatorIdentityDocumentDto>
{
    public IEnumerable<LinkDefinition> GetLinks(InstanceOperatorIdentityDocumentDto dto, ClaimsPrincipal? user)
    {
        yield return LinkDefinition.Self(RouteNames.GetInstanceOperatorIdentity);

        // This lookup uses normal authentication, not the bootstrap secret scheme.
        if (user?.Identities.Any(identity => identity.IsAuthenticated
            && identity.AuthenticationType != ApiAuthenticationSchemeNames.SetupSecret) == true)
        {
            yield return LinkDefinition.Related("form-options", RouteNames.GetOperatorIdentityFormOptions).Authenticated();
        }

        yield return new LinkDefinition(
            "update",
            RouteNames.SaveInstanceOperatorIdentity,
            Method: HttpMethods.Put,
            Title: "Update instance operator identity");
    }
}

/// <summary>
/// Collection link policy for instance operator identity documents.
/// </summary>
public sealed class InstanceOperatorIdentityCollectionLinkPolicy
    : ICollectionLinkPolicy<InstanceOperatorIdentityDocumentDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(InstanceOperatorIdentityDocumentDto dto, ClaimsPrincipal? user) => [];

    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
