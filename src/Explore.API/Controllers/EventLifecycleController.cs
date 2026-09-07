using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.API.Services.Calendar;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.Events;
using Explore.Application.Features.Events.Moderation;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Specifications.Events;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Controllers;

/// <summary>
/// Event write lifecycle: creation, import, publication, update, archive, cancel, and delete.
/// </summary>
/// <remarks>
/// Split out of the original EventController by route capability. The route template is stated
/// explicitly rather than via the [controller] token so the public URLs are unchanged, and every action
/// keeps its original <c>Name = RouteNames.*</c>, which is what pins the generated operationId.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/Event")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventLifecycleController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "event",
        "Event validation failed",
        "Event creation failed.");

    private static readonly ApiValidationProblemDescriptor ImportValidationProblem = new(
        "event",
        "Event validation failed",
        "Event import failed.");

    private static readonly ApiValidationProblemDescriptor PublishValidationProblem = new(
        "event",
        "Event validation failed",
        "Event publishing failed.");

    private static readonly ApiValidationProblemDescriptor ApprovePublishValidationProblem = new(
        "event",
        "Event validation failed",
        "Event approval-publication failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "event",
        "Event validation failed",
        "Event update failed.");

    private static readonly ApiValidationProblemDescriptor ArchiveValidationProblem = new(
        "event",
        "Event validation failed",
        "Event archive failed.");

    private static readonly ApiValidationProblemDescriptor CancelValidationProblem = new(
        "event",
        "Event validation failed",
        "Event cancel failed.");

    private static readonly ApiNotFoundProblemDescriptor EventNotFoundProblem = new(
        "Event not found",
        "Event not found.");

    private static readonly CommandFailurePolicy ApprovePublishFailures = CommandFailurePolicy
        .ValidatedBy(ApprovePublishValidationProblem)
        .NotFound(EventNotFoundProblem, FailureCodes.NotFound)
        .Conflict(
            "Event approval-publication conflict",
            "Event approval-publication conflict.",
            EventPublicationExecutor.ConcurrencyConflictCode);

    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;


    public EventLifecycleController(
        IMediator mediator,
        ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Create a new event.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEvent)]
    [EndpointSummary("Create Event")]
    [EndpointDescription("Creates a new event. If OrganizationId is provided, the event is created under that organization. " +
        "If GroupId is provided, the event is created under that group. " +
        "If neither is provided, the event is created under the user's personal actor when tenant policy allows user-reported publishing.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventDraftRequestDto draft, CancellationToken cancellationToken = default)
    {
        var command = new CreateEventCommand { EventDto = draft.ToCreateEventDto() };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventById,
            new { id = response.Id },
            response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("import", Name = RouteNames.ImportEvent)]
    [EndpointSummary("Import Event")]
    [EndpointDescription("Imports an event from an external source or backfill path with provenance metadata.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Import([FromBody] ImportEventRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ImportEventCommand
        {
            Request = request,
            TenantId = _tenantContext.TenantId
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, ImportValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventManagementDetails,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Publish a draft event.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/publish", Name = RouteNames.PublishEvent)]
    [EndpointSummary("Publish Event")]
    [EndpointDescription("Publishes a draft event after readiness and concurrency validation. Side effects are written to the transactional outbox.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Publish(Guid id, [FromBody] PublishEventRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new PublishEventCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_publish_concurrency_conflict"
                ? this.ToCommandConflictProblem(response, "Event publish conflict", "Event publishing conflict.")
                : this.ToCommandValidationProblem(response, PublishValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Approve and publish a draft event through the privileged approval boundary.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/approve-publish", Name = RouteNames.ApprovePublishEvent)]
    [EndpointSummary("Approve And Publish Event")]
    [EndpointDescription("Approves and publishes a ready draft event after privileged authorization and concurrency validation. Side effects are written to the transactional outbox.")]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ApprovePublish(
        Guid id,
        [FromBody] PublishEventRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ApprovePublishEventCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : ApprovePublishFailures.Map(this, response);
    }

    /// <summary>
    /// Partially update an existing event's editable property groups.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEvent)]
    [EndpointSummary("Update Event")]
    [EndpointDescription("Partially update editable event shell fields. Route ID is authoritative and If-Match must contain the current event concurrency stamp.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventDto updateDto,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current event concurrency stamp.");
        }

        var command = new UpdateEventCommand
        {
            EventId = id,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            UpdateEventDto = updateDto
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(EventNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Archive an event. Tolerant lifecycle transition — no public outbox events.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/archive", Name = RouteNames.ArchiveEvent)]
    [EndpointSummary("Archive Event")]
    [EndpointDescription("Archives an event after concurrency validation. Archived events are removed from public discovery. No public outbox events are emitted.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Archive(Guid id, [FromBody] ArchiveEventRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ArchiveEventCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_archive_concurrency_conflict"
                ? this.ToCommandConflictProblem(response, "Event archive conflict", "Event archive conflict.")
                : this.ToCommandValidationProblem(response, ArchiveValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Cancel an event. Tolerant lifecycle transition — no public outbox events.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("{id:guid}/cancel", Name = RouteNames.CancelEvent)]
    [EndpointSummary("Cancel Event")]
    [EndpointDescription("Cancels an event after concurrency validation. Registrations and public calls to action stop being available. No public outbox events are emitted.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Cancel(Guid id, [FromBody] CancelEventRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CancelEventCommand
        {
            Id = id,
            Request = request
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == "event_cancel_concurrency_conflict"
                ? this.ToCommandConflictProblem(response, "Event cancel conflict", "Event cancel conflict.")
                : this.ToCommandValidationProblem(response, CancelValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Delete an event.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEvent)]
    [EndpointSummary("Delete Event")]
    [EndpointDescription("Delete an event. User must be a member of the organization that owns the event.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();

        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new DeleteEventCommand { Id = id, UserId = userId };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
