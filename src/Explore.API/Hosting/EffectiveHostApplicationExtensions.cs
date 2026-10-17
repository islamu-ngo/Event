using Explore.API.Middleware;
using Explore.ServiceDefaults;

namespace Explore.API.Hosting;

public static class EffectiveHostApplicationExtensions
{
    /// <summary>Run once at the outer listener boundary, before any host/scheme/peer consumer.</summary>
    public static IApplicationBuilder UseEffectiveHostAuthority(this IApplicationBuilder app)
    {
        app.UseForwardedHeaders();
        app.UseEarlyRequestAdmission();
        return app.UseMiddleware<EffectiveHostAuthorityMiddleware>();
    }
}
