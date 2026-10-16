using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.DTOs.Tenant;
using Explore.Application.DTOs.Tenant.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Settings;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Settings;

/// <summary>
/// The first configuration observation and its one physical owner. The private constructor,
/// issuing service, source issuer and selected writer all participate in custody.
/// </summary>
public sealed class StorageSettingsMutation : IAsyncDisposable
{
    private readonly object service;
    private readonly string commandSnapshot;
    private IPolicyMutationExecutionScope? scope;
    private string instanceProjection = "";
    private string tenantProjection = "";
    private bool disposed;
    private int consumed;

    private StorageSettingsMutation(object command, object service, Guid? tenant)
    {
        OriginalCommand = command;
        this.service = service;
        TenantId = tenant;
        commandSnapshot = JsonSerializer.Serialize(command, command.GetType());
    }

    public object OriginalCommand { get; }
    public Guid Actor => Source.Actor;
    public Guid? TenantId { get; }
    public IStorageConfigurationSource Source { get; private set; } = null!;
    public IUnitOfWork OriginalOwner { get; private set; } = null!;
    public StorageConfigurationState Before { get; private set; } = null!;
    public ImmutableArray<StorageConfigurationRow> Effects { get; private set; }
    public ImmutableDictionary<string, string> Values => Effects.ToImmutableDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
    public InstanceStorageSettingsDto InstanceProjection => JsonSerializer.Deserialize<InstanceStorageSettingsDto>(instanceProjection)!;
    public TenantStorageSettingsDto TenantProjection => JsonSerializer.Deserialize<TenantStorageSettingsDto>(tenantProjection)!;
    public DateTime OccurredAt { get; private set; }

