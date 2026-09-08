using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;

namespace Explore.Application.Models.Storage;

public sealed record ResolvedStoragePolicy
{
    public ResolvedStoragePolicy(
        Guid? TenantId,
        string Provider,
        long MaxUploadBytes,
        long TenantQuotaBytes,
        long InstanceMaxUploadBytes,
        bool TenantOverridesAllowed,
        bool TenantStorageLocked,
        SettingSource ProviderSource,
        SettingSource MaxUploadSource,
        SettingSource QuotaSource)
        : this(
            TenantId,
            Provider,
            MaxUploadBytes,
            TenantQuotaBytes,
            InstanceMaxUploadBytes,
            TenantOverridesAllowed,
            TenantStorageLocked,
            ProviderSource,
            MaxUploadSource,
            QuotaSource,
            StorageRouteKeys.General,
            1,
            [],
            null)
    {
    }

    public ResolvedStoragePolicy(
        Guid? TenantId,
        string Provider,
        long MaxUploadBytes,
        long TenantQuotaBytes,
        long InstanceMaxUploadBytes,
        bool TenantOverridesAllowed,
        bool TenantStorageLocked,
        SettingSource ProviderSource,
        SettingSource MaxUploadSource,
        SettingSource QuotaSource,
        string RouteKey,
        int PolicyVersion,
        IReadOnlyList<ResolvedStorageRoutePolicy> Routes,
        ResolvedStorageRoutePolicy? SelectedRoute = null)
    {
        this.TenantId = TenantId;
        this.Provider = Provider;
        this.MaxUploadBytes = MaxUploadBytes;
        this.TenantQuotaBytes = TenantQuotaBytes;
        this.InstanceMaxUploadBytes = InstanceMaxUploadBytes;
        this.TenantOverridesAllowed = TenantOverridesAllowed;
        this.TenantStorageLocked = TenantStorageLocked;
        this.ProviderSource = ProviderSource;
        this.MaxUploadSource = MaxUploadSource;
        this.QuotaSource = QuotaSource;
        this.RouteKey = RouteKey;
        this.PolicyVersion = PolicyVersion;
        this.Routes = Routes;
        this.SelectedRoute = SelectedRoute
            ?? new ResolvedStorageRoutePolicy(RouteKey, Provider, MaxUploadBytes, ProviderSource, MaxUploadSource);
    }

    public Guid? TenantId { get; init; }
    public string Provider { get; init; }
    public long MaxUploadBytes { get; init; }
    public long TenantQuotaBytes { get; init; }
    public long InstanceMaxUploadBytes { get; init; }
    public bool TenantOverridesAllowed { get; init; }
    public bool TenantStorageLocked { get; init; }
    public SettingSource ProviderSource { get; init; }
    public SettingSource MaxUploadSource { get; init; }
    public SettingSource QuotaSource { get; init; }
    public string RouteKey { get; init; }
    public int PolicyVersion { get; init; }
    public IReadOnlyList<ResolvedStorageRoutePolicy> Routes { get; init; }
    public ResolvedStorageRoutePolicy SelectedRoute { get; init; }
}
