using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Narratives;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/cases")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(192 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationCaseIntakeController(
    ICommandHandler<IssueModerationSubmissionReceiptCommand, ModerationSubmissionReceiptResponse> issueReceipt,
    ICommandHandler<IssueModerationReadReceiptCommand, ModerationReadReceiptResponse> issueReadReceipt,
    ICommandHandler<SubmitModerationAllegationCommand, ModerationAllegationResponse> submitAllegation,
    IQueryHandler<ReadModerationCaseNarrativeQuery, ModerationCaseNarrativeReadResult> readNarrative) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid moderation intake request", "The moderation intake request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Moderation intake forbidden", "Current account authority is required.", FailureCodes.AdminRequired)
        .Conflict("Moderation intake conflict", "The original allegation intent does not match.",
            FailureCodes.ConcurrencyConflict)
        .Unavailable("Moderation intake unavailable", "Current native moderation intake authority is unavailable.",
            "ModerationCaseIntakeUnavailable");

    /// <summary>Issues a committed account-bound submit token without disclosing the protected target.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("submission-receipts", Name = RouteNames.IssueModerationSubmissionReceipt)]
    [ProducesResponseType<ModerationSubmissionReceiptResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationSubmissionReceiptResponse>> IssueSubmissionReceipt(
        [FromBody] IssueModerationSubmissionReceiptCommand command, CancellationToken cancellationToken)
    {
        var response = await issueReceipt.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Issues a committed read token only for a separately granted current case/material relationship.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("read-receipts", Name = RouteNames.IssueModerationReadReceipt)]
    [ProducesResponseType<ModerationReadReceiptResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationReadReceiptResponse>> IssueReadReceipt(
        [FromBody] IssueModerationReadReceiptCommand command, CancellationToken cancellationToken)
    {
        var response = await issueReadReceipt.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Commits an exact receipt-bound allegation and returns only its original acceptance.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("allegations", Name = RouteNames.SubmitModerationAllegation)]
    [ProducesResponseType<ModerationAllegationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationAllegationResponse>> SubmitAllegation(
        [FromBody] SubmitModerationAllegationCommand command, CancellationToken cancellationToken)
    {
        var response = await submitAllegation.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Reads confidential material using a separately issued read token, never a submission receipt.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{caseId:guid}/narrative", Name = RouteNames.ReadModerationCaseNarrative)]
    [Produces("application/octet-stream")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ReadNarrative(Guid caseId,
        [FromBody] ReadModerationCaseNarrativeQuery query, CancellationToken cancellationToken)
    {
        var pending = await readNarrative.QueryAsync(query with { CaseId = caseId }, cancellationToken);
        if (!pending.IsValid)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["request"] = pending.ValidationErrors.ToArray()
            }));
        return new ModerationCaseNarrativeResult(pending);
    }
}
