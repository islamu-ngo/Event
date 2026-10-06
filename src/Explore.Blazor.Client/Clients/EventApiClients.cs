using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Explore.Blazor.Client.Clients;

public sealed class GuestRegistrationOrderStartResult
{
    internal GuestRegistrationOrderStartResult(GuestRegistrationOrderStartDto response, string capability)
    {
        Response = response;
        Capability = capability;
    }

    public GuestRegistrationOrderStartDto Response { get; }
    public bool HasCapability => !string.IsNullOrWhiteSpace(Capability);
    internal string Capability { get; }
}

public partial interface IEventLifecycleClient
{
    Task<BaseCommandResponseOfGuid> CreateEventWithIdempotencyKeyAsync(
        CreateEventDraftRequestDto body,
        string idempotencyKey,
        string? apiVersion = null,
        string? xApiVersion = null,
        CancellationToken cancellationToken = default);
}

public partial interface IGuestRegistrationOrderClient
{
    Task<GuestRegistrationOrderStartResult> StartGuestRegistrationOrderWithCapabilityAsync(
        Guid eventId,
        StartRegistrationOrderRequest body,
        string idempotencyKey,
        string protectedChallenge,
        string nonce,
        CancellationToken cancellationToken = default);
}

public partial class EventLifecycleClient
{
    public async Task<BaseCommandResponseOfGuid> CreateEventWithIdempotencyKeyAsync(
        CreateEventDraftRequestDto body,
        string idempotencyKey,
        string? apiVersion = null,
        string? xApiVersion = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = EventApiTransportBehavior.BeginCreateEvent(idempotencyKey);
        return await CreateEventAsync(body, apiVersion, xApiVersion, cancellationToken);
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url) =>
        EventApiTransportBehavior.PrepareRequest(request, url);

    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) =>
        EventApiTransportBehavior.ProcessResponse(response.RequestMessage, response);
}

public partial class GuestRegistrationOrderClient
{
    public async Task<GuestRegistrationOrderStartResult> StartGuestRegistrationOrderWithCapabilityAsync(
        Guid eventId,
        StartRegistrationOrderRequest body,
        string idempotencyKey,
        string protectedChallenge,
        string nonce,
        CancellationToken cancellationToken = default)
    {
        using var operation = EventApiTransportBehavior.BeginGuestRegistrationOrder(idempotencyKey);
        var response = await StartGuestRegistrationOrderAsync(
            eventId,
            idempotency_Key: operation.IdempotencyKey,
            x_Registration_Challenge: protectedChallenge,
            x_Registration_Proof: nonce,
            body: body,
            cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(operation.Capability))
        {
            throw new InvalidOperationException("Guest registration capability was not returned.");
        }

        return new GuestRegistrationOrderStartResult(response, operation.Capability);
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url) =>
        EventApiTransportBehavior.PrepareRequest(request, url);

    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) =>
        EventApiTransportBehavior.ProcessResponse(response.RequestMessage, response);
}

public partial class GuestRegistrationOrderPaymentClient
{
    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url) =>
        EventApiTransportBehavior.PrepareRequest(request, url);

    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) =>
        EventApiTransportBehavior.ProcessResponse(response.RequestMessage, response);
}

public partial class AuthenticatedRegistrationOrderPaymentClient
{
    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url) =>
        EventApiTransportBehavior.PrepareRequest(request, url);

    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) =>
        EventApiTransportBehavior.ProcessResponse(response.RequestMessage, response);
}

public partial class StudioRegistrationOrderPaymentClient
{
    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url) =>
        EventApiTransportBehavior.PrepareRequest(request, url);

    partial void ProcessResponse(HttpClient client, HttpResponseMessage response) =>
        EventApiTransportBehavior.ProcessResponse(response.RequestMessage, response);
}

public static class EventApiJsonSerializerSettings
{
    /// <summary>Preserves native protocol enum tokens and required issuance fields at generated-client ingestion.</summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions settings)
    {
        settings.Converters.Add(new SetupEnrollmentScopeJsonConverter());
        settings.Converters.Add(new JsonStringEnumConverter<ExternalApiKeyDisclosureStatus>(allowIntegerValues: false));
        settings.Converters.Add(new JsonStringEnumConverter());
        settings.TypeInfoResolver = (settings.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(typeInfo =>
            {
                if (typeInfo.Type != typeof(ExternalApiKeyIssuanceDto))
                    return;
                // NSwag does not emit JsonRequired for nonnullable enums, so an absent
                // status otherwise defaults to Issued. Presence and valid-state checks
                // are separate: explicit null apiKey is required for metadata recovery.
                foreach (var property in typeInfo.Properties)
                {
                    if (property.Name is "id" or "keyId" or "disclosureStatus" or "apiKey")
                        property.IsRequired = true;
                    // Generated property converters take precedence over options converters.
                    if (property.Name == "disclosureStatus")
                        property.CustomConverter = new JsonStringEnumConverter<ExternalApiKeyDisclosureStatus>(
                            allowIntegerValues: false);
                }
            });
        return settings;
    }

    private sealed class SetupEnrollmentScopeJsonConverter : JsonConverter<SetupEnrollmentScope>
    {
        public override SetupEnrollmentScope Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) => reader.GetString() switch
            {
                "target.read" => SetupEnrollmentScope.Target_read,
                "secret_binding.readiness" => SetupEnrollmentScope.Secret_binding_readiness,
                "secret_binding.write" => SetupEnrollmentScope.Secret_binding_write,
                "configuration.import" => SetupEnrollmentScope.Configuration_import,
                _ => throw new JsonException("Invalid Setup enrollment scope.")
            };

        public override void Write(
            Utf8JsonWriter writer,
            SetupEnrollmentScope value,
            JsonSerializerOptions options) => writer.WriteStringValue(value switch
            {
                SetupEnrollmentScope.Target_read => "target.read",
                SetupEnrollmentScope.Secret_binding_readiness => "secret_binding.readiness",
                SetupEnrollmentScope.Secret_binding_write => "secret_binding.write",
                SetupEnrollmentScope.Configuration_import => "configuration.import",
                _ => throw new JsonException("Invalid Setup enrollment scope.")
            });
    }
}

