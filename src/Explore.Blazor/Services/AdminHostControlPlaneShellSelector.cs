using Event.Web.BffHosting.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Explore.Blazor.Services;

public sealed class AdminHostControlPlaneShellSelector(IEventBffHostClassifier hostClassifier)
{
    public bool ShouldUseControlPlaneShell(HttpContext? httpContext, PathString? routePath = null)
    {
        if (httpContext is null || !hostClassifier.IsAdminHost(httpContext))
        {
            return false;
        }

        var path = routePath ?? httpContext.Request.Path;
        return !path.StartsWithSegments("/login")
            && !path.StartsWithSegments("/logout")
            && !path.StartsWithSegments("/auth/local/change-password")
            && !path.StartsWithSegments("/auth/local-account-recovery");
    }
}
