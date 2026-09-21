using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
public sealed class SystemController(
    IQueryHandler<GetSystemOnboardingStatusQuery, SystemOnboardingStatusDto> onboardingStatusHandler) : EventControllerBase
{
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("onboarding-status", Name = RouteNames.GetSystemOnboardingStatus)]
    [OutputCache(PolicyName = "SystemConfig")]
    [EndpointSummary("Get System Onboarding Status")]
    [EndpointDescription("Returns non-sensitive startup state: whether onboarding is required and the effective deployment mode.")]
    [ProducesResponseType(typeof(SystemOnboardingStatusDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemOnboardingStatusDto>> GetOnboardingStatus(CancellationToken cancellationToken = default)
    {
        var status = await onboardingStatusHandler.QueryAsync(new GetSystemOnboardingStatusQuery(), cancellationToken);
        return Ok(status);
    }
}
