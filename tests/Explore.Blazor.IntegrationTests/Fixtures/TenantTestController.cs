using Explore.Blazor.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.IntegrationTests.Fixtures;

[ApiController]
[Route("test")]
public class TenantTestController : ControllerBase
{
    private readonly ITenantRouteContextAccessor _accessor;

    public TenantTestController(ITenantRouteContextAccessor accessor)
    {
        _accessor = accessor;
    }

    [HttpGet("tenant-info")]
    [AllowAnonymous]
    public IActionResult GetTenantInfo()
    {
        return Ok(new
        {
            slug = _accessor.TenantSlug,
            path = HttpContext.Request.Path.Value,
            pathBase = HttpContext.Request.PathBase.Value
        });
    }
}
