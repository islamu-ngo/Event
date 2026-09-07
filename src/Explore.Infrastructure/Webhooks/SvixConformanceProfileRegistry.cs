using Explore.Domain;

namespace Explore.Infrastructure.Webhooks;

public enum SvixDeploymentKind
{
    Managed = 1,
    SelfHosted = 2
}

public sealed record SvixConformanceProfile(
    SvixDeploymentKind DeploymentKind,
    string Environment,
    string ProviderVersion,
    string CapabilityPolicyVersion,
    string EvidenceRevision,
    int ExecutedTestCount,
    TimeSpan IdempotencyWindow,
    bool SupportsExactMessageLookup,
    WebhookProviderCapability Capabilities)
{
    public bool IsVerified => ExecutedTestCount > 0;
}

public static class SvixConformanceProfileRegistry
{
    public const string ManagedEnvironment = "production";
    public const string ManagedProviderVersion = "managed-api-v1";
    public const string ManagedCapabilityPolicyVersion = "svix-managed-api-v1";
    public const string SelfHostedEnvironment = "self-hosted";
    public const string SelfHostedProviderVersion = "1.96.1";
    public const string SelfHostedCapabilityPolicyVersion = "svix-self-hosted-1.96.1-v1";
    public const string SelfHostedImage = "svix/svix-server:v1.96.1";

    private static readonly IReadOnlyList<SvixConformanceProfile> Profiles =
    [
        new(
            SvixDeploymentKind.Managed,
            ManagedEnvironment,
            ManagedProviderVersion,
            ManagedCapabilityPolicyVersion,
            "managed-live-proof-required",
            0,
            TimeSpan.FromHours(12),
            false,
            WebhookProviderCapability.None),
        new(
            SvixDeploymentKind.SelfHosted,
            SelfHostedEnvironment,
            SelfHostedProviderVersion,
            SelfHostedCapabilityPolicyVersion,
            "2026-07-14.3",
            11,
            TimeSpan.FromHours(12),
            false,
            WebhookProviderCapability.EndpointManagement |
            WebhookProviderCapability.PayloadInspection |
            WebhookProviderCapability.AppPortal |
            WebhookProviderCapability.EventCatalog)
    ];

    public static IReadOnlyList<SvixConformanceProfile> All => Profiles;

    public static IReadOnlyList<SvixConformanceProfile> Supported =>
        Profiles.Where(profile => profile.IsVerified).ToArray();

    public static bool TryResolve(
        string? environment,
        string? providerVersion,
        string? capabilityPolicyVersion,
        bool baseUrlConfigured,
        out SvixConformanceProfile? profile)
    {
        var deploymentKind = baseUrlConfigured
            ? SvixDeploymentKind.SelfHosted
            : SvixDeploymentKind.Managed;
        profile = Profiles.SingleOrDefault(candidate =>
            candidate.DeploymentKind == deploymentKind &&
            string.Equals(candidate.Environment, environment?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.ProviderVersion, providerVersion?.Trim(), StringComparison.Ordinal) &&
            string.Equals(
                candidate.CapabilityPolicyVersion,
                capabilityPolicyVersion?.Trim(),
                StringComparison.Ordinal));
        return profile is not null;
    }

    public static bool SupportsExactMessageLookup(
        WebhookProviderKind providerKind,
        string providerVersion,
        string providerEnvironment) =>
        providerKind == WebhookProviderKind.Svix &&
        Profiles.Any(profile =>
            profile.IsVerified &&
            profile.SupportsExactMessageLookup &&
            string.Equals(profile.ProviderVersion, providerVersion.Trim(), StringComparison.Ordinal) &&
            string.Equals(profile.Environment, providerEnvironment.Trim(), StringComparison.OrdinalIgnoreCase));
}
