using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.DTOs.Scheduling;
using Explore.Application.Features.Scheduling.Requests.Commands;
using Explore.Application.Features.Scheduling.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Operator surface for the instance scheduler. It is opt-in: when the host has not enabled the administration
/// API every action answers <c>404</c>, so a client that discovers navigation from HAL simply never sees the
/// section rather than seeing a control that fails when used.
/// </summary>
[ApiVersion("0.1")]
[Route("api/admin/scheduler")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class SchedulerAdminController : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor SchedulerSurfaceDisabledProblem = new(
        "Scheduler administration not found",
        "This host does not expose the scheduler administration API.");

    private static readonly ApiValidationProblemDescriptor SchedulerConfirmationRequiredProblem = new(
        "confirmationText",
        "Scheduler confirmation required",
        "This action requires the scheduler name as typed confirmation.");

    private static readonly ApiNotFoundProblemDescriptor SchedulerJobNotFoundProblem = new(
        "Scheduled job not found",
        "The requested scheduled job does not exist in this instance's scheduler.");

    private readonly IMediator _mediator;
    private readonly ISchedulerAdminPolicy _policy;
    private readonly IResourceAssembler<SchedulerAdminOverviewDto, SchedulerAdminOverviewDto> _overviewAssembler;
    private readonly IResourceAssembler<SchedulerAdminJobDto, SchedulerAdminJobDto> _jobAssembler;
    private readonly ISchedulerAdminAuditSink _auditSink;

    public SchedulerAdminController(
        IMediator mediator,
        ISchedulerAdminPolicy policy,
        IResourceAssembler<SchedulerAdminOverviewDto, SchedulerAdminOverviewDto> overviewAssembler,
        IResourceAssembler<SchedulerAdminJobDto, SchedulerAdminJobDto> jobAssembler,
        ISchedulerAdminAuditSink auditSink)
    {
        _mediator = mediator;
        _policy = policy;
        _overviewAssembler = overviewAssembler;
        _jobAssembler = jobAssembler;
        _auditSink = auditSink;
    }

    [HttpGet(Name = RouteNames.GetSchedulerAdminOverview)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Scheduler Administration Overview")]
    [EndpointDescription("Returns live scheduler lifecycle state, scheduled jobs, trigger states, and fire times for instance administrators.")]
    [ProducesResponseType(typeof(HalResource<SchedulerAdminOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<SchedulerAdminOverviewDto>>> GetOverview(
        CancellationToken cancellationToken = default)
    {
        if (!_policy.IsEnabled)
        {
            return this.ToNotFoundProblem(SchedulerSurfaceDisabledProblem);
        }

        var overview = await _mediator.Send(new GetSchedulerAdminOverviewQuery(), cancellationToken);
        var resource = await _overviewAssembler.ToResource(overview, HttpContext);

        return Ok(resource);
    }

    [HttpGet("jobs", Name = RouteNames.GetSchedulerAdminJobs)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("List Scheduled Jobs")]
    [EndpointDescription("Returns every scheduled job with its owner, trigger states, schedule summary, and fire times.")]
    [ProducesResponseType(typeof(HalCollectionResource<SchedulerAdminJobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<SchedulerAdminJobDto>>> GetJobs(
        CancellationToken cancellationToken = default)
    {
        if (!_policy.IsEnabled)
        {
            return this.ToNotFoundProblem(SchedulerSurfaceDisabledProblem);
        }

        var jobs = await _mediator.Send(new GetSchedulerAdminJobsQuery(), cancellationToken);
        var resource = await _jobAssembler.ToCollectionResource(
            jobs,
            RouteNames.GetSchedulerAdminJobs,
            HttpContext);

        return Ok(resource);
    }

    [HttpPost("pause", Name = RouteNames.PauseScheduler)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Pause Scheduler")]
    [EndpointDescription("Moves the scheduler to standby so no further triggers fire. Running jobs are allowed to finish.")]
    [ProducesResponseType(typeof(BaseCommandResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<string>>> Pause(
        [FromBody] SchedulerPauseRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            new PauseSchedulerCommand { ConfirmationText = request?.ConfirmationText },
            SchedulerAdminAuditActions.PauseScheduler,
            jobGroup: null,
            jobName: null,
            cancellationToken);

    [HttpPost("resume", Name = RouteNames.ResumeScheduler)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Resume Scheduler")]
    [EndpointDescription("Returns the scheduler from standby so its triggers fire again.")]
    [ProducesResponseType(typeof(BaseCommandResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<string>>> Resume(CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            new ResumeSchedulerCommand(),
            SchedulerAdminAuditActions.ResumeScheduler,
            jobGroup: null,
            jobName: null,
            cancellationToken);

    [HttpPost("jobs/{group}/{name}/pause", Name = RouteNames.PauseSchedulerJob)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Pause Scheduled Job")]
    [EndpointDescription("Pauses every trigger attached to one scheduled job without removing its schedule.")]
    [ProducesResponseType(typeof(BaseCommandResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<string>>> PauseJob(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new PauseSchedulerJobCommand { Group = group, Name = name }, SchedulerAdminAuditActions.PauseJob, group, name, cancellationToken);

    [HttpPost("jobs/{group}/{name}/resume", Name = RouteNames.ResumeSchedulerJob)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Resume Scheduled Job")]
    [EndpointDescription("Resumes every paused trigger attached to one scheduled job.")]
    [ProducesResponseType(typeof(BaseCommandResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<string>>> ResumeJob(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new ResumeSchedulerJobCommand { Group = group, Name = name }, SchedulerAdminAuditActions.ResumeJob, group, name, cancellationToken);

    [HttpPost("jobs/{group}/{name}/trigger", Name = RouteNames.TriggerSchedulerJob)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Trigger Scheduled Job")]
    [EndpointDescription("Runs one scheduled job immediately without altering its existing triggers or schedule.")]
    [ProducesResponseType(typeof(BaseCommandResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<string>>> TriggerJob(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new TriggerSchedulerJobCommand { Group = group, Name = name }, SchedulerAdminAuditActions.TriggerJob, group, name, cancellationToken);

    [HttpPost("jobs/{group}/{name}/reset-error", Name = RouteNames.ResetSchedulerJobErrorState)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Reset Scheduled Job Error State")]
    [EndpointDescription("Clears every trigger of one scheduled job out of the scheduler error state so it fires on its normal schedule again.")]
    [ProducesResponseType(typeof(BaseCommandResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<string>>> ResetJobErrorState(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new ResetSchedulerJobErrorStateCommand { Group = group, Name = name }, SchedulerAdminAuditActions.ResetJobErrorState, group, name, cancellationToken);

    [HttpPost("jobs/{group}/{name}/interrupt", Name = RouteNames.InterruptSchedulerJob)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Interrupt Scheduled Job")]
    [EndpointDescription("Requests cooperative cancellation of a job's currently executing instances. The job stops at its next cancellation checkpoint; a job that ignores cancellation keeps running.")]
    [ProducesResponseType(typeof(BaseCommandResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<ActionResult<BaseCommandResponse<string>>> InterruptJob(
        string group,
        string name,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(new InterruptSchedulerJobCommand { Group = group, Name = name }, SchedulerAdminAuditActions.InterruptJob, group, name, cancellationToken);

    /// <summary>
    /// Runs one scheduler command and maps its structured refusal onto HTTP semantics. Mapping lives here so the
    /// handlers stay transport-neutral and every action reports the same status for the same refusal.
    /// </summary>
    private async Task<ActionResult<BaseCommandResponse<string>>> ExecuteAsync(
        IRequest<BaseCommandResponse<string>> command,
        string auditAction,
        string? jobGroup,
        string? jobName,
        CancellationToken cancellationToken)
    {
        if (!_policy.IsEnabled)
        {
            return this.ToNotFoundProblem(SchedulerSurfaceDisabledProblem);
        }

        var response = await _mediator.Send(command, cancellationToken);

        // Audited at the boundary, where the principal and correlation id are available, and for refusals as well
        // as successes: a denied privileged action is the one most worth having a record of.
        await _auditSink.RecordAsync(
            new SchedulerAdminAuditRecord(
                auditAction,
                User.Identity?.Name ?? CurrentUserId?.ToString() ?? "unknown",
                jobGroup,
                jobName,
                response.IsSuccess,
                response.FailureCode,
                HttpContext.TraceIdentifier,
                DateTime.UtcNow),
            cancellationToken);

        if (response.IsSuccess)
        {
            return Ok(response);
        }

        // A read-only or disabled scheduler advertises no write affordance, so reaching a refusal here means the
        // caller bypassed HAL discovery. Conflict states that the surface itself refuses the action, which is a
        // different fact from the caller lacking authority (403) or the job not existing (404).
        if (response.FailureCode == FailureCodes.SchedulerConfirmationRequired)
        {
            return this.ToValidationProblem(
                SchedulerConfirmationRequiredProblem,
                response.Message ?? "This action requires typed confirmation.");
        }

        return response.FailureCode == FailureCodes.NotFound
            ? this.ToNotFoundProblem(SchedulerJobNotFoundProblem, response.Message)
            : this.ToCommandConflictProblem(
                response,
                "Scheduler action refused",
                "The scheduler could not complete the requested action.");
    }
}
