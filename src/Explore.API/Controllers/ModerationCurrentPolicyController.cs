using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/policies")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationCurrentPolicyController(
    IQueryHandler<GetModerationCurrentPolicyQuery, ModerationCurrentPolicyReadResult> read,
    IResourceAssembler<ModerationCurrentPolicyView, ModerationCurrentPolicyView> assembler,
    IOptions<JsonOptions> json) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("policyRead", "Invalid policy read",
            "Select tenant or instance scope and a nonempty UUIDv7 prospective policy identity when supplied."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Policy read forbidden", "Current native policy authoring authority is required.", FailureCodes.AdminRequired)
        .Unavailable("Policy read unavailable", "The installed native policy read is unavailable.", "ModerationCurrentPolicyUnavailable");

    /// <summary>Reads latest adopted authoring state only for the actual request tenant or installed instance.</summary>
    [AllowAnonymous]
    [HttpGet("current", Name = RouteNames.GetModerationCurrentPolicy)]
    [ProducesResponseType<HalResource<ModerationCurrentPolicyView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Read([FromQuery] string scope = "tenant",
        [FromQuery] Guid? prospectivePolicyId = null, CancellationToken cancellationToken = default) =>
        new ModerationCurrentPolicyHttpResult(await read.QueryAsync(new(scope, prospectivePolicyId), cancellationToken),
            assembler, json, outcome => Failures.Map(this, ModerationCurrentPolicyReadResult.Failure(outcome)));
}
