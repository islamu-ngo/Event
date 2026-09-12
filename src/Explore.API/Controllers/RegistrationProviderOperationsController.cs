using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/tenants/{tenantId:guid}/events/{eventId:guid}/registration-providers")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[Tags("RegistrationProviderManagement")]
public sealed class RegistrationProviderOperationsController(
    IMediator mediator,
    IResourceAssembler<RegistrationProviderBindingHealthDto, RegistrationProviderBindingHealthDto> healthAssembler,
    IResourceAssembler<RegistrationProviderParkedQueueItemDto, RegistrationProviderParkedQueueItemDto> queueAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor ProviderManagementValidationProblem = new(
        "registrationProviderManagement",
        "Registration provider management request failed",
        "The registration provider management request was invalid.");

    [HttpGet("health", Name = RouteNames.GetRegistrationProviderHealth)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationProviderBindingHealthDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<RegistrationProviderBindingHealthDto>>> GetHealth(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RegistrationProviderBindingHealthDto> result = await mediator.Send(new GetRegistrationProviderHealthQuery(tenantId, eventId), cancellationToken);
        return Ok(healthAssembler.ToCollectionResource(result, RouteNames.GetRegistrationProviderHealth, new { eventId, tenantId }, HttpContext));
    }

    [HttpGet("queue", Name = RouteNames.GetRegistrationProviderQueue)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationProviderParkedQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<RegistrationProviderParkedQueueItemDto>>> GetQueue(
        Guid tenantId,
        Guid eventId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RegistrationProviderParkedQueueItemDto> result = await mediator.Send(new GetRegistrationProviderQueueQuery(tenantId, eventId, limit), cancellationToken);
        return Ok(queueAssembler.ToCollectionResource(result, RouteNames.GetRegistrationProviderQueue, new RegistrationProviderEventCollectionContext(tenantId, eventId), HttpContext));
    }

    [HttpPost("{bindingId:guid}/reconcile", Name = RouteNames.PollRegistrationProviderReconciliation)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PollReconciliation(
        Guid tenantId,
        Guid eventId,
        Guid bindingId,
        [FromQuery] DateTime sinceUtc,
        CancellationToken cancellationToken = default) => ToActionResult(await mediator.Send(new PollRegistrationProviderReconciliationCommand(tenantId, eventId, bindingId, DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc)), cancellationToken));

    [HttpPost("manual-imports", Name = RouteNames.QueueManualRegistrationProviderImport)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> QueueManualImport(
        Guid tenantId,
        Guid eventId,
        [FromBody] ManualRegistrationProviderImportRequestDto request,
        CancellationToken cancellationToken = default) => ToActionResult(await mediator.Send(new QueueManualRegistrationProviderImportCommand(tenantId, eventId, request.BindingId, request.StorageReference, request.SourceReference), cancellationToken));

    [HttpPost("queue/retry", Name = RouteNames.RetryRegistrationProviderParkedItem)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RetryQueueItem(
        Guid tenantId,
        Guid eventId,
        [FromBody] RetryRegistrationProviderParkedItemRequestDto request,
        CancellationToken cancellationToken = default) => ToActionResult(await mediator.Send(new RetryRegistrationProviderParkedItemCommand(tenantId, eventId, request.SubmissionId, request.EffectOutboxId, request.ExpectedProcessingGeneration, request.Reason), cancellationToken));

    [HttpPost("queue/resolve", Name = RouteNames.ResolveRegistrationProviderQueueItem)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResolveQueueItem(
        Guid tenantId,
        Guid eventId,
        [FromBody] ResolveRegistrationProviderQueueItemRequestDto request,
        CancellationToken cancellationToken = default) => ToActionResult(await mediator.Send(new ResolveRegistrationProviderQueueItemCommand(tenantId, eventId, request.SubmissionId, request.EffectOutboxId, request.DecisionCode, request.NoteReference), cancellationToken));

    private ActionResult<BaseCommandResponse<Guid>> ToActionResult(BaseCommandResponse<Guid> result) => result.IsSuccess
        ? Ok(result)
        : this.ToCommandValidationProblem(result, ProviderManagementValidationProblem);
}
