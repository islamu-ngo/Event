using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Storage;

/// <summary>
/// Immutable-in-custody source snapshot. It does not persist a storage-object binding,
/// resolve a different secret after drift, or acquire configuration mutation authority.
/// </summary>
internal sealed class StorageProbeBinding
{
    private static readonly string[] Keys =
    [
        GovernanceSettingKeys.Deployment.Mode, GovernanceSettingKeys.TenantDelegation.LockStorage,
        GovernanceSettingKeys.Storage.Provider, GovernanceSettingKeys.Storage.RouteMatrix,
        GovernanceSettingKeys.Storage.DefaultMaxUploadBytes, GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
        GovernanceSettingKeys.Storage.InstanceMaxUploadBytes, GovernanceSettingKeys.Storage.Endpoint,
        GovernanceSettingKeys.Storage.BucketName, GovernanceSettingKeys.Storage.Region,
        GovernanceSettingKeys.Storage.ForcePathStyle
    ];
    private readonly Guid ambientTenant;
    private readonly SystemSetting[] instance;
    private readonly ResolvedSetting[] tenant;
    private readonly RetainedSecretReference? access;
    private readonly RetainedSecretReference? secret;
    private readonly RetainedSecretReference? instanceAccess;
    private readonly RetainedSecretReference? instanceSecret;
    private readonly string snapshot;
    private string? credentialSnapshot;
    private string? instanceCredentialSnapshot;

    private StorageProbeBinding(Guid ambientTenant, SystemSetting[] instance, ResolvedSetting[] tenant,
        RetainedSecretReference? access, RetainedSecretReference? secret,
        RetainedSecretReference? instanceAccess, RetainedSecretReference? instanceSecret,
        string bindingMetadata, string localRoot)
    {
        this.ambientTenant = ambientTenant;
        this.instance = instance;
        this.tenant = tenant;
        this.access = access;
        this.secret = secret;
        this.instanceAccess = instanceAccess;
        this.instanceSecret = instanceSecret;
        snapshot = JsonSerializer.Serialize(new { ambientTenant, instance, tenant, access, secret,
            instanceAccess, instanceSecret, bindingMetadata, localRoot });
    }

