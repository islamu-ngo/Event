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
/// Webhook endpoint registration, secret rotation, and delivery testing for a consumer.
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
public sealed class WebhookEndpointsController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<WebhookEndpointDto, WebhookEndpointDto> webhookEndpointAssembler,
    IWebhookOwnershipScopeResolver webhookOwnershipScopeResolver) : WebhooksControllerBase(webhookOwnershipScopeResolver)
{
    private static readonly ApiValidationProblemDescriptor EndpointValidationProblem = new(
        "webhookEndpoint",
        "Webhook endpoint validation failed",
        "Webhook endpoint command failed.");

    private static readonly ApiNotFoundProblemDescriptor ConsumerNotFoundProblem = new(
        "Webhook consumer not found",
        "Webhook consumer was not found.",
        "webhook_consumer_not_found");

    private static readonly ApiNotFoundProblemDescriptor EndpointNotFoundProblem = new(
        "Webhook endpoint not found",
        "Webhook endpoint was not found.",
        "webhook_endpoint_not_found");
    private static readonly CommandFailurePolicy EndpointFailures = CommandFailurePolicy
        .ValidatedBy(EndpointValidationProblem)
        .Conflict(
            "Webhook endpoint already exists",
            "Webhook endpoint URL is already configured for this consumer.",
            "webhook_endpoint_url_conflict")
        .NotFound(ConsumerNotFoundProblem, "webhook_consumer_not_found")
        .NotFound(EndpointNotFoundProblem, "webhook_endpoint_not_found");

    [HttpGet("endpoints", Name = RouteNames.GetWebhookEndpoints)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook endpoints")]
    [EndpointDescription("Returns outgoing webhook endpoints for one typed owner with HAL management affordances.")]
    [OutputCache(PolicyName = "ListData")]
    [ProducesResponseType(typeof(HalCollectionResource<WebhookEndpointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<WebhookEndpointDto>>> GetEndpoints(
        [FromQuery] int ownerKindId,
        [FromQuery] Guid? ownerId = null,
        [FromQuery] Guid? consumerId = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedConsumerId = consumerId == Guid.Empty ? null : consumerId;
        var endpoints = await mediator.Send(
            new GetWebhookEndpointsQuery
            {
                OwnerKindId = ownerKindId,
                OwnerId = ownerId,
                ConsumerId = normalizedConsumerId,
                Limit = limit
            },
            cancellationToken);

        var resource = await webhookEndpointAssembler.ToCollectionResource(
            endpoints,
            RouteNames.GetWebhookEndpoints,
            await CreateCollectionRouteValuesAsync(
                ownerKindId,
                ownerId,
                limit,
                cancellationToken,
                consumerId: normalizedConsumerId),
            HttpContext);

        return Ok(resource);
    }

    [HttpGet("endpoints/{endpointId:guid}", Name = RouteNames.GetWebhookEndpointById)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook endpoint")]
    [EndpointDescription("Returns one owner-authorized outgoing webhook endpoint with subscription metadata.")]
    [ProducesResponseType(typeof(HalResource<WebhookEndpointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalResource<WebhookEndpointDto>>> GetEndpoint(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await mediator.Send(
            new GetWebhookEndpointByIdQuery
            {
                EndpointId = endpointId
            },
            cancellationToken);

        if (endpoint is null)
        {
            return this.ToNotFoundProblem(EndpointNotFoundProblem);
        }

        var resource = await webhookEndpointAssembler.ToResource(endpoint, HttpContext);
        return Ok(resource);
    }

    [HttpPost("endpoints", Name = RouteNames.CreateWebhookEndpoint)]
    [EndpointSummary("Create webhook endpoint")]
    [EndpointDescription("Creates an outgoing webhook endpoint that inherits its persisted consumer owner.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateEndpoint(
        [FromBody] CreateWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new CreateWebhookEndpointCommand
            {
                ConsumerId = request.ConsumerId,
                Url = request.Url,
                Description = request.Description,
                SecretRef = request.SecretRef,
                EventTypeIds = request.EventTypeIds,
                MaxAttempts = request.MaxAttempts,
                TimeoutSeconds = request.TimeoutSeconds,
                RateLimitPerMinute = request.RateLimitPerMinute
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return EndpointFailures.Map(this, response);
        }

        return CreatedAtRoute(
            RouteNames.GetWebhookEndpointById,
            new { endpointId = response.Id },
            response);
    }

    [HttpPatch("endpoints/{endpointId:guid}", Name = RouteNames.UpdateWebhookEndpoint)]
    [EndpointSummary("Update webhook endpoint")]
    [EndpointDescription("Updates a tenant-scoped outgoing webhook endpoint and replaces its event type subscriptions.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateEndpoint(
        Guid endpointId,
        [FromBody] UpdateWebhookEndpointRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new UpdateWebhookEndpointCommand
            {
                EndpointId = endpointId,
                Destination = request.Destination,
                Subscriptions = request.Subscriptions,
                DeliveryPolicy = request.DeliveryPolicy,
                Governance = request.Governance
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return EndpointFailures.Map(this, response);
        }

        return Ok(response);
    }

    [HttpDelete("endpoints/{endpointId:guid}", Name = RouteNames.DeleteWebhookEndpoint)]
    [EndpointSummary("Delete webhook endpoint")]
    [EndpointDescription("Archives a tenant-scoped outgoing webhook endpoint while preserving delivery history.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult> DeleteEndpoint(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new ArchiveWebhookEndpointCommand
            {
                EndpointId = endpointId
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return EndpointFailures.Map(this, response);
        }

        return NoContent();
    }

    [HttpPost("endpoints/{endpointId:guid}/rotate-secret", Name = RouteNames.RotateWebhookEndpointSecret)]
    [EndpointSummary("Rotate webhook endpoint secret")]
    [EndpointDescription("Rotates the signing secret reference for a tenant-scoped outgoing webhook endpoint.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RotateEndpointSecret(
        Guid endpointId,
        [FromBody] RotateWebhookEndpointSecretRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new RotateWebhookEndpointSecretCommand
            {
                EndpointId = endpointId,
                NewSecretRef = request.NewSecretRef,
                PreviousSecretValidForSeconds = request.PreviousSecretValidForSeconds,
                ExpectedConfigurationVersion = request.ExpectedConfigurationVersion,
                PendingWorkDecisionId = request.PendingWorkDecisionId,
                PendingWorkReason = request.PendingWorkReason,
                AcknowledgeUncertainProviderPublications = request.AcknowledgeUncertainProviderPublications
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return EndpointFailures.Map(this, response);
        }

        return Ok(response);
    }

    [HttpPost("endpoints/{endpointId:guid}/test", Name = RouteNames.TestWebhookEndpoint)]
    [EndpointSummary("Test webhook endpoint")]
    [EndpointDescription("Schedules a signed LocalProvider test delivery to a tenant-scoped outgoing webhook endpoint.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> TestEndpoint(
        Guid endpointId,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new TestWebhookEndpointCommand
            {
                EndpointId = endpointId,
                SourceTenantId = tenantContext.TenantId
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return EndpointFailures.Map(this, response);
        }

        return Ok(response);
    }
}