    internal static async Task<StorageSettingsMutation> ObserveAsync(object command, object service,
        Guid? tenant, IPolicyMutationExecutionScopeFactory scopes, CancellationToken token)
    {
        if (command is not (UpdateInstanceStorageSettingsCommand or PatchTenantStorageSettingsCommand)
            || tenant == Guid.Empty || (command is PatchTenantStorageSettingsCommand) != tenant.HasValue)
            throw new UnauthorizedAccessException("Storage observation requires its actual configuration action.");
        var observation = new StorageSettingsMutation(command, service, tenant);
        try
        {
            observation.scope = await scopes.CreateAsync(new(tenant), token);
            var services = observation.scope.Services;
            observation.Source = await services.GetRequiredService<IStorageConfigurationAuthority>()
                .CaptureAsync(command, tenant, token);
            observation.OriginalOwner = services.GetRequiredService<IUnitOfWork>();
            var writer = services.GetRequiredService<IStorageConfigurationWriter>();
            observation.Before = await writer.ObserveAsync(observation.Source, observation.OriginalOwner, token);
            // These calls can contact the selected external authority. No transaction or source
            // lock is held; the same complete rows and absences are admitted again afterward.
            bool configured = await observation.Source.ObserveProviderAsync(observation.Before, token);
            await writer.RequireUnchangedAsync(observation.Before, token);
            observation.OccurredAt = writer.RepresentTimestamp(services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
            observation.Compile(configured);
            observation.RequireActive();
            return observation;
        }
        catch { await observation.DisposeAsync(); throw; }
    }

    public async Task<ValidationResult> ValidateAsync(CancellationToken token = default)
    {
        RequireActive();
        if (TenantId.HasValue)
        {
            var command = JsonSerializer.Deserialize<PatchTenantStorageSettingsCommand>(commandSnapshot)!;
            var shape = await new PatchTenantStorageSettingsDtoValidator().ValidateAsync(command.Settings, token);
            if (!shape.IsValid) return shape;
            var projection = TenantProjection;
            if (projection.IsReadOnly)
                return new([new ValidationFailure("Settings", "Tenant storage settings are locked by instance policy.")]);
            return await new TenantStorageSettingsDtoValidator(projection.EffectivePolicy.InstanceMaxUploadBytes)
                .ValidateAsync(projection, token);
        }
        var instance = JsonSerializer.Deserialize<UpdateInstanceStorageSettingsCommand>(commandSnapshot)!;
        if (!instance.Patch.HasChanges() || instance.Patch.Policy is { HasValue: true, Value: null }
            || instance.Patch.S3Configuration is { HasValue: true, Value: null })
            return new([new ValidationFailure("Patch", "Storage settings patch must include a complete policy or S3 configuration group.")]);
        return await new InstanceStorageSettingsDtoValidator().ValidateAsync(InstanceProjection, token);
    }

    internal async Task ExecuteAsync(object issuer, CancellationToken token)
    {
        RequireActive();
        if (!ReferenceEquals(service, issuer) || Interlocked.Exchange(ref consumed, 1) != 0)
            throw new UnauthorizedAccessException("Storage observations cannot change issuer or replay.");
        var validation = await ValidateAsync(token);
        if (!validation.IsValid) throw new ValidationException(validation.Errors);
        var services = scope!.Services;
        var writer = services.GetRequiredService<IStorageConfigurationWriter>();
        // Re-resolve only the retained external references while idle, never a new winner.
        await Source.RequireExternalSourcesAsync(token);
        await writer.RequireUnchangedAsync(Before, token);
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = OriginalCommand switch
        {
            UpdateInstanceStorageSettingsCommand command => await preparation.PrepareAsync(command, this, token),
            PatchTenantStorageSettingsCommand command => await preparation.PrepareAsync(command, this, token),
            _ => throw new UnauthorizedAccessException()
        };
        await services.GetRequiredService<ISettingMutationLock>().ExecuteOrderedGroupsAsync(
            [StorageConfigurationState.Keys], ct => preparation.ExecutePreparedAsync(proof,
                (owner, inner) => preparation.ExecuteStorageJoinedAsync(owner, this, async (_, joined) =>
                {
                    await writer.ExecuteAsync(this, joined);
                    return true;
                }, inner), ct), token);
        // The owning attempt has acknowledged physical settlement, and every ordered
        // source wrapper has returned. Direct service calls share this activation path.
        services.GetRequiredService<IHierarchicalSettingsResolver>()
            .InvalidateCache(TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance, TenantId);
        services.GetRequiredService<IS3ConfigResolver>().InvalidateCache(TenantId);
    }

    public void RequireActive()
    {
        if (disposed || scope is null || Source is null
            || !ReferenceEquals(Source.OriginalCommand, OriginalCommand)
            || Source.TenantId != TenantId
            || JsonSerializer.Serialize(OriginalCommand, OriginalCommand.GetType()) != commandSnapshot)
            throw new UnauthorizedAccessException("The original storage configuration observation changed.");
    }

    private void Compile(bool configured)
    {
        var projected = StorageConfigurationProjection.Project(Before, Source.AmbientTenant, configured);
        var instance = projected.Instance;
        var tenant = projected.Tenant;
        var effects = ImmutableArray.CreateBuilder<StorageConfigurationRow>();
        if (TenantId is null)
        {
            var patch = JsonSerializer.Deserialize<UpdateInstanceStorageSettingsCommand>(commandSnapshot)!.Patch;
            if (patch.Policy is { HasValue: true, Value: { } policy })
            {
                instance.Provider = policy.Provider;
                instance.DefaultMaxUploadBytes = policy.DefaultMaxUploadBytes;
                instance.DefaultTenantQuotaBytes = policy.DefaultTenantQuotaBytes;
                instance.InstanceMaxUploadBytes = policy.InstanceMaxUploadBytes;
                instance.LockTenantStorage = policy.LockTenantStorage;
                instance = instance with { Routes = policy.Routes };
                Add(0, StorageConfigurationProjection.NormalizeProvider(policy.Provider));
                Add(1, policy.DefaultMaxUploadBytes); Add(2, policy.DefaultTenantQuotaBytes);
                Add(3, policy.InstanceMaxUploadBytes); Add(4, policy.LockTenantStorage);
                Add(5, Routes(policy.Routes));
            }
            if (patch.S3Configuration is { HasValue: true, Value: { } s3 })
            {
                instance.S3Endpoint = s3.Endpoint; instance.S3PublicEndpoint = s3.PublicEndpoint;
                instance.S3BucketName = s3.BucketName; instance.S3Region = s3.Region;
                instance.S3ForcePathStyle = s3.ForcePathStyle;
                instance.S3UploadUrlExpirationMinutes = s3.UploadUrlExpirationMinutes;
                Add(6, s3.Endpoint?.Trim() ?? ""); Add(7, s3.PublicEndpoint?.Trim() ?? "");
                Add(8, s3.BucketName?.Trim() ?? ""); Add(9, s3.Region?.Trim() ?? "");
                Add(10, s3.ForcePathStyle); Add(11, s3.UploadUrlExpirationMinutes);
            }
        }
        else
        {
            var patch = JsonSerializer.Deserialize<PatchTenantStorageSettingsCommand>(commandSnapshot)!.Settings;
            if (patch.Policy?.Provider is { HasValue: true } provider)
            { tenant.Provider = provider.Value ?? ""; Add(0, StorageConfigurationProjection.NormalizeProvider(provider.Value)); }
            if (patch.Policy?.MaxUploadBytes is { HasValue: true } upload)
            { tenant.MaxUploadBytes = upload.Value; Add(1, upload.Value); }
            if (patch.Policy?.TenantQuotaBytes is { HasValue: true } quota)
            { tenant.TenantQuotaBytes = quota.Value; Add(2, quota.Value); }
            if (patch.Policy?.Routes is { HasValue: true } routes)
            { tenant = tenant with { Routes = routes.Value ?? [] }; Add(5, Routes(routes.Value ?? [])); }
            if (patch.S3?.Endpoint is { HasValue: true } endpoint)
            { tenant.S3Endpoint = endpoint.Value ?? ""; Add(6, endpoint.Value?.Trim() ?? ""); }
            if (patch.S3?.PublicEndpoint is { HasValue: true } publicEndpoint)
            { tenant.S3PublicEndpoint = publicEndpoint.Value ?? ""; Add(7, publicEndpoint.Value?.Trim() ?? ""); }
            if (patch.S3?.BucketName is { HasValue: true } bucket)
            { tenant.S3BucketName = bucket.Value ?? ""; Add(8, bucket.Value?.Trim() ?? ""); }
            if (patch.S3?.Region is { HasValue: true } region)
            { tenant.S3Region = region.Value ?? ""; Add(9, region.Value?.Trim() ?? ""); }
            if (patch.S3?.ForcePathStyle is { HasValue: true } path)
            { tenant.S3ForcePathStyle = path.Value; Add(10, path.Value); }
            if (patch.S3?.UploadUrlExpirationMinutes is { HasValue: true } expiration)
            { tenant.S3UploadUrlExpirationMinutes = expiration.Value; Add(11, expiration.Value); }
        }
        instanceProjection = JsonSerializer.Serialize(instance);
        tenantProjection = JsonSerializer.Serialize(tenant);
        Effects = effects.ToImmutable();

        void Add<T>(int index, T value)
        {
            string key = StorageConfigurationState.Keys[index];
            var original = TenantId.HasValue ? Before.Tenant[key] : Before.System[key];
            var metadata = Metadata(index);
            effects.Add(new(TenantId, key, original?.Id ?? Guid.CreateVersion7(),
                SettingValueSerializer.Serialize(value), TenantId.HasValue ? null : metadata.Type,
                TenantId.HasValue && original?.IsLocked == true, null,
                TenantId.HasValue ? null : metadata.Description, TenantId.HasValue ? null : metadata.Category,
                TenantId.HasValue ? null : metadata.Order, original?.CreatedAt ?? OccurredAt,
                original is null ? Actor : original.CreatedBy, OccurredAt, Actor));
        }
    }

    public static (SettingValueType Type, string Category, int Order, string Description) Metadata(int index) => index switch
    {
        0 => (SettingValueType.String, "ObjectStorage", 1, "Selected storage provider. Local filesystem is default; S3-compatible storage is optional."),
        1 => (SettingValueType.Long, "ObjectStorage", 2, "Default maximum upload size in bytes for tenant storage policy."),
        2 => (SettingValueType.Long, "ObjectStorage", 3, "Default tenant storage quota in bytes."),
        3 => (SettingValueType.Long, "ObjectStorage", 4, "Instance-wide upload ceiling in bytes; tenant overrides cannot exceed this value."),
        4 => (SettingValueType.Boolean, "Governance", 5, "Lock tenant-level storage overrides when running multi-tenant deployments."),
        5 => (SettingValueType.Json, "ObjectStorage", 6, "Server-side route matrix that maps image, document, and general uploads to providers and byte limits."),
        6 => (SettingValueType.String, "ObjectStorage", 7, "S3-compatible endpoint URL (e.g., https://fsn1.your-objectstorage.com)"),
        7 => (SettingValueType.String, "ObjectStorage", 8, "Public endpoint for S3-compatible public object access when configured."),
        8 => (SettingValueType.String, "ObjectStorage", 9, "S3 bucket name for object storage"),
        9 => (SettingValueType.String, "ObjectStorage", 12, "S3 region identifier (e.g., fsn1 for Hetzner, us-east-1 for AWS)"),
        10 => (SettingValueType.Boolean, "ObjectStorage", 13, "Use path-style URLs (required by most non-AWS S3 providers)"),
        11 => (SettingValueType.Integer, "ObjectStorage", 14, "Legacy S3 presigned upload URL expiration time in minutes"),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static StorageRouteMatrixDocument Routes(IEnumerable<Explore.Application.DTOs.Storage.StorageRouteSettingsDto>? routes) =>
        new(1, (routes ?? []).GroupBy(row => StorageConfigurationProjection.NormalizeRoute(row.RouteKey))
            .Select(group => group.First()).Select(row => new StorageRouteSetting(
                StorageConfigurationProjection.NormalizeRoute(row.RouteKey),
                StorageConfigurationProjection.NormalizeProvider(row.Provider), row.MaxUploadBytes)).ToList());

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        if (scope is not null) await scope.DisposeAsync();
    }
    public override string ToString() => nameof(StorageSettingsMutation);
}
