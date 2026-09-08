using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.Controllers;

[ApiController]
[Route("robots.txt")]
public sealed class RobotsController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [Produces("text/plain")]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public ContentResult Get()
    {
        string content = environment.IsProduction()
            ? $"User-agent: *\nAllow: /\nSitemap: {BuildBaseUrl()}/sitemap.xml\n"
            : "User-agent: *\nDisallow: /\n";

        return Content(content, "text/plain; charset=utf-8");
    }

    private string BuildBaseUrl()
    {
        var request = Request;
        string? forwardedProto = request.Headers.TryGetValue("X-Forwarded-Proto", out var proto)
            ? proto.ToString().Split(',')[0].Trim()
            : null;

        string scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto
            : request.Scheme;
        string resolvedHost = request.Host.Value ?? string.Empty;

        return $"{scheme}://{resolvedHost}{request.PathBase}".TrimEnd('/');
    }
}
