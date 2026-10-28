using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
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
[Route("api/moderation/cases/{caseId:guid}/content-responses")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationContentResponseIndexController(
    IQueryHandler<GetModerationContentResponsesQuery, ModerationContentResponseIndexResult> responses,
    IResourceAssembler<ModerationContentResponseSummary, ModerationContentResponseSummary> assembler,
    IOptions<JsonOptions> json, LinkGenerator routes) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid response page", "The protected response page coordinates are invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Responses unavailable", "The qualified original response page is unavailable."), FailureCodes.NotFound)
        .Unavailable("Responses unavailable", "Current protected reviewer custody is unavailable.", "ModerationContentResponseUnavailable");

    /// <summary>Lists bounded response metadata without decrypting statements or disclosing representative identity.</summary>
    [AllowAnonymous]
    [HttpGet(Name = RouteNames.GetModerationContentResponses)]
    [ProducesResponseType<HalCollectionResource<ModerationContentResponseSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(Guid caseId, [FromQuery] Guid? afterId = null, [FromQuery] int limit = 32,
        CancellationToken cancellationToken = default) =>
        new ModerationContentResponseIndexHttpResult(
            await responses.QueryAsync(new(caseId, afterId, limit), cancellationToken),
            assembler, json, routes, caseId, afterId, limit,
            outcome => Failures.Map(this, ModerationContentResponseAvailabilityResult.Failure(outcome)));
}
