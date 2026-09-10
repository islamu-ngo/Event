using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Explore.Blazor.IntegrationTests.Fixtures;

/// <summary>
/// Supplies protected /api/v1 metadata absent from the anonymous proxy catch-all.
/// The action is read-only so production authentication and antiforgery remain intact.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/logging-boundary")]
public sealed class BffRequestLoggingBoundaryController : ControllerBase
{
    [HttpGet("{**tail}")]
    public IActionResult Read() => Ok(new
    {
        method = Request.Method,
        path = Request.Path.Value,
        authenticated = User.Identity?.IsAuthenticated == true,
        requiresAuthorization = HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null
    });
}
