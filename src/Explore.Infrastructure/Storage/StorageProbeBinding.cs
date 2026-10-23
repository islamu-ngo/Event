using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain.Constants;
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
    private readonly ResolvedSetting[] instance;
    private readonly ResolvedSetting[] tenant;
    private readonly RetainedSecretReference? access;
    private readonly RetainedSecretReference? secret;
    private readonly string snapshot;
    private string? credentialSnapshot;

    private StorageProbeBinding(Guid ambientTenant, ResolvedSetting[] instance, ResolvedSetting[] tenant,
        RetainedSecretReference? access, RetainedSecretReference? secret, string localRoot)
    {
        this.ambientTenant = ambientTenant;
        this.instance = instance;
        this.tenant = tenant;
        this.access = access;
        this.secret = secret;
        snapshot = JsonSerializer.Serialize(new { ambientTenant, instance, tenant, access, secret, localRoot });
    }

    internal static async Task<StorageProbeBinding> CaptureAsync(IServiceProvider services, Guid ambientTenant, CancellationToken ct)
    {
        var settings = services.GetRequiredService<IHierarchicalSettingsReader>();
        var instance = (await settings.ResolveBatchAsync(Keys,
            new(RequireAuthoritativeReads: true), ct)).OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
        // Preserve the real S3 resolver's ambient tenant selection, including the instance
        // probe's ordinary Global policy plus original request-tenant connection settings.
        var tenant = (await settings.ResolveBatchAsync(Keys,
            new(TenantId: ambientTenant, RequireAuthoritativeReads: true), ct))
            .OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
        RetainedSecretReference? access = null, secret = null;
        if (!string.IsNullOrWhiteSpace(Text(tenant, GovernanceSettingKeys.Storage.Endpoint))
            && !string.IsNullOrWhiteSpace(Text(tenant, GovernanceSettingKeys.Storage.BucketName)))
        {
            var secrets = services.GetRequiredService<IRetainedSecretResolver>();
            access = await secrets.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.AccessKeyId, ambientTenant, ct);
            secret = await secrets.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, ambientTenant, ct);
        }
        return new(ambientTenant, instance, tenant, access, secret,
            services.GetRequiredService<IOptions<LocalFileStorageOptions>>().Value.RootPath);
    }

    internal void RequireSame(StorageProbeBinding current)
    {
        if (!string.Equals(snapshot, current.snapshot, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The original probe provider selection or credential binding changed.");
    }

    internal async Task<S3Configuration?> ResolveCredentialsAsync(IServiceProvider services, CancellationToken ct)
    {
        if (access is null || secret is null) return null;
        var secrets = services.GetRequiredService<IRetainedSecretResolver>();
        var key = await secrets.ResolveAsync(access, ct);
        var password = await secrets.ResolveAsync(secret, ct);
        if (!key.IsResolved || string.IsNullOrWhiteSpace(key.Value)
            || !password.IsResolved || string.IsNullOrWhiteSpace(password.Value))
            throw new UnauthorizedAccessException("The original probe credential is unavailable.");
        string currentCredential = JsonSerializer.Serialize(new
        {
            Access = key.Value,
            Secret = password.Value,
            AccessRevision = key.Secret!.BindingRevision,
            SecretRevision = password.Secret!.BindingRevision,
            AccessSource = key.Source,
            SecretSource = password.Source,
            AccessScope = key.Scope,
            SecretScope = password.Scope,
            AccessScopeId = key.ScopeId,
            SecretScopeId = password.ScopeId
        });
        credentialSnapshot ??= currentCredential;
        if (credentialSnapshot != currentCredential)
            throw new UnauthorizedAccessException("The original probe credential material or revision changed.");
        string region = Text(tenant, GovernanceSettingKeys.Storage.Region);
        return new()
        {
            Endpoint = Text(tenant, GovernanceSettingKeys.Storage.Endpoint),
            BucketName = Text(tenant, GovernanceSettingKeys.Storage.BucketName),
            Region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region,
            ForcePathStyle = SettingValueSerializer.DeserializeBool(
                tenant.SingleOrDefault(row => row.Key == GovernanceSettingKeys.Storage.ForcePathStyle)?.Value, false),
            AccessKeyId = key.Value,
            SecretAccessKey = password.Value
        };
    }

    internal async Task RequireCredentialsAsync(IServiceProvider services, S3Configuration? original, CancellationToken ct)
    {
        var current = await ResolveCredentialsAsync(services, ct);
        if ((current is null) != (original is null)
            || current is not null && (current.AccessKeyId != original!.AccessKeyId
                || current.SecretAccessKey != original.SecretAccessKey))
            throw new UnauthorizedAccessException("The original storage probe credential changed.");
    }

    internal async Task<string> SelectProviderAsync(IServiceProvider services, Guid? policyTenant,
        S3Configuration? config, CancellationToken ct)
    {
        var resolver = new StoragePolicyResolver(new SnapshotSettings(this),
            services.GetRequiredService<IFileStorageProviderResolver>(), new SnapshotS3(config));
        return (await resolver.ResolveAsync(policyTenant, ct)).Provider;
    }

    private static string Text(IEnumerable<ResolvedSetting> settings, string key) =>
        SettingValueSerializer.DeserializeString(settings.SingleOrDefault(row => row.Key == key)?.Value, string.Empty);

    // The actual policy resolver takes the historical writable settings interface. This
    // private read adapter deliberately has no mutation implementation or cache authority.
    private sealed class SnapshotSettings(StorageProbeBinding binding) : IHierarchicalSettingsResolver
    {
        public Task<IReadOnlyList<ResolvedSetting>> ResolveBatchAsync(
            IEnumerable<string> keys, SettingContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (context.TenantId.HasValue && context.TenantId != binding.ambientTenant)
                throw new UnauthorizedAccessException("A probe cannot resolve another tenant's provider.");
            var selected = context.TenantId.HasValue ? binding.tenant : binding.instance;
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
        public Task SetValueAsync(string key, string value, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default) =>
            throw new UnauthorizedAccessException();
        public Task RemoveOverrideAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default) =>
            throw new UnauthorizedAccessException();
        public Task LockAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default) =>
            throw new UnauthorizedAccessException();
        public Task UnlockAsync(string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default) =>
            throw new UnauthorizedAccessException();
    }

    private sealed class SnapshotS3(S3Configuration? config) : IS3ConfigResolver
    {
        public Task<S3Configuration?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(config);
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) => Task.FromResult(config is not null);
        public void InvalidateCache(Guid? tenantId = null) => throw new NotSupportedException();
    }
}
