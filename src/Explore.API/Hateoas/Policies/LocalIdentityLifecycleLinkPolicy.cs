
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

internal static class LocalIdentityLifecycleLinkPolicy
{
    internal static IEnumerable<LinkDefinition> GetLinks(LocalIdentityLifecycleCapabilities capabilities)
    {
        if (capabilities.VerifyEmail)
            yield return new LinkDefinition("verify-email", RouteNames.RequestLocalEmailVerification, null,
                HttpMethods.Post, "Verify Local email");
        if (capabilities.RecoverPassword)
            yield return new LinkDefinition("recover-password", RouteNames.RequestLocalPasswordRecovery, null,
                HttpMethods.Post, "Recover Local password");
        if (capabilities.ChangePassword)
            yield return new LinkDefinition("change-password", RouteNames.ChangeLocalPassword, null,
                HttpMethods.Post, "Change Local password", RequiresAuth: true);
    }
}
