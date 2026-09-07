namespace Explore.Application.Features.ConfigurationManifest.Preflight;

using System.Collections.Immutable;
using Explore.Application.Features.ConfigurationManifest.Compilation;

public enum ConfigurationManifestTenantDisposition
{
    Create = 1,
    SkippedExisting = 2
}

public static class ConfigurationManifestApplicationFailureCodes
{
    public const string InstanceAlreadyBootstrapped =
        "configuration_manifest_instance_already_bootstrapped";
    public const string BootstrapStateInvalid =
        "configuration_manifest_bootstrap_state_invalid";
    public const string SettingLocked = "configuration_manifest_setting_locked";
    public const string DocumentLocked = "configuration_manifest_document_locked";
    public const string TenantConflict = "configuration_manifest_tenant_conflict";
    public const string WriteConflict = "configuration_manifest_write_conflict";
    public const string LockUnavailable = "configuration_manifest_lock_unavailable";
    public const string PaidPolicyUnavailable =
        "configuration_manifest_paid_policy_unavailable";
    public const string PaidPolicyStale = "configuration_manifest_paid_policy_stale";
    public const string PaidPolicyBroadening =
        "configuration_manifest_paid_policy_broadening";
    public const string ApplyFailed = "configuration_manifest_apply_failed";
    public const string Cancelled = "configuration_manifest_cancelled";
}

public sealed record ConfigurationManifestPreflightTenant(
    ConfigurationManifestTenantPlan Plan,
    ConfigurationManifestTenantDisposition Disposition,
    Guid TenantId);

public sealed record ConfigurationManifestPreflightError(
    int ManifestIndex,
    string Key,
    string Code,
    string Message);

public sealed record ConfigurationManifestPreflightResult(
    ConfigurationManifestApplyPlan BoundPlan,
    ImmutableArray<ConfigurationManifestPreflightTenant> Tenants,
    ImmutableArray<ConfigurationManifestPreflightError> Errors)
{
    public bool IsValid => Errors.IsEmpty;
}

public interface IConfigurationManifestPreflight
{
    Task<ConfigurationManifestPreflightResult> EvaluateAsync(
        ConfigurationManifestApplyPlan plan,
        CancellationToken cancellationToken);
}
