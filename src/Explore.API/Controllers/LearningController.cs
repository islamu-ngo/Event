using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Learning;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Application.Features.Learning.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using ISLAMU.Wire.Contracts.Learning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api/learning")]
[Tags("Learning")]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[OutputCache(NoStore = true)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
public sealed class LearningController(
    IQueryHandler<GetLearningContextQuery, LearningContextDto> contextQuery,
    IQueryHandler<GetLearningPracticeQuery, LearningPracticeDto> practiceQuery,
    ICommandHandler<ClaimLearningTourCommand, LearningMutationResult> claimCommand,
    ICommandHandler<RecordLearningTourOutcomeCommand, LearningMutationResult> outcomeCommand,
    ICommandHandler<UpdateLearningPreferencesCommand, LearningMutationResult> preferencesCommand,
    ICommandHandler<ApplyLearningPracticeActionCommand, LearningMutationResult> practiceCommand,
    ICommandHandler<ManageTenantLearningSpaceCommand, LearningMutationResult> spaceCommand,
    IResourceAssembler<LearningContextDto, LearningContextDto> contextAssembler,
    IResourceAssembler<LearningPracticeDto, LearningPracticeDto> practiceAssembler,
    IResourceAssembler<LearningMutationReceiptDto, LearningMutationReceiptDto> receiptAssembler) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("learning", "Invalid learning request", "The learning request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Not Found", "Learning resource not found."), FailureCodes.NotFound)
        .Conflict("Learning changed", "Refresh the private learning context before trying again.",
            FailureCodes.ConcurrencyConflict);

    [HttpGet("context", Name = RouteNames.GetLearningContext)]
    [AllowAnonymous]
    [EndpointSummary("Get private learning context")]
    [ProducesResponseType(typeof(HalResource<LearningContextDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningContextDto>>> GetContext(
        [FromQuery] string contextKey = "self",
        [FromQuery] Guid? eventContextId = null,
        CancellationToken cancellationToken = default)
    {
        LearningContextDto context = await contextQuery.QueryAsync(
            new GetLearningContextQuery(contextKey, eventContextId), cancellationToken);
        return Ok(await contextAssembler.ToResource(context, HttpContext));
    }

    [HttpGet("practice/{id:guid}", Name = RouteNames.GetLearningPractice)]
    [AllowAnonymous]
    [EndpointSummary("Get private learning practice")]
    [ProducesResponseType(typeof(HalResource<LearningPracticeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningPracticeDto>>> GetPractice(
        Guid id, CancellationToken cancellationToken = default)
    {
        LearningPracticeDto practice = await practiceQuery.QueryAsync(
            new GetLearningPracticeQuery(id), cancellationToken);
        return Ok(await practiceAssembler.ToResource(practice, HttpContext));
    }

    [HttpPost("tours/claim", Name = RouteNames.ClaimLearningTour)]
    [Authorize]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Claim an available learning tour")]
    [ProducesResponseType(typeof(HalResource<LearningMutationReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningMutationReceiptDto>>> ClaimTour(
        [FromBody] LearningClaimTourRequest input, CancellationToken cancellationToken = default) =>
        await ToReceipt(await claimCommand.ExecuteAsync(new ClaimLearningTourCommand(input), cancellationToken));

    [HttpPost("tours/outcome", Name = RouteNames.RecordLearningTourOutcome)]
    [Authorize]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Record private learning tour progress")]
    [ProducesResponseType(typeof(HalResource<LearningMutationReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningMutationReceiptDto>>> RecordTourOutcome(
        [FromBody] LearningRecordTourOutcomeRequest input, CancellationToken cancellationToken = default) =>
        await ToReceipt(await outcomeCommand.ExecuteAsync(new RecordLearningTourOutcomeCommand(input), cancellationToken));

    [HttpPatch("preferences", Name = RouteNames.UpdateLearningPreferences)]
    [Authorize]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Update private learning preferences")]
    [ProducesResponseType(typeof(HalResource<LearningMutationReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningMutationReceiptDto>>> UpdatePreferences(
        [FromBody] LearningUpdatePreferencesRequest input, CancellationToken cancellationToken = default) =>
        await ToReceipt(await preferencesCommand.ExecuteAsync(new UpdateLearningPreferencesCommand(input), cancellationToken));

    [HttpPost("practice/actions", Name = RouteNames.ApplyLearningPracticeAction)]
    [Authorize]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Apply an authored simulation practice action")]
    [ProducesResponseType(typeof(HalResource<LearningMutationReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningMutationReceiptDto>>> ApplyPracticeAction(
        [FromBody] LearningApplyPracticeActionRequest input, CancellationToken cancellationToken = default) =>
        await ToReceipt(await practiceCommand.ExecuteAsync(new ApplyLearningPracticeActionCommand(input), cancellationToken));

    [HttpPost("space/actions", Name = RouteNames.ManageTenantLearningSpace)]
    [Authorize]
    [SuppressIdempotencyResponseStorage]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestSizeLimit(65536)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [EndpointSummary("Remove or recreate the tenant learning space")]
    [ProducesResponseType(typeof(HalResource<LearningMutationReceiptDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<LearningMutationReceiptDto>>> ManageSpace(
        [FromBody] LearningManageSpaceRequest input, CancellationToken cancellationToken = default) =>
        await ToReceipt(await spaceCommand.ExecuteAsync(new ManageTenantLearningSpaceCommand(input), cancellationToken));

    private async Task<ActionResult<HalResource<LearningMutationReceiptDto>>> ToReceipt(LearningMutationResult result)
    {
        if (!result.Response.IsSuccess)
            return Failures.Map(this, result.Response);

        return Ok(await receiptAssembler.ToResource(result.Receipt
            ?? throw new InvalidOperationException("A successful learning mutation requires its private receipt."),
            HttpContext));
    }
}
