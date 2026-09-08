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
[Route("api/webhooks/provider-publications")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class WebhookProviderPublicationsController(
    IMediator mediator,
    ITenantContext tenantContext,
    IResourceAssembler<WebhookProviderPublicationDto, WebhookProviderPublicationDto> assembler)
    : EventControllerBase
{
    [HttpGet(Name = RouteNames.GetWebhookProviderPublications)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get provider publications")]
    [EndpointDescription("Returns bounded tenant-scoped provider publication evidence with state-authorized HAL actions.")]
    [ProducesResponseType(typeof(HalCollectionResource<WebhookProviderPublicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalCollectionResource<WebhookProviderPublicationDto>>> GetPublications(
        [FromQuery] Guid? messageId = null,
        [FromQuery] Guid? consumerId = null,
        [FromQuery] int? statusId = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessageId = Normalize(messageId);
        var normalizedConsumerId = Normalize(consumerId);
        var publications = await mediator.Send(
            new GetWebhookProviderPublicationsQuery
            {
                TenantId = tenantContext.TenantId,
                WebhookMessageId = normalizedMessageId,
                WebhookConsumerId = normalizedConsumerId,
                StatusId = statusId,
                Limit = limit
            },
            cancellationToken);

        var resource = await assembler.ToCollectionResource(
            publications,
            RouteNames.GetWebhookProviderPublications,
            new { messageId = normalizedMessageId, consumerId = normalizedConsumerId, statusId, limit },
            HttpContext);
        return Ok(resource);
    }

    [HttpGet("{publicationId:guid}", Name = RouteNames.GetWebhookProviderPublicationById)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EndpointSummary("Get provider publication")]
    [EndpointDescription("Returns one tenant-scoped provider publication and its append-only safe attempt evidence.")]
    [ProducesResponseType(typeof(HalResource<WebhookProviderPublicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    public async Task<ActionResult<HalResource<WebhookProviderPublicationDto>>> GetPublication(
        Guid publicationId,
        CancellationToken cancellationToken = default)
    {
        var publication = await mediator.Send(
            new GetWebhookProviderPublicationByIdQuery
            {
                TenantId = tenantContext.TenantId,
                PublicationId = publicationId
            },
            cancellationToken);
        if (publication is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Provider publication not found",
                detail: "Provider publication was not found.");
        }

        return Ok(await assembler.ToResource(publication, HttpContext));
    }

    [HttpPost("{publicationId:guid}/reconcile", Name = RouteNames.ReconcileWebhookProviderPublication)]
    [EndpointSummary("Reconcile provider publication")]
    [EndpointDescription("Resolves manual publication uncertainty using an exact external provider message identifier.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Reconcile(
        Guid publicationId,
        [FromBody] ReconcileWebhookProviderPublicationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new ReconcileWebhookProviderPublicationCommand
            {
                TenantId = tenantContext.TenantId,
                PublicationId = publicationId,
                ActorUserId = RequiredUserId,
                ExpectedConcurrencyVersion = request.ExpectedConcurrencyVersion,
                ExternalProviderMessageId = request.ExternalProviderMessageId,
                ReasonCode = request.ReasonCode
            },
            cancellationToken);
        return ToOperationResult(response, "Provider publication cannot be reconciled");
    }

    [HttpPost("{publicationId:guid}/abandon", Name = RouteNames.AbandonWebhookProviderPublication)]
    [EndpointSummary("Abandon provider publication")]
    [EndpointDescription("Explicitly abandons manual-reconciliation or dead-lettered provider work.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Abandon(
        Guid publicationId,
        [FromBody] AbandonWebhookProviderPublicationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(
            new AbandonWebhookProviderPublicationCommand
            {
                TenantId = tenantContext.TenantId,
                PublicationId = publicationId,
                ActorUserId = RequiredUserId,
                ExpectedConcurrencyVersion = request.ExpectedConcurrencyVersion,
                ReasonCode = request.ReasonCode
            },
            cancellationToken);
        return ToOperationResult(response, "Provider publication cannot be abandoned");
    }

    private ActionResult<BaseCommandResponse<Guid>> ToOperationResult(
        BaseCommandResponse<Guid> response,
        string conflictTitle)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        if (response.FailureCode == "webhook_provider_publication_not_found")
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Provider publication not found",
                detail: response.Message);
        }

        if (response.FailureCode is "webhook_provider_publication_concurrency_conflict" or
            "webhook_provider_publication_not_reconcilable" or
            "webhook_provider_publication_not_abandonable")
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: conflictTitle,
                detail: response.Message);
        }

        return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["providerPublication"] = response.Errors?.ToArray() ?? []
        }));
    }

    private static Guid? Normalize(Guid? value) => value is { } id && id != Guid.Empty ? id : null;
}
