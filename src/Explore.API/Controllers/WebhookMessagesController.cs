using Explore.Application.Authentication;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Webhook message history, payload inspection, delivery attempts, retry, and incoming redrive.
/// </summary>
/// <remarks>
/// Split out of WebhooksController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/webhooks")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class WebhookMessagesController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<WebhookMessageDto, WebhookMessageDto> webhookMessageAssembler,
    IResourceAssembler<WebhookDeliveryAttemptDto, WebhookDeliveryAttemptDto> webhookDeliveryAttemptAssembler,
    IWebhookOwnershipScopeResolver webhookOwnershipScopeResolver) : WebhooksControllerBase(webhookOwnershipScopeResolver)
{
    private static readonly ApiValidationProblemDescriptor RetryValidationProblem = new(
        "webhookDeliveryRetry",
        "Webhook delivery retry validation failed",
        "Webhook delivery retry command failed.");

    private static readonly ApiValidationProblemDescriptor IncomingRedriveValidationProblem = new(
        "incomingWebhookRedrive",
        "Incoming webhook redrive validation failed",
        "Incoming webhook redrive command failed.");

    private static readonly ApiNotFoundProblemDescriptor IncomingWebhookNotFoundProblem = new(
        "Incoming webhook not found",
        "Incoming webhook was not found.",
        "incoming_webhook_not_found");

    private static readonly ApiNotFoundProblemDescriptor MessageNotFoundProblem = new(
        "Webhook message not found",
        "Webhook message was not found.",
        "webhook_message_not_found");
    private static readonly ApiNotFoundProblemDescriptor MessagePayloadNotFoundProblem = new(
        "Webhook payload not found",
        "Webhook payload was not found.",
        "webhook_message_payload_not_found");
    private static readonly ApiNotFoundProblemDescriptor DeliveryAttemptNotFoundProblem = new(
        "Webhook delivery attempt not found",
        "Webhook delivery attempt was not found.",
        "webhook_delivery_attempt_not_found");
    private static readonly CommandFailurePolicy DeliveryRetryFailures = CommandFailurePolicy
        .ValidatedBy(RetryValidationProblem)
        .NotFound(DeliveryAttemptNotFoundProblem, "webhook_delivery_attempt_not_found")
        .Conflict(
            "Webhook delivery retry not available",
            "Webhook delivery retry cannot be scheduled for this attempt.",
            "webhook_delivery_retry_deferred",
            "webhook_delivery_attempt_active",
            "webhook_delivery_attempt_not_retryable");
    private static readonly CommandFailurePolicy IncomingRedriveFailures = CommandFailurePolicy
        .ValidatedBy(IncomingRedriveValidationProblem)
        .NotFound(IncomingWebhookNotFoundProblem, "incoming_webhook_not_found")
        .Conflict(
            "Incoming webhook redrive not available",
            "Incoming webhook cannot be redriven in its current state.",
            "incoming_webhook_redrive_generation_conflict",
            "incoming_webhook_redrive_active_lease",
            "incoming_webhook_redrive_not_eligible");

    [HttpGet("messages", Name = RouteNames.GetWebhookMessages)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook messages")]
    [EndpointDescription("Returns tenant-scoped outgoing webhook messages with safe delivery-history metadata.")]
    [ProducesResponseType(typeof(HalCollectionResource<WebhookMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<WebhookMessageDto>>> GetMessages(
        [FromQuery] int ownerKindId,
        [FromQuery] Guid? ownerId = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var messages = await mediator.Send(
            new GetWebhookMessagesQuery
            {
                OwnerKindId = ownerKindId,
                OwnerId = ownerId,
                Limit = limit
            },
            cancellationToken);

        var resource = await webhookMessageAssembler.ToCollectionResource(
            messages,
            RouteNames.GetWebhookMessages,
            await CreateCollectionRouteValuesAsync(ownerKindId, ownerId, limit, cancellationToken),
            HttpContext);

        return Ok(resource);
    }

    [HttpGet("messages/{messageId:guid}", Name = RouteNames.GetWebhookMessageById)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook message")]
    [EndpointDescription("Returns one tenant-scoped outgoing webhook message without raw payload JSON.")]
    [ProducesResponseType(typeof(HalResource<WebhookMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalResource<WebhookMessageDto>>> GetMessage(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var message = await mediator.Send(
            new GetWebhookMessageByIdQuery
            {
                MessageId = messageId
            },
            cancellationToken);

        if (message is null)
        {
            return this.ToNotFoundProblem(MessageNotFoundProblem);
        }

        var resource = await webhookMessageAssembler.ToResource(message, HttpContext);
        return Ok(resource);
    }

    [HttpGet("messages/{messageId:guid}/payload", Name = RouteNames.GetWebhookMessagePayload)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook message payload")]
    [EndpointDescription("Returns separately authorized exact outgoing webhook payload bytes as base64 while retained.")]
    [ProducesResponseType(typeof(WebhookMessagePayloadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [ResponseCache(Duration = 0, NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<WebhookMessagePayloadDto>> GetMessagePayload(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetWebhookMessagePayloadQuery
            {
                MessageId = messageId
            },
            cancellationToken);

        return result.Status switch
        {
            WebhookMessagePayloadReadStatus.Available when result.Payload is not null => Ok(result.Payload),
            WebhookMessagePayloadReadStatus.Gone => this.ToGoneProblem(
                "Webhook payload no longer retained",
                "The webhook payload is no longer available because its retention period ended.",
                "webhook_message_payload_gone"),
            _ => this.ToNotFoundProblem(MessagePayloadNotFoundProblem)
        };
    }

    [HttpGet("delivery-attempts", Name = RouteNames.GetWebhookDeliveryAttempts)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook delivery attempts")]
    [EndpointDescription("Returns tenant-scoped LocalProvider delivery attempt audit rows with optional message and endpoint filters.")]
    [ProducesResponseType(typeof(HalCollectionResource<WebhookDeliveryAttemptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<WebhookDeliveryAttemptDto>>> GetDeliveryAttempts(
        [FromQuery] int ownerKindId,
        [FromQuery] Guid? ownerId = null,
        [FromQuery] Guid? messageId = null,
        [FromQuery] Guid? endpointId = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessageId = messageId is { } requestedMessageId && requestedMessageId != Guid.Empty
            ? requestedMessageId
            : (Guid?)null;
        var normalizedEndpointId = endpointId is { } requestedEndpointId && requestedEndpointId != Guid.Empty
            ? requestedEndpointId
            : (Guid?)null;
        var attempts = await mediator.Send(
            new GetWebhookDeliveryAttemptsQuery
            {
                OwnerKindId = ownerKindId,
                OwnerId = ownerId,
                MessageId = normalizedMessageId,
                EndpointId = normalizedEndpointId,
                Limit = limit
            },
            cancellationToken);

        var resource = await webhookDeliveryAttemptAssembler.ToCollectionResource(
            attempts,
            RouteNames.GetWebhookDeliveryAttempts,
            await CreateCollectionRouteValuesAsync(
                ownerKindId,
                ownerId,
                limit,
                cancellationToken,
                messageId: normalizedMessageId,
                endpointId: normalizedEndpointId),
            HttpContext);

        return Ok(resource);
    }

    [HttpGet("delivery-attempts/{attemptId:guid}", Name = RouteNames.GetWebhookDeliveryAttemptById)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook delivery attempt")]
    [EndpointDescription("Returns one tenant-scoped LocalProvider delivery attempt audit row.")]
    [ProducesResponseType(typeof(HalResource<WebhookDeliveryAttemptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalResource<WebhookDeliveryAttemptDto>>> GetDeliveryAttempt(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await mediator.Send(
            new GetWebhookDeliveryAttemptByIdQuery
            {
                AttemptId = attemptId
            },
            cancellationToken);

        if (attempt is null)
        {
            return this.ToNotFoundProblem(DeliveryAttemptNotFoundProblem);
        }

        var resource = await webhookDeliveryAttemptAssembler.ToResource(attempt, HttpContext);
        return Ok(resource);
    }

    [HttpPost("delivery-attempts/{attemptId:guid}/retry", Name = RouteNames.RetryWebhookDeliveryAttempt)]
    [EndpointSummary("Retry webhook delivery attempt")]
    [EndpointDescription("Schedules a manual LocalProvider retry for a failed or abandoned webhook delivery attempt.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RetryDeliveryAttempt(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new RetryWebhookDeliveryAttemptCommand
            {
                AttemptId = attemptId
            },
            cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : DeliveryRetryFailures.Map(this, response);
    }

    [HttpPost("incoming/{incomingWebhookMessageId:guid}/redrive", Name = RouteNames.RedriveIncomingWebhook)]
    [EndpointSummary("Redrive incoming webhook")]
    [EndpointDescription("Schedules a new processing generation for one tenant-scoped dead-lettered incoming webhook.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RedriveIncomingWebhook(
        Guid incomingWebhookMessageId,
        [FromBody] RedriveIncomingWebhookRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new RedriveIncomingWebhookCommand
            {
                TenantId = tenantContext.TenantId,
                IncomingWebhookMessageId = incomingWebhookMessageId,
                ExpectedProcessingGeneration = request.ExpectedProcessingGeneration,
                Reason = request.Reason
            },
            cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : IncomingRedriveFailures.Map(this, response);
    }
}
