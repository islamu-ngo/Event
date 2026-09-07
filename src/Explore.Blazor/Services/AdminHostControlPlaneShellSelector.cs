using Event.Web.BffHosting.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Explore.Blazor.Services;

public sealed class AdminHostControlPlaneShellSelector(IEventBffHostClassifier hostClassifier)
{
    public bool ShouldUseControlPlaneShell(HttpContext? httpContext)
    {
        return httpContext is not null && hostClassifier.IsAdminHost(httpContext);
    }
}
