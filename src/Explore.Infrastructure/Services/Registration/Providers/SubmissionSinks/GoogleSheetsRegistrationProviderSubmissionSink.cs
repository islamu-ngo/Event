// ABOUTME: Google Sheets approved-field submission sink for post-commit registration delivery.
// ABOUTME: Uses existing provider secret binding and bounded JSON append payloads without logging answers or credentials.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;

namespace Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;

public sealed class GoogleSheetsRegistrationProviderSubmissionSink(
    HttpClient httpClient,
    ISecretResolver secretResolver,
    TimeProvider? timeProvider = null) : IRegistrationProviderDescriptor, IRegistrationProviderSubmissionSink
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public const string HttpClientName = "RegistrationProvider.GoogleSheetsSink";
    private const int MaxPayloadBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RegistrationProviderTuple SupportedTuple { get; } = new(
        "GOOGLE_SHEETS",
        "GOOGLE_WORKSPACE",
        "v4",
        "ISLAMU_EVENT_APPROVED_FIELDS_SHEETS_V1",
        "2026-08-12");

    public RegistrationProviderTuple Tuple => SupportedTuple;

    public RegistrationProviderCapabilitySet ProvenCapabilities { get; } = new(
        false, false, false, false, false, false, false, false, false, false, true, false);

    public async Task<RegistrationProviderSubmissionSinkResult> AcceptAsync(
        RegistrationProviderSubmissionSinkRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(new Uri(request.Connection.ManagementApiBaseUrl).Host, "sheets.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
            request.Connection.ApiTokenSecretBindingId is not { } bindingId ||
            string.IsNullOrWhiteSpace(request.Binding.ProviderSurveyId))
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "provider_connection_invalid");
        }

        string token = (await secretResolver.ResolveTenantBindingAsync(request.TenantId, bindingId, cancellationToken)).Value?.Trim() ?? string.Empty;
        if (token.Length == 0)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.RetryableBeforeHandoff,
                "provider_credentials_unavailable");
        }

        string[] keys = [.. request.Answers.Keys.Order(StringComparer.Ordinal)];
        var payload = new { values = new[] { keys.Select(key => request.Answers[key]).ToArray() } };
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "provider_submission_payload_too_large");
        }

        string range = string.IsNullOrWhiteSpace(request.Connection.ProviderWorkspaceId)
            ? "Sheet1!A:ZZ"
            : request.Connection.ProviderWorkspaceId;
        using HttpRequestMessage message = new(
            HttpMethod.Post,
            new Uri($"{request.Connection.ManagementApiBaseUrl.TrimEnd('/')}/spreadsheets/{Uri.EscapeDataString(request.Binding.ProviderSurveyId!)}/values/{Uri.EscapeDataString(range)}:append?valueInputOption=RAW&insertDataOption=INSERT_ROWS"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.TryAddWithoutValidation("Idempotency-Key", request.RegistrationSubmissionId.ToString("N"));
        message.Content = new StringContent(json, Encoding.UTF8, "application/json");

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
