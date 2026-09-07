using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
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
[Route("api/admin/incoming-webhook-effects")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class IncomingWebhookEffectsAdminController(
    IMediator mediator,
    IResourceAssembler<IncomingWebhookEffectStatusDto, IncomingWebhookEffectStatusDto> statusAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor EffectValidationProblem = new(
        "incomingWebhookEffect",
        "Incoming Coop effect request failed",
        "The incoming Coop effect request was invalid.");

    [HttpGet("status", Name = RouteNames.GetIncomingWebhookEffectStatus)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [ProducesResponseType(typeof(HalCollectionResource<IncomingWebhookEffectStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<IncomingWebhookEffectStatusDto>>> GetStatus(
        [FromQuery] Guid tenantId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetIncomingWebhookEffectStatusQuery { TenantId = tenantId, Limit = limit },
            cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToCommandValidationProblem(result, EffectValidationProblem);
        }

        var resource = statusAssembler.ToCollectionResource(
            result.Id ?? [],
            RouteNames.GetIncomingWebhookEffectStatus,
            new { tenantId, limit },
            HttpContext);
        return Ok(resource);
    }

    [HttpPost("tenants/{tenantId:guid}/{effectOutboxId:guid}/redrive", Name = RouteNames.RedriveIncomingWebhookEffect)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Redrive(
        Guid tenantId,
        Guid effectOutboxId,
        [FromBody] RedriveIncomingWebhookRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new RedriveIncomingWebhookEffectCommand
            {
                TenantId = tenantId,
                EffectOutboxId = effectOutboxId,
                ExpectedProcessingGeneration = request.ExpectedProcessingGeneration,
                Reason = request.Reason
            },
            cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(result);
        }

        return result.FailureCode switch
        {
            "incoming_webhook_effect_not_found" => NotFound(result),
            "incoming_webhook_effect_redrive_generation_conflict" or
            "incoming_webhook_effect_redrive_not_eligible" or
            "incoming_webhook_effect_redrive_payload_unavailable" => Conflict(result),
            _ => this.ToCommandValidationProblem(result, EffectValidationProblem)
        };
    }
}
