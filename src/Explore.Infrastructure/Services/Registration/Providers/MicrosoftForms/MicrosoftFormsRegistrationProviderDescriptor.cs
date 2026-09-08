using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;

namespace Explore.Infrastructure.Services.Registration.Providers.MicrosoftForms;

public sealed class MicrosoftFormsRegistrationProviderDescriptor(
    ISecretResolver secretResolver,
    TimeProvider timeProvider) :
    IRegistrationProviderDescriptor,
    IRegistrationProviderPresentation,
    IRegistrationProviderCallbackVerifier,
    IRegistrationProviderDelegatedAutomation
{
    public const string ProviderCode = "MICROSOFT_FORMS";
    public const string ContractVersion = "POWER_AUTOMATE_V1";
    public const string CorrelationPlatformFieldKey = "system.registration_attempt_token";
    public const string CallbackKeyHeader = "X-ISLAMU-Event-Callback-Key";

    public RegistrationProviderTuple Tuple { get; } = new(
        ProviderCode,
        "MICROSOFT_365",
        ContractVersion,
        "ISLAMU_EVENT_MICROSOFT_FORMS_V1",
        "2026-08-11");

    public RegistrationProviderCapabilitySet ProvenCapabilities { get; } = new(
        Redirect: true,
        Embed: true,
        Manual: true,
        SchemaRead: false,
        FormProvision: false,
        SubmissionWrite: false,
        SubmissionRead: false,
        CallbackVerification: true,
        SubscriptionManagement: false,
        Reconciliation: false,
        SubmissionSink: false,
        AutoFinalize: true);

    public string ConnectorContractVersion => ContractVersion;
    public string RequiredCorrelationPlatformFieldKey => CorrelationPlatformFieldKey;

    public Task<RegistrationProviderPresentationResult> GetPresentationAsync(
        RegistrationProviderPresentationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RegistrationProviderFieldMapping? correlation = request.Binding.FieldMappings.SingleOrDefault(mapping =>
            !mapping.IsDeleted && string.Equals(mapping.PlatformFieldKey, CorrelationPlatformFieldKey, StringComparison.Ordinal));
        if (request.AttemptId is null || string.IsNullOrWhiteSpace(request.AttemptCapabilityToken) ||
            string.IsNullOrWhiteSpace(request.Binding.ProviderSurveyId) || correlation is null)
        {
            return Task.FromResult(new RegistrationProviderPresentationResult(false, false, true));
        }

        UriBuilder builder = new(request.Connection.PublicBaseUrl);
        string correlationValue = $"{request.AttemptId.Value:D}|{request.AttemptCapabilityToken}";
        builder.Query = "id=" + Uri.EscapeDataString(request.Binding.ProviderSurveyId) + "&" +
            Uri.EscapeDataString(correlation.ProviderFieldKey) + "=" + Uri.EscapeDataString(correlationValue);
        Uri uri = builder.Uri;
        return Task.FromResult(new RegistrationProviderPresentationResult(true, true, true, uri, uri));
    }

    public async Task<RegistrationProviderCallbackVerificationResult> VerifyCallbackAsync(
        RegistrationProviderCallbackVerificationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetHeader(request.Headers, CallbackKeyHeader, out string? suppliedKey) ||
            request.Binding.WebhookSecretBindingId is not { } secretBindingId ||
            await secretResolver.ResolveTenantBindingAsync(request.TenantId, secretBindingId, cancellationToken) is not { Value: { } expectedKey } ||
            !FixedTimeEquals(suppliedKey, expectedKey))
        {
            return new(false, "microsoft_forms_callback_key_invalid");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(request.Body);
            JsonElement root = document.RootElement;
            if (!StringEquals(root, "providerCode", ProviderCode) ||
                !GuidEquals(root, "bindingId", request.Binding.Id) ||
                !StringEquals(root, "formId", request.Binding.ProviderSurveyId) ||
                !StringEquals(root, "contractVersion", ContractVersion) ||
                RequiredString(root, "responseId") is not { } responseId ||
                RequiredString(root, "idempotencyKey") is not { } idempotencyKey ||
                RequiredString(root, "attemptToken") is null ||
                !root.TryGetProperty("attemptId", out JsonElement attemptId) || !attemptId.TryGetGuid(out Guid parsedAttemptId) || parsedAttemptId == Guid.Empty ||
                !root.TryGetProperty("timestamp", out JsonElement timestamp) || timestamp.ValueKind != JsonValueKind.String ||
                !timestamp.TryGetDateTimeOffset(out DateTimeOffset receivedAt) || Duration(timeProvider.GetUtcNow(), receivedAt) > TimeSpan.FromMinutes(5) ||
                !root.TryGetProperty("mappedValues", out JsonElement mappedValues) || mappedValues.ValueKind != JsonValueKind.Object ||
                !string.Equals(idempotencyKey, $"{request.Binding.ProviderSurveyId}:{responseId}", StringComparison.Ordinal))
            {
                return new(false, "microsoft_forms_callback_envelope_invalid");
            }

            return new(true, Receipt: "microsoft-forms:power-automate:v1", ProviderSubmissionId: responseId);
        }
        catch (JsonException)
        {
            return new(false, "microsoft_forms_callback_envelope_invalid");
        }
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string? value)
    {
        foreach ((string key, string candidate) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }
        value = null;
        return false;
    }

    private static bool FixedTimeEquals(string supplied, string expected)
    {
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static bool StringEquals(JsonElement root, string name, string? expected) =>
        RequiredString(root, name) is { } value && string.Equals(value, expected, StringComparison.Ordinal);

    private static bool GuidEquals(JsonElement root, string name, Guid expected) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetGuid(out Guid parsed) && parsed == expected;

    private static string? RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String &&
        value.GetString()?.Trim() is { Length: > 0 and <= 200 } text ? text : null;

    private static TimeSpan Duration(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left - right : right - left;
}
