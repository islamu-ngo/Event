using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/admin/email-dispatch")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class EmailDispatchAdminController : EventControllerBase
{
    private readonly ICommandHandler<SetEmailDispatchTenantPauseStateCommand, BaseCommandResponse<Guid>> _tenantPauseCommand;
    private readonly ICommandHandler<ParkEmailDispatchCommand, BaseCommandResponse<Guid>> _parkCommand;
    private readonly ICommandHandler<ResolveEmailDispatchWithoutReplayCommand, BaseCommandResponse<Guid>> _resolveCommand;
    private readonly ICommandHandler<ReconcileUnknownEmailDispatchCommand, BaseCommandResponse<Guid>> _reconcileCommand;
    private readonly IQueryHandler<GetEmailDispatchStatusQuery, BaseCommandResponse<IReadOnlyList<EmailDispatchStatusDto>>> _statusQuery;
    private readonly IQueryHandler<GetEmailDispatchProcessorControlQuery, EmailDispatchProcessorControlDto> _controlQuery;
    private readonly ICommandHandler<ReplayEmailDispatchCommand, BaseCommandResponse<Guid>> _replayCommand;
    private readonly ICommandHandler<SetEmailDispatchProcessorPauseStateCommand, BaseCommandResponse<Guid>> _pauseCommand;
    private readonly ICommandHandler<SetEmailDispatchGlobalRateLimitOverrideCommand, BaseCommandResponse<Guid>> _rateLimitCommand;
    private readonly IResourceAssembler<EmailDispatchStatusDto, EmailDispatchStatusDto> _statusAssembler;
    private readonly IResourceAssembler<EmailDispatchProcessorControlDto, EmailDispatchProcessorControlDto> _processorControlAssembler;

    public EmailDispatchAdminController(
        ICommandHandler<SetEmailDispatchTenantPauseStateCommand, BaseCommandResponse<Guid>> tenantPauseCommand,
        ICommandHandler<ParkEmailDispatchCommand, BaseCommandResponse<Guid>> parkCommand,
        ICommandHandler<ResolveEmailDispatchWithoutReplayCommand, BaseCommandResponse<Guid>> resolveCommand,
        ICommandHandler<ReconcileUnknownEmailDispatchCommand, BaseCommandResponse<Guid>> reconcileCommand,
        IQueryHandler<GetEmailDispatchStatusQuery, BaseCommandResponse<IReadOnlyList<EmailDispatchStatusDto>>> statusQuery,
        IQueryHandler<GetEmailDispatchProcessorControlQuery, EmailDispatchProcessorControlDto> controlQuery,
        ICommandHandler<ReplayEmailDispatchCommand, BaseCommandResponse<Guid>> replayCommand,
        ICommandHandler<SetEmailDispatchProcessorPauseStateCommand, BaseCommandResponse<Guid>> pauseCommand,
        ICommandHandler<SetEmailDispatchGlobalRateLimitOverrideCommand, BaseCommandResponse<Guid>> rateLimitCommand,
        IResourceAssembler<EmailDispatchStatusDto, EmailDispatchStatusDto> statusAssembler,
        IResourceAssembler<EmailDispatchProcessorControlDto, EmailDispatchProcessorControlDto> processorControlAssembler)
    {
        _tenantPauseCommand = tenantPauseCommand;
        _parkCommand = parkCommand;
        _resolveCommand = resolveCommand;
        _reconcileCommand = reconcileCommand;
        _statusQuery = statusQuery;
        _controlQuery = controlQuery;
        _replayCommand = replayCommand;
        _pauseCommand = pauseCommand;
        _rateLimitCommand = rateLimitCommand;
        _statusAssembler = statusAssembler;
        _processorControlAssembler = processorControlAssembler;
    }

    /// <summary>
    /// Get sanitized Basic Dispatch Mode status rows for a tenant.
    /// </summary>
    [HttpGet("status", Name = RouteNames.GetEmailDispatchStatus)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(HalCollectionResource<EmailDispatchStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<EmailDispatchStatusDto>>> GetStatus(
        [FromQuery] EmailDispatchStatusQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _statusQuery.QueryAsync(
            new GetEmailDispatchStatusQuery { TenantId = query.TenantId, Limit = query.Limit },
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToEmailDispatchValidationProblem(
                result.Message ?? "Email dispatch status query failed.",
                result.Errors);
        }

        var resource = await _statusAssembler.ToCollectionResource(
            result.Id ?? [],
            RouteNames.GetEmailDispatchStatus,
            new { tenantId = query.TenantId, limit = query.Limit },
            HttpContext);

        return Ok(resource);
    }

    /// <summary>
    /// Pause Basic Dispatch Mode email delivery for one tenant.
    /// </summary>
    [HttpPut("tenants/{tenantId:guid}/pause", Name = RouteNames.PauseEmailDispatchTenant)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PauseTenant(
        Guid tenantId,
        [FromQuery] EmailDispatchPauseTenantQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _tenantPauseCommand.ExecuteAsync(
            new SetEmailDispatchTenantPauseStateCommand
            {
                TenantId = tenantId,
                IsPaused = true,
                PauseReason = query.GetNormalizedReason(),
                ChangedBy = CurrentUserId
            },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>
    /// Resume Basic Dispatch Mode email delivery for one tenant.
    /// </summary>
    [HttpDelete("tenants/{tenantId:guid}/pause", Name = RouteNames.ResumeEmailDispatchTenant)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResumeTenant(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var result = await _tenantPauseCommand.ExecuteAsync(
            new SetEmailDispatchTenantPauseStateCommand
            {
                TenantId = tenantId,
                IsPaused = false,
                ChangedBy = CurrentUserId
            },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>
    /// Park one unsafe EmailDispatch outbox row for operator review.
    /// </summary>
    [HttpPut("tenants/{tenantId:guid}/outbox/{outboxId:guid}/park", Name = RouteNames.ParkEmailDispatch)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ParkDispatch(
        Guid tenantId,
        Guid outboxId,
        [FromQuery] EmailDispatchParkQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _parkCommand.ExecuteAsync(
            new ParkEmailDispatchCommand
            {
                TenantId = tenantId,
                OutboxId = outboxId,
                Reason = query.GetNormalizedReason(),
                ChangedBy = CurrentUserId
            },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>
    /// Replay one deferred EmailDispatch outbox row by resetting durable PostgreSQL state.
    /// </summary>
    [HttpPost("tenants/{tenantId:guid}/outbox/{outboxId:guid}/replay", Name = RouteNames.ReplayEmailDispatch)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ReplayDispatch(
        Guid tenantId,
        Guid outboxId,
        CancellationToken cancellationToken = default)
    {
        var result = await _replayCommand.ExecuteAsync(
            new ReplayEmailDispatchCommand
            {
                TenantId = tenantId,
                OutboxId = outboxId,
                ChangedBy = CurrentUserId
            },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>
    /// Resolve one deferred EmailDispatch row without replaying its content.
    /// </summary>
    [HttpPost("tenants/{tenantId:guid}/outbox/{outboxId:guid}/resolve-without-replay", Name = RouteNames.ResolveEmailDispatchWithoutReplay)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResolveWithoutReplay(
        Guid tenantId,
        Guid outboxId,
        [FromQuery] EmailDispatchResolveQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _resolveCommand.ExecuteAsync(
            new ResolveEmailDispatchWithoutReplayCommand
            {
                TenantId = tenantId,
                OutboxId = outboxId,
                Reason = query.GetNormalizedReason(),
                ChangedBy = CurrentUserId
            },
            cancellationToken);

        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>Get sanitized instance-wide SMTP processor control state.</summary>
    [HttpGet("control", Name = RouteNames.GetEmailDispatchProcessorControl)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(HalResource<EmailDispatchProcessorControlDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<EmailDispatchProcessorControlDto>>> GetProcessorControl(
        CancellationToken cancellationToken = default)
    {
        var result = await _controlQuery.QueryAsync(new GetEmailDispatchProcessorControlQuery(), cancellationToken);
        return Ok(await _processorControlAssembler.ToResource(result, HttpContext));
    }

    /// <summary>Pause every SMTP dispatch admission path for the instance.</summary>
    [HttpPut("control/pause", Name = RouteNames.PauseEmailDispatchProcessor)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PauseProcessor(
        [FromQuery] EmailDispatchProcessorPauseQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _pauseCommand.ExecuteAsync(new SetEmailDispatchProcessorPauseStateCommand
        {
            IsPaused = true,
            PauseReason = query.GetNormalizedReason(),
            ChangedBy = CurrentUserId
        }, cancellationToken);
        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>Resume SMTP dispatch admission for the instance.</summary>
    [HttpDelete("control/pause", Name = RouteNames.ResumeEmailDispatchProcessor)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ResumeProcessor(
        CancellationToken cancellationToken = default)
    {
        var result = await _pauseCommand.ExecuteAsync(new SetEmailDispatchProcessorPauseStateCommand
        {
            IsPaused = false,
            ChangedBy = CurrentUserId
        }, cancellationToken);
        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>Set the persisted instance-wide SMTP rate-limit override.</summary>
    [HttpPut("control/rate-limit", Name = RouteNames.SetEmailDispatchGlobalRateLimitOverride)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SetGlobalRateLimitOverride(
        [FromQuery] EmailDispatchGlobalRateLimitQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _rateLimitCommand.ExecuteAsync(new SetEmailDispatchGlobalRateLimitOverrideCommand
        {
            RateLimitPerMinute = query.RateLimitPerMinute,
            ChangedBy = CurrentUserId
        }, cancellationToken);
        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>Clear the persisted SMTP rate override and restore configured rate.</summary>
    [HttpDelete("control/rate-limit", Name = RouteNames.ClearEmailDispatchGlobalRateLimitOverride)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ClearGlobalRateLimitOverride(
        CancellationToken cancellationToken = default)
    {
        var result = await _rateLimitCommand.ExecuteAsync(new SetEmailDispatchGlobalRateLimitOverrideCommand
        {
            RateLimitPerMinute = null,
            ChangedBy = CurrentUserId
        }, cancellationToken);
        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }

    /// <summary>Resolve an Unknown SMTP outcome as delivered or not delivered.</summary>
    [HttpPost("tenants/{tenantId:guid}/outbox/{outboxId:guid}/reconcile", Name = RouteNames.ReconcileUnknownEmailDispatch)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ReconcileUnknown(
        Guid tenantId,
        Guid outboxId,
        [FromQuery] EmailDispatchReconciliationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _reconcileCommand.ExecuteAsync(new ReconcileUnknownEmailDispatchCommand
        {
            TenantId = tenantId,
            OutboxId = outboxId,
            Outcome = query.Outcome,
            Reason = query.GetNormalizedReason(),
            ProviderMessageId = string.IsNullOrWhiteSpace(query.ProviderMessageId)
                ? null
                : query.ProviderMessageId.Trim(),
            ChangedBy = CurrentUserId
        }, cancellationToken);
        return result.IsSuccess ? Ok(result) : this.ToEmailDispatchProblem(result);
    }
}
