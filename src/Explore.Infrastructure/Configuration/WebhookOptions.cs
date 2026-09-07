using Explore.Domain.Secrets;
using Explore.Infrastructure.Webhooks;

namespace Explore.Infrastructure.Configuration;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";
    public const string ProviderDisabled = "Disabled";
    public const string ProviderLocal = "Local";
    public const string ProviderSvix = "Svix";
    public const string ProviderComposite = "Composite";
    public const string ProviderDryRun = "DryRun";

    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = ProviderLocal;
    public bool AllowTenantOverride { get; set; } = true;
    public int DefaultPayloadRetentionDays { get; set; } = 14;
    public WebhookLocalOptions Local { get; set; } = new();
    public WebhookSvixOptions Svix { get; set; } = new();
    public WebhookStripeOptions Stripe { get; set; } = new();

    public bool IsDisabled => !Enabled || IsProvider(ProviderDisabled);
    public bool IsProvider(string provider) =>
        string.Equals(Provider?.Trim(), provider, StringComparison.OrdinalIgnoreCase);
}

public sealed class WebhookLocalOptions
{
    public int MaxAttempts { get; set; } = 8;
    public int InitialRetryDelaySeconds { get; set; } = 30;
    public int MaxRetryDelaySeconds { get; set; } = 24 * 60 * 60;
    public int MaxRetryAfterSeconds { get; set; } = 60 * 60;
    public int TimeoutSeconds { get; set; } = 15;
    public int ConnectTimeoutSeconds { get; set; } = 3;
    public int MaxPayloadBytes { get; set; } = 256 * 1024;
    public int MaxResponsePreviewBytes { get; set; } = 4096;
    public bool BlockPrivateNetworks { get; set; } = true;
    public List<string> AllowedPrivateCidrs { get; set; } = [];
}

public sealed class WebhookSvixOptions
{
    public string? BaseUrl { get; set; }
    public string Environment { get; set; } = SvixConformanceProfileRegistry.ManagedEnvironment;
    public string ProviderVersion { get; set; } = SvixConformanceProfileRegistry.ManagedProviderVersion;
    public string CapabilityPolicyVersion { get; set; } =
        SvixConformanceProfileRegistry.ManagedCapabilityPolicyVersion;
    public string? AuthTokenSecretRef { get; set; } = SecretDefinitionRegistry.Keys.Webhooks.SvixAuthToken;
    public string? OperationalWebhookSecretRef { get; set; } = SecretDefinitionRegistry.Keys.Webhooks.SvixOperationalWebhookSecret;
    public long OperationalWebhookMaxBodyBytes { get; set; } = 65_536;
    public bool AppPortalEnabled { get; set; } = true;
    public bool SyncEventTypesOnStartup { get; set; } = true;
}

public sealed class WebhookStripeOptions
{
    public string? ConnectWebhookSecretRef { get; set; } = SecretDefinitionRegistry.Keys.Stripe.WebhookSecret;
    public long ConnectWebhookMaxBodyBytes { get; set; } = 65_536;
}

public sealed class WebhookTenantSettings
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "Inherited";
    public int MaxEndpoints { get; set; } = 10;
    public List<string> AllowedEventTypes { get; set; } = ["event.*", "registration.*", "report.*"];
    public bool AllowOrganizationWebhooks { get; set; } = true;
}
