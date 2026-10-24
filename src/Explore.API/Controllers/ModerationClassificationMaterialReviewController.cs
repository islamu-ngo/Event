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
[Route("api/moderation/classification-materials")]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationClassificationMaterialReviewController(
    IQueryHandler<GetModerationClassificationMaterialReviewQuery, ModerationClassificationMaterialReviewReadResult> read,
    IResourceAssembler<ModerationClassificationMaterialReviewView, ModerationClassificationMaterialReviewView> assembler,
    IOptions<JsonOptions> json) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("materialReview", "Invalid material proposal", "Select a nonempty native proposal identity."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Material proposal forbidden", "The current nominated independent reviewer is required.", FailureCodes.AdminRequired)
        .Unavailable("Material proposal unavailable", "The original complete unaccepted proposal is unavailable.",
            "ModerationMaterialReviewUnavailable");

    /// <summary>Reads unaccepted proposal metadata only through the actual nominated reviewer and current complete native inventory.</summary>
    [AllowAnonymous]
    [HttpGet("sources/{sourceId:guid}/review", Name = RouteNames.GetModerationClassificationMaterialReview)]
    [ProducesResponseType<HalResource<ModerationClassificationMaterialReviewView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Read(Guid sourceId, CancellationToken cancellationToken = default) =>
        new ModerationClassificationMaterialReviewHttpResult(await read.QueryAsync(new(sourceId), cancellationToken),
            assembler, json, outcome => Failures.Map(this, ModerationClassificationMaterialReviewReadResult.Failure(outcome)));
}