    internal static async Task<StorageProbeBinding> CaptureAsync(IServiceProvider services, Guid ambientTenant, CancellationToken ct)
    {
        var instance = (await services.GetRequiredService<IInstanceSystemSettingsReader>()
            .ReadAsync(Keys, ct)).OrderBy(row => row.SettingKey, StringComparer.Ordinal).ToArray();
        // Provider readiness belongs to the instance; transport retains the original
        // request tenant's connection settings and external references.
        var overrides = await services.GetRequiredService<IStorageProbeTenantSettingsReader>()
            .ReadAsync(ambientTenant, Keys, ct);
        var instanceSettings = instance.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var tenantSettings = overrides.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var tenant = Keys.Select(key => HierarchicalSettingMerge.Resolve(key, instanceSettings, tenantSettings))
            .OfType<ResolvedSetting>().OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
        var bindingImage = await services.GetRequiredService<IStorageProbeSecretBindingsReader>()
            .ReadAsync(ambientTenant, ct);
        SecretBinding? InstanceBinding(string key) => bindingImage.Bindings.SingleOrDefault(
            row => row.SettingKey == key && row.Scope == SecretScope.Instance);
        SecretBinding? TransportBinding(string key) => bindingImage.Bindings.SingleOrDefault(
            row => row.SettingKey == key && row.Scope == SecretScope.Tenant && row.ScopeId == ambientTenant)
            ?? InstanceBinding(key);
        RetainedSecretReference? access = null, secret = null, instanceAccess = null, instanceSecret = null;
        string endpoint = SettingValueSerializer.DeserializeString(HierarchicalSettingMerge.Resolve(
            GovernanceSettingKeys.Storage.Endpoint, instanceSettings)?.Value, string.Empty);
        string bucket = SettingValueSerializer.DeserializeString(HierarchicalSettingMerge.Resolve(
            GovernanceSettingKeys.Storage.BucketName, instanceSettings)?.Value, string.Empty);
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(bucket))
        {
            var secrets = services.GetRequiredService<IRetainedSecretResolver>();
            instanceAccess = await secrets.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
                null, InstanceBinding(SecretDefinitionRegistry.Keys.Storage.AccessKeyId), ct);
            instanceSecret = await secrets.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey,
                null, InstanceBinding(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey), ct);
        }
        if (!string.IsNullOrWhiteSpace(Text(tenant, GovernanceSettingKeys.Storage.Endpoint))
            && !string.IsNullOrWhiteSpace(Text(tenant, GovernanceSettingKeys.Storage.BucketName)))
        {
            var secrets = services.GetRequiredService<IRetainedSecretResolver>();
            access = await secrets.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
                ambientTenant, TransportBinding(SecretDefinitionRegistry.Keys.Storage.AccessKeyId), ct)
                ?? throw new InvalidOperationException("storage_secret_unavailable");
            secret = await secrets.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey,
                ambientTenant, TransportBinding(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey), ct)
                ?? throw new InvalidOperationException("storage_secret_unavailable");
        }
        return new(ambientTenant, instance, tenant, access, secret, instanceAccess, instanceSecret, bindingImage.Metadata,
            services.GetRequiredService<IOptions<LocalFileStorageOptions>>().Value.RootPath);
    }

    internal void RequireSame(StorageProbeBinding current)
    {
        if (!string.Equals(snapshot, current.snapshot, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The original probe provider selection or credential binding changed.");
    }

    internal async Task<(S3Configuration? Tenant, bool InstanceConfigured)> ResolveCredentialsAsync(
        IServiceProvider services, CancellationToken ct)
        => (await ResolveCredentialsAsync(services, false, ct),
            await ResolveCredentialsAsync(services, true, ct) is not null);

    private async Task<S3Configuration?> ResolveCredentialsAsync(
        IServiceProvider services, bool instanceOwner, CancellationToken ct)
    {
        var accessReference = instanceOwner ? instanceAccess : access;
        var secretReference = instanceOwner ? instanceSecret : secret;
        if (accessReference is null || secretReference is null) return null;
        var secrets = services.GetRequiredService<IRetainedSecretResolver>();
        var key = await secrets.ResolveAsync(accessReference, ct);
        var password = await secrets.ResolveAsync(secretReference, ct);
        bool unconfigured = instanceOwner && (key.Status == SecretResolutionStatus.Unconfigured
            || password.Status == SecretResolutionStatus.Unconfigured);
        if (key.Status is not (SecretResolutionStatus.Resolved or SecretResolutionStatus.Unconfigured)
            || password.Status is not (SecretResolutionStatus.Resolved or SecretResolutionStatus.Unconfigured))
            throw new UnauthorizedAccessException("The original probe credential is unavailable.");
        string currentCredential = JsonSerializer.Serialize(new
        {
            Access = key.Value,
            Secret = password.Value,
            AccessStatus = key.Status,
            SecretStatus = password.Status,
            AccessRevision = key.Secret?.BindingRevision,
            SecretRevision = password.Secret?.BindingRevision,
            AccessSource = key.Source,
            SecretSource = password.Source,
            AccessScope = key.Scope,
            SecretScope = password.Scope,
            AccessScopeId = key.ScopeId,
            SecretScopeId = password.ScopeId
        });
        if (instanceOwner) instanceCredentialSnapshot ??= currentCredential;
        else credentialSnapshot ??= currentCredential;
        if ((instanceOwner ? instanceCredentialSnapshot : credentialSnapshot) != currentCredential)
            throw new UnauthorizedAccessException("The original probe credential material or revision changed.");
        if (unconfigured) return null;
        if (!key.IsResolved || string.IsNullOrWhiteSpace(key.Value)
            || !password.IsResolved || string.IsNullOrWhiteSpace(password.Value))
            throw new UnauthorizedAccessException("The original probe credential is unavailable.");
        string Read(string settingKey) => instanceOwner
            ? SettingValueSerializer.DeserializeString(
                instance.SingleOrDefault(row => row.SettingKey == settingKey)?.Value, string.Empty)
            : Text(tenant, settingKey);
        string region = Read(GovernanceSettingKeys.Storage.Region);
        return new()
        {
            Endpoint = Read(GovernanceSettingKeys.Storage.Endpoint),
            BucketName = Read(GovernanceSettingKeys.Storage.BucketName),
            Region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region,
            ForcePathStyle = SettingValueSerializer.DeserializeBool(
                Read(GovernanceSettingKeys.Storage.ForcePathStyle), false),
            AccessKeyId = key.Value,
            SecretAccessKey = password.Value
        };
    }

    internal async Task RequireCredentialsAsync(IServiceProvider services,
        (S3Configuration? Tenant, bool InstanceConfigured) original, CancellationToken ct)
    {
        var current = await ResolveCredentialsAsync(services, ct);
        if (current.InstanceConfigured != original.InstanceConfigured
            || (current.Tenant is null) != (original.Tenant is null)
            || current.Tenant is not null && (current.Tenant.AccessKeyId != original.Tenant!.AccessKeyId
                || current.Tenant.SecretAccessKey != original.Tenant.SecretAccessKey))
            throw new UnauthorizedAccessException("The original storage probe credential changed.");
    }

    internal async Task<string> SelectProviderAsync(IServiceProvider services, Guid? policyTenant,
        bool instanceConfigured, CancellationToken ct)
    {
        var resolver = new StoragePolicyResolver(new SnapshotSettings(this), new SnapshotInstanceSettings(this),
            services.GetRequiredService<IFileStorageProviderResolver>(), new SnapshotInstanceS3(instanceConfigured));
        return (await resolver.ResolveAsync(policyTenant, ct)).Provider;
    }

    private static string Text(IEnumerable<ResolvedSetting> settings, string key) =>
        SettingValueSerializer.DeserializeString(settings.SingleOrDefault(row => row.Key == key)?.Value, string.Empty);

    private sealed class SnapshotSettings(StorageProbeBinding binding) : IHierarchicalSettingsReader
    {
        public Task<IReadOnlyList<ResolvedSetting>> ResolveBatchAsync(
            IEnumerable<string> keys, SettingContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (context.TenantId != binding.ambientTenant)
                throw new UnauthorizedAccessException("A probe cannot resolve another tenant's provider.");
            var selected = binding.tenant;
            return Task.FromResult<IReadOnlyList<ResolvedSetting>>(selected.Where(row => keys.Contains(row.Key)).ToArray());
        }

        public async Task<ResolvedSetting?> ResolveWithMetadataAsync(string key, SettingContext context, CancellationToken ct = default) =>
            (await ResolveBatchAsync([key], context, ct)).SingleOrDefault();
        public async Task<T?> ResolveAsync<T>(string key, SettingContext context, CancellationToken ct = default) =>
            SettingValueSerializer.Deserialize((await ResolveWithMetadataAsync(key, context, ct))?.Value, default(T)!);
        public Task<TGroup> ResolveGroupAsync<TGroup>(SettingContext context, CancellationToken ct = default)
            where TGroup : ISettingGroup, new() => throw new NotSupportedException();
        public void InvalidateCache(SettingScope? scope = null, Guid? scopeId = null) => throw new NotSupportedException();
        public void InvalidateOrganizationCache(Guid tenantId, Guid organizationId) => throw new NotSupportedException();
        public void InvalidateUserCache(Guid tenantId, Guid userId) => throw new NotSupportedException();
    }

    private sealed class SnapshotInstanceSettings(StorageProbeBinding binding) : IInstanceSystemSettingsReader
    {
        public Task<IReadOnlyList<SystemSetting>> ReadAsync(
            IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SystemSetting>>(
                binding.instance.Where(row => keys.Contains(row.SettingKey)).ToArray());
        }
    }

    private sealed class SnapshotInstanceS3(bool configured) : IInstanceS3ConfigResolver
    {
        public Task<bool> IsInstanceConfiguredAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(configured);
        }
    }
}
