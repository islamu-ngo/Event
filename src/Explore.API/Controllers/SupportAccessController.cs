using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/support-access")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class SupportAccessController(
    IMediator mediator,
    IResourceAssembler<SupportAccessSessionDto, SupportAccessSessionDto> sessionAssembler,
    IResourceAssembler<SupportAccessAuditEventDto, SupportAccessAuditEventDto> auditEventAssembler) : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor SupportAccessValidationProblem = new(
        "supportAccess",
        "Support-access request failed",
        "Support-access request failed.");

    [HttpGet("current", Name = RouteNames.GetCurrentSupportAccessSession)]
    [EndpointSummary("Get current support-access session")]
    [EndpointDescription("Returns the current authenticated actor's active support-access session, if one is valid for the request.")]
    [ProducesResponseType(typeof(CurrentSupportAccessSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<CurrentSupportAccessSessionDto>> GetCurrent(
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new GetCurrentSupportAccessSessionQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("sessions", Name = RouteNames.StartSupportAccessSession)]
    [EndpointSummary("Start support-access session")]
    [EndpointDescription("Starts a short-lived support-access session for a target tenant using the authenticated actor identity.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(HalResource<SupportAccessSessionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<HalResource<SupportAccessSessionDto>>> Start(
        [FromBody] StartSupportAccessSessionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new StartSupportAccessSessionCommand
            {
                TargetTenantId = request.TargetTenantId,
                TargetTenantUserId = request.TargetTenantUserId,
                Mode = request.Mode,
                DurationMinutes = request.DurationMinutes,
                ReasonCode = request.ReasonCode,
                ReasonText = request.ReasonText,
                TicketReference = request.TicketReference
            },
            cancellationToken);

        if (!response.IsSuccess || response.Session is null)
        {
            return this.ToCommandValidationProblem(response, SupportAccessValidationProblem);
        }

        var resource = await sessionAssembler.ToResource(response.Session, HttpContext);
        return CreatedAtRoute(
            RouteNames.ListSupportAccessSessions,
            new { targetTenantId = response.Session.TargetTenantId },
            resource);
    }

    [HttpPost("sessions/{sessionId:guid}/stop", Name = RouteNames.StopSupportAccessSession)]
    [EndpointSummary("Stop support-access session")]
    [EndpointDescription("Stops the authenticated actor's active support-access session.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(HalResource<SupportAccessSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<HalResource<SupportAccessSessionDto>>> Stop(
        Guid sessionId,
        [FromBody] StopSupportAccessSessionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new StopSupportAccessSessionCommand
            {
                SessionId = sessionId,
                EndReasonText = request.EndReasonText
            },
            cancellationToken);

        if (!response.IsSuccess || response.Session is null)
        {
            return this.ToCommandValidationProblem(response, SupportAccessValidationProblem);
        }

        var resource = await sessionAssembler.ToResource(response.Session, HttpContext);
        return Ok(resource);
    }

    [HttpPost("sessions/{sessionId:guid}/force-stop", Name = RouteNames.ForceStopSupportAccessSession)]
    [EndpointSummary("Force-stop support-access session")]
    [EndpointDescription("Force-stops an active support-access session for emergency revocation.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(HalResource<SupportAccessSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<HalResource<SupportAccessSessionDto>>> ForceStop(
        Guid sessionId,
        [FromBody] ForceStopSupportAccessSessionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new ForceStopSupportAccessSessionCommand
            {
                SessionId = sessionId,
                EndReasonText = request.EndReasonText
            },
            cancellationToken);

        if (!response.IsSuccess || response.Session is null)
        {
            return this.ToCommandValidationProblem(response, SupportAccessValidationProblem);
        }

        var resource = await sessionAssembler.ToResource(response.Session, HttpContext);
        return Ok(resource);
    }

    [HttpGet("tenants/{targetTenantId:guid}/sessions", Name = RouteNames.ListSupportAccessSessions)]
    [EndpointSummary("List support-access sessions")]
    [EndpointDescription("Returns bounded support-access session history for a target tenant.")]
    [ProducesResponseType(typeof(HalCollectionResource<SupportAccessSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<SupportAccessSessionDto>>> ListSessions(
        Guid targetTenantId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new ListSupportAccessSessionsQuery
            {
                TargetTenantId = targetTenantId,
                Limit = limit
            },
            cancellationToken);

        var resource = await sessionAssembler.ToCollectionResource(
            result,
            RouteNames.ListSupportAccessSessions,
            new { targetTenantId, limit },
            HttpContext);

        return Ok(resource);
    }

    [HttpGet("tenants/{targetTenantId:guid}/sessions/{sessionId:guid}/audit-events", Name = RouteNames.GetSupportAccessAuditEvents)]
    [EndpointSummary("Get support-access audit events")]
    [EndpointDescription("Returns bounded audit evidence for a support-access session in a target tenant.")]
    [ProducesResponseType(typeof(HalCollectionResource<SupportAccessAuditEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<SupportAccessAuditEventDto>>> GetAuditEvents(
        Guid targetTenantId,
        Guid sessionId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetSupportAccessAuditEventsQuery
            {
                TargetTenantId = targetTenantId,
                SessionId = sessionId,
                Limit = limit
            },
            cancellationToken);

        var resource = await auditEventAssembler.ToCollectionResource(
            result,
            RouteNames.GetSupportAccessAuditEvents,
            new { targetTenantId, sessionId, limit },
            HttpContext);

        return Ok(resource);
    }
}
