using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/content-responses/{responseId:guid}/statement")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[Produces("text/plain")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationContentResponseStatementsController(
    IQueryHandler<GetModerationContentResponseStatementQuery, ModerationContentResponseStatementResult> statement) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid response", "The protected response identifier is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Response unavailable", "The protected original response is unavailable."), FailureCodes.NotFound)
        .Unavailable("Response unavailable", "Current protected reviewer custody is unavailable.", "ModerationContentResponseUnavailable");

    /// <summary>Reads finite response text only through current independent case-reviewer qualification.</summary>
    [AllowAnonymous]
    [HttpGet(Name = RouteNames.GetModerationContentResponseStatement)]
    [ProducesResponseType<string>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Statement(Guid responseId, CancellationToken cancellationToken) =>
        new ModerationContentResponseStatementHttpResult(
            await statement.QueryAsync(new(responseId), cancellationToken),
            outcome => Failures.Map(this, ModerationContentResponseAvailabilityResult.Failure(outcome)));
}
