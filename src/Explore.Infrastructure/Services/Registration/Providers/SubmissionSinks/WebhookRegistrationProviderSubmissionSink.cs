// ABOUTME: Generic outbound webhook registration submission sink using approved mapped fields only.
// ABOUTME: Applies existing secret binding, endpoint safety, bounded payloads, and idempotency headers post-commit.

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;

public sealed class WebhookRegistrationProviderSubmissionSink(
    HttpClient httpClient,
    ISecretResolver secretResolver,
    WebhookEndpointSafetyPolicy endpointSafetyPolicy,
    IOptionsMonitor<WebhookOptions> webhookOptions,
    TimeProvider? timeProvider = null) : IRegistrationProviderDescriptor, IRegistrationProviderSubmissionSink
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public const string HttpClientName = "RegistrationProvider.WebhookSink";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RegistrationProviderTuple SupportedTuple { get; } = new(
        "WEBHOOK",
        "HTTPS",
        "v1",
        "ISLAMU_EVENT_APPROVED_FIELDS_WEBHOOK_V1",
        "2026-08-12");

    public RegistrationProviderTuple Tuple => SupportedTuple;

    public RegistrationProviderCapabilitySet ProvenCapabilities { get; } = new(
        false, false, false, false, false, false, false, false, false, false, true, false);

    public async Task<RegistrationProviderSubmissionSinkResult> AcceptAsync(
        RegistrationProviderSubmissionSinkRequest request,
        CancellationToken cancellationToken)
    {
        Uri endpoint = new(request.Connection.PublicBaseUrl);
        WebhookEndpointSafetyResult safety = await endpointSafetyPolicy.ValidateAsync(endpoint, cancellationToken);
        if (!safety.IsAllowed)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "provider_endpoint_blocked");
        }

        var payload = new
        {
            id = request.RegistrationSubmissionId,
            fields = request.Answers.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        byte[] body = Encoding.UTF8.GetBytes(json);
        if (body.Length > webhookOptions.CurrentValue.Local.MaxPayloadBytes)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "provider_submission_payload_too_large");
        }

        using HttpRequestMessage message = new(HttpMethod.Post, endpoint);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.TryAddWithoutValidation("Idempotency-Key", request.RegistrationSubmissionId.ToString("N"));
        message.Headers.TryAddWithoutValidation("X-Islamu-Registration-Submission-Id", request.RegistrationSubmissionId.ToString("D"));
        if ((request.Binding.WebhookSecretBindingId ?? request.Connection.WebhookSecretBindingId) is { } bindingId &&
            (await secretResolver.ResolveTenantBindingAsync(request.TenantId, bindingId, cancellationToken)).Value is { Length: > 0 } secret)
        {
            string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
            message.Headers.TryAddWithoutValidation("X-Islamu-Signature", "sha256=" + signature);
        }

        message.Content = new ByteArrayContent(body);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        if (request.DisclosureUntilUtc is { } deadline && _timeProvider.GetUtcNow().UtcDateTime >= deadline)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "registration_data_retention_expired");
        }

        using HttpResponseMessage response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new RegistrationProviderSubmissionSinkResult(true, request.RegistrationSubmissionId, false);
        }

        throw new RegistrationProviderSubmissionDeliveryException(
            (int)response.StatusCode is 408 or 429
                ? RegistrationProviderSubmissionDeliveryFailureKind.RetryableBeforeHandoff
                : (int)response.StatusCode >= 500
                    ? RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff
                    : RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
            (int)response.StatusCode >= 500 ? "provider_write_outcome_unknown" : "provider_write_rejected");
    }
}
