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

[ApiVersion("0.1")]
[Route("api/webhooks")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class WebhooksController(
    IMediator mediator,
    IWebhookOwnershipScopeResolver webhookOwnershipScopeResolver,
    IResourceAssembler<WebhookConsumerDto, WebhookConsumerDto> webhookConsumerAssembler)
    : WebhooksControllerBase(webhookOwnershipScopeResolver)
{
    private static readonly ApiValidationProblemDescriptor ConsumerValidationProblem = new(
        "webhookConsumer",
        "Webhook consumer validation failed",
        "Webhook consumer command failed.");

    private static readonly ApiValidationProblemDescriptor PortalValidationProblem = new(
        "webhookProviderPortal",
        "Webhook provider portal validation failed",
        "Webhook provider portal access could not be created.");

    private static readonly ApiValidationProblemDescriptor ProviderBindingValidationProblem = new(
        "webhookProviderBinding",
        "Webhook provider binding repair validation failed",
        "Webhook provider binding could not be repaired.");

    private static readonly ApiNotFoundProblemDescriptor ConsumerNotFoundProblem = new(
        "Webhook consumer not found",
        "Webhook consumer was not found.",
        "webhook_consumer_not_found");

    private static readonly CommandFailurePolicy ConsumerFailures = CommandFailurePolicy
        .ValidatedBy(ConsumerValidationProblem)
        .NotFound(ConsumerNotFoundProblem, "webhook_consumer_not_found")
        .Conflict(
            "Webhook consumer configuration conflict",
            "Webhook consumer configuration could not be changed.",
            "webhook_consumer_name_conflict",
            "webhook_consumer_configuration_conflict",
            "webhook_consumer_provider_mode_unchanged",
            "webhook_consumer_provider_mode_unavailable",
            "webhook_consumer_provider_mode_local_target_required",
            "webhook_consumer_provider_mode_binding_required",
            "webhook_consumer_provider_mode_pending_migration_unsupported",
            "webhook_consumer_provider_mode_uncertain_publications");

    private static readonly CommandFailurePolicy ProviderBindingRepairFailures = CommandFailurePolicy
        .ValidatedBy(ProviderBindingValidationProblem)
        .NotFound(ConsumerNotFoundProblem, "webhook_consumer_not_found")
        .Conflict(
            "Webhook provider binding cannot be repaired",
            "Webhook provider binding repair is unavailable.",
            "webhook_provider_binding_mismatched",
            "webhook_provider_application_not_found",
            "webhook_provider_binding_repair_not_available",
            "webhook_provider_binding_repair_conflict")
        .Unavailable(
            "Webhook provider binding repair unavailable",
            "Webhook provider binding repair is temporarily unavailable.",
            "svix_provider_not_enabled",
            "svix_self_hosted_profile_unsupported",
            "webhook_provider_profile_unavailable",
            "webhook_instance_identity_unavailable",
            "svix_auth_token_secret_missing",
            "svix_auth_token_unresolved",
            "svix_auth_failed",
            "svix_provider_unavailable");

    [HttpGet("event-types", Name = RouteNames.GetWebhookEventTypes)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook event types")]
    [EndpointDescription("Returns the canonical outgoing webhook event catalog with schema and example payload metadata.")]
    [OutputCache(PolicyName = "LookupData")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookEventTypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WebhookEventTypeDto>>> GetEventTypes(
        CancellationToken cancellationToken = default)
    {
        var eventTypes = await mediator.Send(new GetWebhookEventTypesQuery(), cancellationToken);
        return Ok(eventTypes);
    }

    [HttpGet("consumers", Name = RouteNames.GetWebhookConsumers)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook consumers")]
    [EndpointDescription("Returns outgoing webhook consumers for one typed owner with HAL management affordances.")]
    [OutputCache(PolicyName = "ListData")]
    [ProducesResponseType(typeof(HalCollectionResource<WebhookConsumerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<WebhookConsumerDto>>> GetConsumers(
        [FromQuery] int ownerKindId,
        [FromQuery] Guid? ownerId = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var consumers = await mediator.Send(
            new GetWebhookConsumersQuery
            {
                OwnerKindId = ownerKindId,
                OwnerId = ownerId,
                Limit = limit
            },
            cancellationToken);

        var resource = await webhookConsumerAssembler.ToCollectionResource(
            consumers,
            RouteNames.GetWebhookConsumers,
            await CreateCollectionRouteValuesAsync(ownerKindId, ownerId, limit, cancellationToken),
            HttpContext);

        return Ok(resource);
    }

    [HttpGet("consumers/{consumerId:guid}", Name = RouteNames.GetWebhookConsumerById)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get webhook consumer")]
    [EndpointDescription("Returns one owner-authorized outgoing webhook consumer with HAL management affordances.")]
    [ProducesResponseType(typeof(HalResource<WebhookConsumerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalResource<WebhookConsumerDto>>> GetConsumer(
        Guid consumerId,
        CancellationToken cancellationToken = default)
    {
        var consumer = await mediator.Send(
            new GetWebhookConsumerByIdQuery
            {
                ConsumerId = consumerId
            },
            cancellationToken);

        if (consumer is null)
        {
            return this.ToNotFoundProblem(ConsumerNotFoundProblem);
        }

        var resource = await webhookConsumerAssembler.ToResource(consumer, HttpContext);
        return Ok(resource);
    }

    [HttpPost("consumers", Name = RouteNames.CreateWebhookConsumer)]
    [EndpointSummary("Create webhook consumer")]
    [EndpointDescription("Creates a tenant-scoped outgoing webhook consumer for Local, Svix, Composite, DryRun, or Disabled provider modes.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateConsumer(
        [FromBody] CreateWebhookConsumerRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new CreateWebhookConsumerCommand
            {
                OwnerId = request.OwnerId,
                ConsumerKindId = request.ConsumerKindId,
                Name = request.Name,
                ProviderModeId = request.ProviderModeId
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return ConsumerFailures.Map(this, response);
        }

        return CreatedAtRoute(
            RouteNames.GetWebhookConsumerById,
            new { consumerId = response.Id },
            response);
    }

    [HttpPatch("consumers/{consumerId:guid}/provider-mode", Name = RouteNames.UpdateWebhookConsumerProviderMode)]
    [EndpointSummary("Change webhook consumer provider mode")]
    [EndpointDescription("Changes the provider mode for new deliveries while preserving already materialized work on its immutable delivery snapshots.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateConsumerProviderMode(
        Guid consumerId,
        [FromBody] UpdateWebhookConsumerProviderModeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProviderMode is null)
        {
            return this.ToValidationProblem(ConsumerValidationProblem, "ProviderMode group is required.");
        }

        var providerMode = request.ProviderMode;
        var response = await mediator.Send(
            new UpdateWebhookConsumerProviderModeCommand
            {
                ConsumerId = consumerId,
                ProviderModeId = providerMode.ProviderModeId,
                ExpectedConfigurationVersion = providerMode.ExpectedConfigurationVersion,
                PendingWorkDecisionId = providerMode.PendingWorkDecisionId,
                PendingWorkReason = providerMode.PendingWorkReason,
                AcknowledgeUncertainProviderPublications = providerMode.AcknowledgeUncertainProviderPublications
            },
            cancellationToken);

        return response.IsSuccess ? Ok(response) : ConsumerFailures.Map(this, response);
    }

    [HttpPost("consumers/{consumerId:guid}/provider-binding/repair", Name = RouteNames.RepairWebhookProviderBinding)]
    [EndpointSummary("Repair webhook provider binding")]
    [EndpointDescription("Verifies self-hosted provider ownership and atomically creates or rebinds the consumer application mapping.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RepairProviderBinding(
        Guid consumerId,
        [FromBody] RepairWebhookProviderBindingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new RepairWebhookProviderBindingCommand
            {
                ConsumerId = consumerId,
                ExternalApplicationId = request.ExternalApplicationId,
                ReasonCode = request.ReasonCode
            },
            cancellationToken);

        return response.IsSuccess
            ? Ok(response)
            : ProviderBindingRepairFailures.Map(this, response);
    }








    [HttpPost("svix/app-portal", Name = RouteNames.OpenSvixAppPortal)]
    [EndpointSummary("Open Svix App Portal")]
    [EndpointDescription("Creates a short-lived Svix App Portal URL for a verified webhook consumer binding.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(WebhookProviderPortalAccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<WebhookProviderPortalAccessDto>> OpenSvixAppPortal(
        [FromBody] OpenSvixAppPortalRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new OpenSvixAppPortalCommand
            {
                ConsumerId = request.ConsumerId,
                SessionId = ResolvePortalSessionId(),
                ExpiresInSeconds = request.ExpiresInSeconds
            },
            cancellationToken);

        if (result.IsSuccess && result.Id is not null)
        {
            return Ok(result.Id);
        }

        return ToWebhookPortalProblem(result);
    }









    private string ResolvePortalSessionId()
    {
        return User.GetProviderSubject()
            ?? CurrentUserId?.ToString("D")
            ?? string.Empty;
    }

    private ActionResult<WebhookProviderPortalAccessDto> ToWebhookPortalProblem(
        WebhookProviderPortalAccessCommandResponse response)
    {
        var code = string.IsNullOrWhiteSpace(response.FailureCode)
            ? "webhook_provider_portal_failed"
            : response.FailureCode;
        var detail = response.Message ?? "Webhook provider portal access could not be created.";

        return code switch
        {
            "webhook_portal_validation_failed" or "webhook_portal_session_required" =>
                ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateValidationProblem(
                    HttpContext,
                    PortalValidationProblem,
                    response.Errors ?? [detail],
                    detail,
                    code)),

            "webhook_consumer_not_found" =>
                ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateNotFoundProblem(
                    HttpContext,
                    ConsumerNotFoundProblem)),

            "webhook_consumer_disabled"
                or "webhook_provider_binding_unverified"
                or "webhook_provider_binding_mismatched"
                or "webhook_provider_capability_unavailable" =>
                ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateConflictProblem(
                    HttpContext,
                    "Webhook provider portal unavailable",
                    detail,
                    code)),

            "svix_provider_not_enabled"
                or "svix_app_portal_disabled"
                or "svix_auth_token_secret_missing"
                or "svix_auth_token_unresolved" =>
                ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateServiceUnavailableProblem(
                    HttpContext,
                    "Webhook provider portal unavailable",
                    detail,
                    code)),

            _ when response.IsRetryable =>
                ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateServiceUnavailableProblem(
                    HttpContext,
                    "Webhook provider temporarily unavailable",
                    detail,
                    code)),

            _ =>
                ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateBadGatewayProblem(
                    HttpContext,
                    "Webhook provider request failed",
                    detail,
                    code))
        };
    }

}
