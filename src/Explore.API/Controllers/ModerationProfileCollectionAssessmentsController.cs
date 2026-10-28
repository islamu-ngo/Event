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
[Route("api/moderation/profile-collection-assessments")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(64 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class ModerationProfileCollectionAssessmentsController(
    ICommandHandler<RecordModerationProfileCollectionAssessmentCommand, BaseCommandResponse<Guid>> record,
    ICommandHandler<AdoptModerationProfileCollectionAssessmentCommand, BaseCommandResponse<Guid>> adopt,
    ICommandHandler<WithdrawModerationProfileCollectionAssessmentCommand, BaseCommandResponse<Guid>> withdraw) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid profile assessment correction", "The assessment correction request is invalid."))
        .NotFound(new("Profile assessment unavailable", "The requested retained assessment is unavailable."), FailureCodes.NotFound);

    /// <summary>Records the nominated owner's complete proposal against the original subject observation, never consent or collection permission.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("sources", Name = RouteNames.RecordModerationProfileCollectionAssessment)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Record(
        [FromBody] RecordModerationProfileCollectionAssessmentCommand command, CancellationToken cancellationToken)
    {
        var response = await record.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Independently accepts the exact latest source without rebinding its original subject or renewing the finite window.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions", Name = RouteNames.AdoptModerationProfileCollectionAssessment)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Adopt(
        [FromBody] AdoptModerationProfileCollectionAssessmentCommand command, CancellationToken cancellationToken)
    {
        var response = await adopt.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Withdraws the exact route-bound retained acceptance without renewing its applicability or collection authority.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions/{adoptionId:guid}/withdraw", Name = RouteNames.WithdrawModerationProfileCollectionAssessment)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Withdraw(Guid adoptionId,
        [FromBody] WithdrawModerationProfileCollectionAssessmentCommand command, CancellationToken cancellationToken)
    {
        var response = await withdraw.ExecuteAsync(command with { AdoptionId = adoptionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
