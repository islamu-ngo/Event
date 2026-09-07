using System.Text.Json;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Services;
using Explore.Application.Contracts.Services.Registration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/integrations/registration")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
[Produces(HateoasConstants.JsonMediaType)]
public sealed class RegistrationProviderCallbackController(
    IIncomingWebhookIntakeService incomingWebhookIntakeService) : EventControllerBase
{
    internal const string ProviderHeader = "X-Registration-Callback-Provider";
    internal const string BindingHeader = "X-Registration-Callback-BindingId";
    private const long MaxBodyBytes = 256 * 1024;

    [HttpPost("{provider}/{bindingId:guid}/callback", Name = RouteNames.RegistrationProviderCallback)]
    [EndpointSummary("Record Registration Provider Callback")]
    [EndpointDescription("Accepts signed provider-hosted registration submission callbacks and queues bounded durable processing.")]
    [AllowAnonymous]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [EnableRateLimiting(RateLimitingExtensions.PublicIngestionPolicy)]
    public async Task<IActionResult> RecordCallback(
        [FromRoute] string provider,
        [FromRoute] Guid bindingId,
        CancellationToken cancellationToken = default)
    {
        Request.Headers[ProviderHeader] = provider;
        Request.Headers[BindingHeader] = bindingId.ToString("D");

        IncomingWebhookReadResult incoming;
        try
        {
            incoming = await incomingWebhookIntakeService.ReadAndVerifyAsync(
                Request,
                RegistrationProviderIncomingWebhookVerifier.IntakeProvider,
                MaxBodyBytes,
                cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or FormatException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            return Accepted();
        }

        if (!incoming.Succeeded)
        {
            return incoming.StatusCode == StatusCodes.Status413PayloadTooLarge ? StatusCode(StatusCodes.Status413PayloadTooLarge) : Accepted();
        }

        IncomingWebhookCaptureResult capture = await incomingWebhookIntakeService.CaptureAsync(incoming, cancellationToken);
        return capture.StatusCode == StatusCodes.Status413PayloadTooLarge ? StatusCode(StatusCodes.Status413PayloadTooLarge) : Accepted();
    }
}
