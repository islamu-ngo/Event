using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/webhooks/bulk-replays")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class WebhookBulkReplaysController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<WebhookBulkReplayOperationDto, WebhookBulkReplayOperationDto> assembler)
    : EventControllerBase
{
    [HttpGet("preview", Name = RouteNames.PreviewWebhookBulkReplay)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Preview webhook bulk replay")]
    [EndpointDescription("Returns bounded eligible and excluded counts without changing delivery state.")]
    [ProducesResponseType(typeof(WebhookBulkReplayPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<WebhookBulkReplayPreviewDto>> Preview(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        [FromQuery] Guid? consumerId = null,
        [FromQuery] Guid? endpointId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] int maxItems = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new PreviewWebhookBulkReplayQuery
            {
                TenantId = tenantContext.TenantId,
                FromUtc = NormalizeUtcBoundary(fromUtc),
                ToUtc = NormalizeUtcBoundary(toUtc),
                WebhookConsumerId = Normalize(consumerId),
                WebhookEndpointId = Normalize(endpointId),
                EventType = eventType,
                MaxItems = maxItems
            },
            cancellationToken);
        return result.Success
            ? Ok(result.Preview)
            : ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["bulkReplay"] = result.Errors?.ToArray() ?? ["Webhook bulk replay preview failed."]
            }));
    }

    [HttpGet(Name = RouteNames.GetWebhookBulkReplays)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook bulk replays")]
    [EndpointDescription("Returns recent tenant-scoped durable replay operations and state-authorized HAL actions.")]
    [ProducesResponseType(typeof(HalCollectionResource<WebhookBulkReplayOperationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<WebhookBulkReplayOperationDto>>> GetOperations(
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var operations = await mediator.Send(
            new GetWebhookBulkReplayOperationsQuery
            {
                TenantId = tenantContext.TenantId,
                Limit = limit
            },
            cancellationToken);
        return Ok(await assembler.ToCollectionResource(
            operations,
            RouteNames.GetWebhookBulkReplays,
            new { limit },
            HttpContext));
    }

    [HttpGet("{operationId:guid}", Name = RouteNames.GetWebhookBulkReplayById)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook bulk replay")]
    [EndpointDescription("Returns one tenant-scoped durable replay operation with normalized lifecycle evidence.")]
    [ProducesResponseType(typeof(HalResource<WebhookBulkReplayOperationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalResource<WebhookBulkReplayOperationDto>>> GetOperation(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var operation = await mediator.Send(
            new GetWebhookBulkReplayOperationQuery
            {
                TenantId = tenantContext.TenantId,
                OperationId = operationId
            },
            cancellationToken);
        if (operation is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Webhook bulk replay not found",
                detail: "Webhook bulk replay operation was not found.");
        }

        return Ok(await assembler.ToResource(operation, HttpContext));
    }

    [HttpPost(Name = RouteNames.ScheduleWebhookBulkReplay)]
    [EndpointSummary("Schedule webhook bulk replay")]
    [EndpointDescription("Queues an idempotent bounded replay after re-running the requested eligibility preview.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Schedule(
        [FromBody] ScheduleWebhookBulkReplayRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.Filter is null)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["filter"] = ["A webhook bulk replay filter is required."]
            }));
        }

        var response = await mediator.Send(
            new ScheduleWebhookBulkReplayCommand
            {
                TenantId = tenantContext.TenantId,
                ActorUserId = RequiredUserId,
                OperationKey = request.OperationKey,
                FromUtc = NormalizeUtcBoundary(request.Filter.FromUtc),
                ToUtc = NormalizeUtcBoundary(request.Filter.ToUtc),
                WebhookConsumerId = Normalize(request.Filter.WebhookConsumerId),
                WebhookEndpointId = Normalize(request.Filter.WebhookEndpointId),
                EventType = request.Filter.EventType,
                MaxItems = request.Filter.MaxItems,
                ReasonCode = request.ReasonCode
            },
            cancellationToken);
        if (response.IsSuccess)
        {
            return AcceptedAtRoute(
                RouteNames.GetWebhookBulkReplayById,
                new { operationId = response.Id },
                response);
        }

        if (response.FailureCode is
            "webhook_bulk_replay_idempotency_conflict" or
            "webhook_bulk_replay_tenant_capacity_exceeded" or
            "webhook_bulk_replay_no_eligible_work")
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Webhook bulk replay cannot be scheduled",
                detail: response.Message);
        }

        return ValidationFailure(response);
    }

    [HttpPost("{operationId:guid}/cancel", Name = RouteNames.CancelWebhookBulkReplay)]
    [EndpointSummary("Cancel webhook bulk replay")]
    [EndpointDescription("Cancels a queued replay using the caller's observed optimistic concurrency version.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Cancel(
        Guid operationId,
        [FromBody] CancelWebhookBulkReplayRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new CancelWebhookBulkReplayCommand
            {
                TenantId = tenantContext.TenantId,
                ActorUserId = RequiredUserId,
                OperationId = operationId,
                ExpectedConcurrencyVersion = request.ExpectedConcurrencyVersion,
                ReasonCode = request.ReasonCode
            },
            cancellationToken);
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        if (response.FailureCode == "webhook_bulk_replay_not_found")
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Webhook bulk replay not found",
                detail: response.Message);
        }

        if (response.FailureCode is
            "webhook_bulk_replay_concurrency_conflict" or
            "webhook_bulk_replay_not_cancellable")
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Webhook bulk replay cannot be cancelled",
                detail: response.Message);
        }

        return ValidationFailure(response);
    }

    private ActionResult<BaseCommandResponse<Guid>> ValidationFailure(BaseCommandResponse<Guid> response) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["bulkReplay"] = response.Errors?.ToArray() ?? []
        }));

    private static Guid? Normalize(Guid? value) => value is { } id && id != Guid.Empty ? id : null;

    private static DateTime NormalizeUtcBoundary(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