internal static class EventApiTransportBehavior
{
    private static readonly AsyncLocal<OperationContext?> CurrentOperation = new();

    internal static OperationScope BeginCreateEvent(string idempotencyKey) =>
        Begin(new OperationContext(idempotencyKey, captureCapability: false));

    internal static OperationScope BeginGuestRegistrationOrder(string idempotencyKey) =>
        Begin(new OperationContext(idempotencyKey, captureCapability: true));

    internal static void PrepareRequest(HttpRequestMessage request, string? generatedUrl = null)
    {
        var operation = CurrentOperation.Value;
        if (IsAnonymousRegistrationIntentRequest(request, generatedUrl))
        {
            // New guest intent has no prior-order capability scope. Run again in the message handler
            // after HttpClient defaults are applied, identically for issuance and submission.
            request.Headers.Remove("X-Registration-Order-Capability");
            request.Headers.Remove("X-Registration-Attempt-Capability");
        }
        if (request.Method == HttpMethod.Post
            && operation is { CaptureCapability: false }
            && !request.Headers.Contains("Idempotency-Key"))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", operation.IdempotencyKey);
        }

        if (request.Method != HttpMethod.Get
            && IsGuestRegistrationOrderRequest(request, generatedUrl)
            && !request.Headers.Contains("Idempotency-Key"))
        {
            request.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                operation?.IdempotencyKey ?? Guid.CreateVersion7().ToString("N"));
        }

        if (request.Method == HttpMethod.Post
            && IsRegistrationProviderAttemptRequest(request, generatedUrl)
            && !request.Headers.Contains("Idempotency-Key"))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        }
    }

    internal static void ProcessResponse(HttpRequestMessage? request, HttpResponseMessage response)
    {
        var operation = CurrentOperation.Value;
        if (operation is not { CaptureCapability: true }
            || request?.Method != HttpMethod.Post
            || !response.Headers.TryGetValues("X-Registration-Order-Capability", out var values))
        {
            return;
        }

        operation.Capability = values.FirstOrDefault();
    }

    private static OperationScope Begin(OperationContext operation)
    {
        var previous = CurrentOperation.Value;
        CurrentOperation.Value = operation;
        return new OperationScope(operation, previous);
    }

    internal static bool IsAnonymousRegistrationIntentRequest(HttpRequestMessage request, string? generatedUrl = null)
    {
        if (request.Method != HttpMethod.Post) return false;
        var segments = GetPathSegments(request, generatedUrl);
        return (segments is ["api", "events", _, "guest-registration-challenges"]
                or ["api", "events", _, "registration-orders", "guest"])
            && Guid.TryParse(segments[2], out _);
    }

    private static bool IsGuestRegistrationOrderRequest(HttpRequestMessage request, string? generatedUrl) =>
        GetPathSegments(request, generatedUrl)
            .Zip(GetPathSegments(request, generatedUrl).Skip(1))
            .Any(pair => pair.First.Equals("registration-orders", StringComparison.OrdinalIgnoreCase)
                && pair.Second.Equals("guest", StringComparison.OrdinalIgnoreCase));

    private static bool IsRegistrationProviderAttemptRequest(HttpRequestMessage request, string? generatedUrl) =>
        GetPathSegments(request, generatedUrl).LastOrDefault()
            ?.Equals("provider-attempts", StringComparison.OrdinalIgnoreCase) == true;

    private static string[] GetPathSegments(HttpRequestMessage request, string? generatedUrl)
    {
        var path = request.RequestUri?.IsAbsoluteUri == true
            ? request.RequestUri.AbsolutePath
            : request.RequestUri?.OriginalString ?? generatedUrl ?? string.Empty;
        return path.Split('?', 2)[0]
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal sealed class OperationContext(string idempotencyKey, bool captureCapability)
    {
        public string IdempotencyKey { get; } = idempotencyKey;
        public bool CaptureCapability { get; } = captureCapability;
        public string? Capability { get; set; }
    }

    internal sealed class OperationScope(OperationContext operation, OperationContext? previous) : IDisposable
    {
        public string IdempotencyKey => operation.IdempotencyKey;
        public string? Capability => operation.Capability;

        public void Dispose() => CurrentOperation.Value = previous;
    }
}
