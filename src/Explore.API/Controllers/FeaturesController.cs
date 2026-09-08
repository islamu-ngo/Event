using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/features")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public class FeaturesController : ControllerBase
{
    private readonly IFeatureFlagService _featureFlagService;

    public FeaturesController(IFeatureFlagService featureFlagService)
    {
        _featureFlagService = featureFlagService;
    }

    [HttpGet("my-flags", Name = RouteNames.GetMyFeatureFlags)]
    public async Task<ActionResult<Dictionary<string, bool>>> GetMyFlags(CancellationToken ct)
    {
        var flags = await _featureFlagService.GetClientFlagsAsync(ct: ct);
        return Ok(flags);
    }
}
