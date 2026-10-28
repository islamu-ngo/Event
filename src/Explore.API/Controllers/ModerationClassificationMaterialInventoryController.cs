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
[Authorize]
[Consumes("application/json")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[RequestSizeLimit(64 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationClassificationMaterialInventoryController(
    IQueryHandler<GetModerationClassificationMaterialInventoryQuery, ModerationClassificationMaterialInventoryReadResult> read,
    IResourceAssembler<ModerationClassificationMaterialInventoryView, ModerationClassificationMaterialInventoryView> assembler,
    IOptions<JsonOptions> json) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("materialInventory", "Invalid material inventory intent", "Supply exact native review coordinates."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Material inventory forbidden", "Current native Event ownership and independent review authority are required.",
            FailureCodes.AdminRequired)
        .Unavailable("Material inventory unavailable", "The original current complete inventory is unavailable.",
            "ModerationMaterialInventoryUnavailable");

    /// <summary>Reads exact metadata under retained native ownership; the bounded body is intent, not a material submission.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("inventory", Name = RouteNames.ReadModerationClassificationMaterialInventory)]
    [ProducesResponseType<HalResource<ModerationClassificationMaterialInventoryView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Read(
        [FromBody] GetModerationClassificationMaterialInventoryQuery query, CancellationToken cancellationToken = default) =>
        new ModerationClassificationMaterialInventoryHttpResult(await read.QueryAsync(query, cancellationToken),
            assembler, json, outcome => Failures.Map(this, ModerationClassificationMaterialInventoryReadResult.Failure(outcome)));
}
