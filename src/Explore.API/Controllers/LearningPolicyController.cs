using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Application.Features.Learning.Requests.Queries;
using Explore.Application.Hateoas;
using ISLAMU.Wire.Contracts.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api/tenant/settings/documents/learning-guidance")]
[Tags("Learning")]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[OutputCache(NoStore = true)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
public sealed class LearningPolicyController(
    IQueryHandler<GetLearningGuidancePolicyQuery, LearningGuidancePolicyDto> query,
    ICommandHandler<UpdateLearningGuidancePolicyCommand, LearningGuidancePolicyDto> command,
    IResourceAssembler<LearningGuidancePolicyDto, LearningGuidancePolicyDto> assembler) : ControllerBase
{
    [HttpGet(Name = RouteNames.GetLearningGuidancePolicy)]
    [AllowAnonymous]
    [EndpointSummary("Read private tenant learning guidance")]
    [ProducesResponseType(typeof(HalResource<LearningGuidancePolicyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningGuidancePolicyDto>>> Get(CancellationToken cancellationToken)
    {
        var policy = await query.QueryAsync(new GetLearningGuidancePolicyQuery(), cancellationToken);
        return Ok(await assembler.ToResource(policy, HttpContext));
    }

    [HttpPatch(Name = RouteNames.UpdateLearningGuidancePolicy)]
    [Authorize]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Replace tenant learning guidance using the observed document stamp")]
    [ProducesResponseType(typeof(HalResource<LearningGuidancePolicyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningGuidancePolicyDto>>> Patch(
        [FromBody] UpdateLearningGuidancePolicyRequest input, CancellationToken cancellationToken)
    {
        var policy = await command.ExecuteAsync(new UpdateLearningGuidancePolicyCommand(input), cancellationToken);
        return Ok(await assembler.ToResource(policy, HttpContext));
    }
}
