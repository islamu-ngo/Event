using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/moderation/reports")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class ModerationReportController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor ModerationReportNotFoundProblem = new(
        "Moderation report not found",
        "Moderation report was not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<ModerationReportDetailDto, ModerationReportQueueItemDto> _resourceAssembler;
    private readonly ITenantContext _tenantContext;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<ModerationReportController> _logger;

    public ModerationReportController(
        IMediator mediator,
        IResourceAssembler<ModerationReportDetailDto, ModerationReportQueueItemDto> resourceAssembler,
        ITenantContext tenantContext,
        BusinessMetrics metrics,
        ILogger<ModerationReportController> logger)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
        _tenantContext = tenantContext;
        _metrics = metrics;
        _logger = logger;
    }

    [HttpGet(Name = RouteNames.GetModerationReportQueue)]
    [EndpointSummary("Get Moderation Report Queue")]
    [EndpointDescription("Returns event-scoped moderation report queue rows for authorized management views.")]
    [ProducesResponseType(typeof(HalCollectionResource<ModerationReportQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<ModerationReportQueueItemDto>>> GetQueue(
        Guid eventId,
        [FromQuery] ModerationReportQueueQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetModerationReportQueueRequest
        {
            EventId = eventId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            Statuses = query.ToStatuses(),
            CaseStatuses = query.ToCaseStatuses(),
            Priority = query.ToPriority(),
            QueueCode = query.QueueCode,
            AssignedModeratorUserId = query.AssignedModeratorUserId,
            UnassignedOnly = query.UnassignedOnly,
            OpenOnly = query.OpenOnly,
            ReasonCode = query.ReasonCode,
            SortBy = query.ToSortBy(),
            SortDescending = query.SortDescending
        }, cancellationToken);

        var resource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetModerationReportQueue,
            additionalRouteValues: new
            {
                eventId,
                query.QueueCode,
                query.AssignedModeratorUserId,
                query.UnassignedOnly,
                query.OpenOnly,
                query.ReasonCode,
                SortBy = query.ToSortBy(),
                query.SortDescending
            },
            HttpContext);

        return Ok(resource);
    }

    [HttpGet("{reportId:guid}", Name = RouteNames.GetModerationReportDetail)]
    [EndpointSummary("Get Moderation Report Detail")]
    [EndpointDescription("Returns safe event-report evidence, case, decision, signal, and provider-link metadata for authorized management views.")]
    [ProducesResponseType(typeof(HalResource<ModerationReportDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalResource<ModerationReportDetailDto>>> GetDetail(
        Guid eventId,
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        var report = await _mediator.Send(new GetModerationReportDetailRequest
        {
            EventId = eventId,
            ReportId = reportId
        }, cancellationToken);

        if (report is null)
        {
            return this.ToNotFoundProblem(ModerationReportNotFoundProblem);
        }

        var resource = await _resourceAssembler.ToResource(report, HttpContext);
        return Ok(resource);
    }

    [HttpPost("{reportId:guid}/triage", Name = RouteNames.TriageModerationReport)]
    [EndpointSummary("Triage Moderation Report")]
    [EndpointDescription("Moves an open report case into a moderation queue with priority and optimistic concurrency validation.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Triage(
        Guid eventId,
        Guid reportId,
        [FromBody] TriageModerationReportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new TriageEventReportCommand
        {
            EventId = eventId,
            ReportId = reportId,
            CaseId = request.CaseId,
            ExpectedCaseConcurrencyStamp = request.ExpectedCaseConcurrencyStamp,
            QueueCode = request.QueueCode,
            Priority = request.Priority
        }, cancellationToken);

        return ToWorkflowActionResult(response, "triage", eventId, reportId);
    }

    [HttpPost("{reportId:guid}/assign", Name = RouteNames.AssignModerationReport)]
    [EndpointSummary("Assign Moderation Report")]
    [EndpointDescription("Assigns an open or assigned report case to an active tenant moderator.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Assign(
        Guid eventId,
        Guid reportId,
        [FromBody] AssignModerationReportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new AssignEventReportCommand
        {
            EventId = eventId,
            ReportId = reportId,
            CaseId = request.CaseId,
            ExpectedCaseConcurrencyStamp = request.ExpectedCaseConcurrencyStamp,
            AssigneeUserId = request.AssigneeUserId
        }, cancellationToken);

        return ToWorkflowActionResult(response, "assign", eventId, reportId);
    }

    [HttpPost("{reportId:guid}/decision", Name = RouteNames.DecideModerationReport)]
    [EndpointSummary("Decide Moderation Report")]
    [EndpointDescription("Records a safe local moderator decision for an assigned report case.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Decide(
        Guid eventId,
        Guid reportId,
        [FromBody] DecideModerationReportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DecideEventReportCommand
        {
            EventId = eventId,
            ReportId = reportId,
            CaseId = request.CaseId,
            ExpectedCaseConcurrencyStamp = request.ExpectedCaseConcurrencyStamp,
            DecisionKind = request.DecisionKind,
            ReasonCode = request.ReasonCode,
            SafeNote = request.SafeNote,
            DuplicateGroupId = request.DuplicateGroupId
        }, cancellationToken);

        return ToWorkflowActionResult(response, "decide", eventId, reportId);
    }

    [HttpPost("{reportId:guid}/decision/execute", Name = RouteNames.ExecuteModerationReportDecision)]
    [EndpointSummary("Execute Moderation Report Decision")]
    [EndpointDescription("Executes a decision-ready report case through the canonical event moderation command path.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ExecuteDecision(
        Guid eventId,
        Guid reportId,
        [FromBody] ExecuteModerationReportDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ExecuteReportDecisionCommand
        {
            EventId = eventId,
            ReportId = reportId,
            CaseId = request.CaseId,
            DecisionId = request.DecisionId,
            ExpectedCaseConcurrencyStamp = request.ExpectedCaseConcurrencyStamp,
            CorrelationId = request.CorrelationId
        }, cancellationToken);

        return ToWorkflowActionResult(response, "execute", eventId, reportId);
    }

    private ActionResult<BaseCommandResponse<Guid>> ToWorkflowActionResult(
        BaseCommandResponse<Guid> response,
        string action,
        Guid eventId,
        Guid reportId)
    {
        var outcome = response.IsSuccess ? "succeeded" : "failed";
        var failureCategory = response.FailureCode ?? "none";
        _metrics.RecordEventReportWorkflowAction(
            GetTenantMetricTag(),
            action,
            outcome,
            failureCategory);
        _logger.LogInformation(
            "Moderation report workflow action {Action} completed for event {EventId} report {ReportId} outcome {Outcome} failure {FailureCategory}",
            action,
            eventId,
            reportId,
            outcome,
            failureCategory);

        return response.IsSuccess ? Ok(response) : this.ToEventReportProblem(response);
    }

    private string? GetTenantMetricTag()
        => _tenantContext.TenantId == Guid.Empty ? null : _tenantContext.TenantId.ToString();
}
