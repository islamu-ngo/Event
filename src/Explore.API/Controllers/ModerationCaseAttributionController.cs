using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/case-attributions")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(16 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationCaseAttributionController(
    ICommandHandler<AttributeModerationCaseCommand, BaseCommandResponse<Guid>> attribute,
    ICommandHandler<RenewModerationCaseAttributionCommand, BaseCommandResponse<Guid>> renew,
    ICommandHandler<WithdrawModerationCaseAttributionCommand, BaseCommandResponse<Guid>> withdraw) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid attribution request", "The attribution request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Attribution forbidden", "Current native human authority is required.", FailureCodes.AdminRequired)
        .Unavailable("Attribution unavailable", "Current native attribution authority is unavailable.",
            "ModerationCaseAttributionUnavailable");

    /// <summary>Qualifies responsibility under review without creating guilt or a restriction.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.AttributeModerationCase)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Attribute(
        [FromBody] AttributeModerationCaseCommand command, CancellationToken cancellationToken)
    {
        var response = await attribute.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Appends a freshly qualified generation to the same proceeding with an exact expected head.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{caseId:guid}/renew", Name = RouteNames.RenewModerationCaseAttribution)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Renew(Guid caseId,
        [FromBody] RenewModerationCaseAttributionCommand command, CancellationToken cancellationToken)
    {
        var response = await renew.ExecuteAsync(command with { CaseId = caseId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Withdraws one exact source without deleting the original attribution or changing its findings.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("sources/{sourceId:guid}/withdraw", Name = RouteNames.WithdrawModerationCaseAttribution)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Withdraw(Guid sourceId,
        [FromBody] WithdrawModerationCaseAttributionCommand command, CancellationToken cancellationToken)
    {
        var response = await withdraw.ExecuteAsync(command with { SourceId = sourceId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
