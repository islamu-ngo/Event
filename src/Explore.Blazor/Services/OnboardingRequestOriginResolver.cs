using Explore.Blazor.Client.Models;
using Microsoft.AspNetCore.Http.Extensions;

namespace Explore.Blazor.Services;

public static class OnboardingRequestOriginResolver
{
    public static OnboardingRequestOrigin Resolve(HttpContext? context)
    {
        var request = context?.Request;
        // Read only the effective request; never interpret raw forwarding headers or listening addresses.
        if (request is null || !request.Host.HasValue
            || request.Scheme is not ("http" or "https"))
            return new(null);

        return new(UriHelper.BuildAbsolute(request.Scheme, request.Host, request.PathBase).TrimEnd('/'));
    }
}
