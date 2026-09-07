using System.Text.Json;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Services;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Serialization;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Configuration;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/integrations/moderation")]
[ApiController]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class ModerationIntegrationController(
    IMediator mediator,
    IIncomingWebhookIntakeService incomingWebhookIntakeService,
    IOptionsMonitor<CoopProviderOptions> coopOptions,
    BusinessMetrics metrics,
    ILogger<ModerationIntegrationController> logger) : EventControllerBase
{
    [HttpPost("osprey/callback", Name = RouteNames.ModerationIntegrationOspreyCallback)]
    [EndpointSummary("Record Osprey Signal Callback")]
    [EndpointDescription("Accepts authenticated Osprey moderation signal callbacks and records provider signals on the local report.")]
    [Authorize(Policy = ModerationIntegrationAuthorizationPolicies.OspreyCallback)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RecordOspreyCallback(
        [FromBody] OspreySignalCallbackRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(new RecordOspreySignalCallbackCommand
        {
            Request = request
        }, cancellationToken);
        RecordCallback("osprey", request.TenantId, response);

        return response.IsSuccess ? Ok(response) : this.ToEventReportProblem(response);
    }

    [HttpPost("coop/callback", Name = RouteNames.ModerationIntegrationCoopCallback)]
    [EndpointSummary("Record Coop Decision Callback")]
    [EndpointDescription("Accepts signed Coop moderation decision callbacks and executes the local report decision flow.")]
    [Authorize(Policy = ModerationIntegrationAuthorizationPolicies.CoopCallback)]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RecordCoopCallback(
        CancellationToken cancellationToken = default)
    {
        var incoming = await incomingWebhookIntakeService.ReadAndVerifyAsync(
            Request,
            "coop",
            coopOptions.CurrentValue.WebhookMaxBodyBytes,
            cancellationToken);
        if (!incoming.Succeeded)
        {
            metrics.RecordEventReportProviderCallback("coop", "failed", incoming.Code);
            logger.LogWarning(
                "Coop moderation callback rejected with status {StatusCode} failure {FailureCategory}",
                incoming.StatusCode,
                incoming.Code);

            return ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateProblem(
                HttpContext,
                incoming.StatusCode,
                incoming.Title,
                incoming.Type,
                incoming.Detail,
                incoming.Code));
        }

        CoopDecisionCallbackRequestDto? request;
        try
        {
            request = JsonSerializer.Deserialize(
                incoming.RawPayload!,
                ExploreJsonContext.Default.CoopDecisionCallbackRequestDto);
        }
        catch (JsonException)
        {
            metrics.RecordEventReportProviderCallback("coop", "failed", "coop_webhook_json_invalid");
            logger.LogWarning("Coop moderation callback rejected because JSON parsing failed");

            return ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateValidationProblem(
                HttpContext,
                new ApiValidationProblemDescriptor("body", "Invalid Coop callback JSON", "The Coop callback body is invalid."),
                ["The Coop callback body could not be parsed as JSON."],
                "The Coop callback body could not be parsed as JSON.",
                "coop_webhook_json_invalid"));
        }

        if (request is null)
        {
            metrics.RecordEventReportProviderCallback("coop", "failed", "coop_webhook_body_required");
            logger.LogWarning("Coop moderation callback rejected because the body was empty");

            return ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateValidationProblem(
                HttpContext,
                new ApiValidationProblemDescriptor("body", "Invalid Coop callback JSON", "The Coop callback body is invalid."),
                ["The Coop callback body is required."],
                "The Coop callback body is required.",
                "coop_webhook_body_required"));
        }

        var capture = await incomingWebhookIntakeService.CaptureAsync(
            incoming,
            cancellationToken);
        if (!capture.Succeeded)
        {
            metrics.RecordEventReportProviderCallback(
                "coop",
                "failed",
                capture.Code);

            return ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateProblem(
                HttpContext,
                capture.StatusCode,
                capture.Title,
                capture.Type,
                capture.Detail,
                capture.Code));
        }

        if (capture.IsDuplicate)
        {
            var duplicateResponse = BaseCommandResponse.Success(
                capture.MessageId,
                "Coop decision callback was already captured.");
            RecordCallback("coop", incoming.Verification!.TenantId!.Value, duplicateResponse);
            return Ok(duplicateResponse);
        }

        var response = BaseCommandResponse.Success(
            capture.MessageId,
            "Coop decision callback was captured for durable processing.");

        RecordCallback("coop", incoming.Verification!.TenantId!.Value, response);

        return response.IsSuccess ? Ok(response) : this.ToEventReportProblem(response);
    }

    private void RecordCallback(string provider, Guid tenantId, BaseCommandResponse<Guid> response)
    {
        var outcome = response.IsSuccess ? "succeeded" : "failed";
        var failureCategory = response.FailureCode ?? "none";
        metrics.RecordEventReportProviderCallback(
            provider,
            outcome,
            failureCategory);
        logger.LogInformation(
            "Moderation provider callback {Provider} completed for report {ReportId} outcome {Outcome} failure {FailureCategory}",
            provider,
            response.Id,
            outcome,
            failureCategory);
    }

}
