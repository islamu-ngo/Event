namespace Explore.Domain;

[Flags]
public enum WebhookProviderCapability : long
{
    None = 0,
    EndpointManagement = 1L << 0,
    ProviderAttemptVisibility = 1L << 1,
    Replay = 1L << 2,
    PayloadInspection = 1L << 3,
    AppPortal = 1L << 4,
    EventCatalog = 1L << 5,
    ProviderRetentionControl = 1L << 6,
    ApplicationThrottling = 1L << 7,
    EndpointThrottling = 1L << 8,
    Transformations = 1L << 9,
    Ordering = 1L << 10,
    OperationalCallbacks = 1L << 11,
    All = EndpointManagement |
          ProviderAttemptVisibility |
          Replay |
          PayloadInspection |
          AppPortal |
          EventCatalog |
          ProviderRetentionControl |
          ApplicationThrottling |
          EndpointThrottling |
          Transformations |
          Ordering |
          OperationalCallbacks
}

public sealed record WebhookProviderCapabilityProfile
{
    public WebhookProviderKind ProviderKind { get; }
    public string ProviderVersion { get; }
    public WebhookProviderCapability Capabilities { get; }
    public string ResolutionVersion { get; }
    public DateTimeOffset ResolvedAtUtc { get; }

    private WebhookProviderCapabilityProfile(
        WebhookProviderKind providerKind,
        string providerVersion,
        WebhookProviderCapability capabilities,
        string resolutionVersion,
        DateTimeOffset resolvedAtUtc)
    {
        ProviderKind = providerKind;
        ProviderVersion = providerVersion;
        Capabilities = capabilities;
        ResolutionVersion = resolutionVersion;
        ResolvedAtUtc = resolvedAtUtc;
    }

    public static WebhookProviderCapabilityProfile Create(
        WebhookProviderKind providerKind,
        string providerVersion,
        WebhookProviderCapability capabilities,
        string resolutionVersion,
        DateTimeOffset resolvedAtUtc)
    {
        if (!Enum.IsDefined(providerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(providerKind), "Webhook provider kind is not valid.");
        }

        EnsureKnownCapabilities(capabilities, nameof(capabilities));

        if (resolvedAtUtc == default)
        {
            throw new ArgumentException("Capability resolution time is required.", nameof(resolvedAtUtc));
        }

        return new WebhookProviderCapabilityProfile(
            providerKind,
            NormalizeRequired(providerVersion, nameof(providerVersion)),
            capabilities,
            NormalizeRequired(resolutionVersion, nameof(resolutionVersion)),
            resolvedAtUtc);
    }

    public bool Supports(WebhookProviderCapability capability)
    {
        EnsureKnownCapabilities(capability, nameof(capability));
        return capability != WebhookProviderCapability.None &&
               (Capabilities & capability) == capability;
    }

    internal static void EnsureKnownCapabilities(
        WebhookProviderCapability capabilities,
        string parameterName)
    {
        if ((capabilities & ~WebhookProviderCapability.All) != 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Webhook provider capabilities contain unknown flags.");
        }
    }

    internal static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }
}
