using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Services;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/integrations")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class IncomingWebhooksController(
    IIncomingWebhookIntakeService incomingWebhookIntakeService,
    IOptionsMonitor<WebhookOptions> webhookOptions,
    BusinessMetrics metrics,
    ILogger<IncomingWebhooksController> logger) : EventControllerBase
{
    [HttpPost("svix/operational", Name = RouteNames.IntegrationSvixOperationalCallback)]
    [EndpointSummary("Record Svix Operational Webhook")]
    [EndpointDescription("Accepts signed Svix operational callbacks without requiring the outgoing webhook provider to be enabled.")]
    [AllowAnonymous]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting(RateLimitingExtensions.PublicIngestionPolicy)]
    public async Task<IActionResult> RecordSvixOperationalCallback(
        CancellationToken cancellationToken = default)
    {
        var incoming = await incomingWebhookIntakeService.ReadAndVerifyAsync(
            Request,
            "svix",
            webhookOptions.CurrentValue.Svix.OperationalWebhookMaxBodyBytes,
            cancellationToken);
        if (!incoming.Succeeded)
        {
            metrics.RecordEventReportProviderCallback("svix", "failed", incoming.Code);
            logger.LogWarning(
                "Svix operational webhook rejected with status {StatusCode} failure {FailureCategory}",
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

        var capture = await incomingWebhookIntakeService.CaptureAsync(incoming, cancellationToken);
        if (!capture.Succeeded)
        {
            metrics.RecordEventReportProviderCallback("svix", "failed", capture.Code);
            return ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateProblem(
                HttpContext,
                capture.StatusCode,
                capture.Title,
                capture.Type,
                capture.Detail,
                capture.Code));
        }

        metrics.RecordEventReportProviderCallback(
            "svix",
            "succeeded",
            "none");
        return Accepted();
    }

    [HttpPost("stripe/connect", Name = RouteNames.IntegrationStripeConnectCallback)]
    [EndpointSummary("Record Stripe Connect Webhook")]
    [EndpointDescription("Accepts signed Stripe Connect account and payment callbacks for durable asynchronous reconciliation.")]
    [AllowAnonymous]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting(RateLimitingExtensions.PublicIngestionPolicy)]
    public async Task<IActionResult> RecordStripeConnectCallback(
        CancellationToken cancellationToken = default)
    {
        var incoming = await incomingWebhookIntakeService.ReadAndVerifyAsync(
            Request,
            "stripe-connect",
            webhookOptions.CurrentValue.Stripe.ConnectWebhookMaxBodyBytes,
            cancellationToken);
        if (!incoming.Succeeded)
        {
            metrics.RecordEventReportProviderCallback("stripe-connect", "failed", incoming.Code);
            logger.LogWarning(
                "Stripe Connect webhook rejected with status {StatusCode} failure {FailureCategory}",
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

        var capture = await incomingWebhookIntakeService.CaptureAsync(incoming, cancellationToken);
        if (!capture.Succeeded)
        {
            metrics.RecordEventReportProviderCallback("stripe-connect", "failed", capture.Code);
            return ApiProblemFactory.ToProblemResult(ApiProblemFactory.CreateProblem(
                HttpContext,
                capture.StatusCode,
                capture.Title,
                capture.Type,
                capture.Detail,
                capture.Code));
        }

        metrics.RecordEventReportProviderCallback("stripe-connect", "succeeded", "none");
        return Accepted();
    }

}
