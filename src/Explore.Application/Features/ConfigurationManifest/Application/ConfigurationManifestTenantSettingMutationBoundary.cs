namespace Explore.Application.Features.ConfigurationManifest.Application;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Application.Settings;

public sealed record ConfigurationManifestTenantSettingMutation(
    string Key,
    string SerializedValue);

public sealed record ConfigurationManifestTenantSettingMutationInput(
    Guid TenantId,
    IReadOnlyList<ConfigurationManifestTenantSettingMutation> Mutations,
    Guid? ActorUserId,
    DateTime OccurredAtUtc);

public interface IConfigurationManifestTenantSettingMutationBoundary
{
    Task CreateInCurrentTransactionAsync(
        ConfigurationManifestTenantSettingMutationInput input,
        CancellationToken cancellationToken = default);

    Task ApplyInCurrentTransactionAsync(
        ConfigurationManifestTenantSettingMutationInput input,
        CancellationToken cancellationToken = default);
}

public sealed class ConfigurationManifestTenantSettingMutationBoundary(
    ITenantSettingRepository tenantSettings)
    : IConfigurationManifestTenantSettingMutationBoundary
{
    public async Task CreateInCurrentTransactionAsync(
        ConfigurationManifestTenantSettingMutationInput input,
        CancellationToken cancellationToken = default)
    {
        List<TenantSettingOverrideUpsert> writes = Compile(input);
        if (writes.Count == 0)
            return;

        await tenantSettings.CreateManyForTenantAsync(
            input.TenantId,
            writes,
            input.ActorUserId,
            input.OccurredAtUtc,
            cancellationToken);
    }

    public async Task ApplyInCurrentTransactionAsync(
        ConfigurationManifestTenantSettingMutationInput input,
        CancellationToken cancellationToken = default)
    {
        List<TenantSettingOverrideUpsert> writes = Compile(input);
        Guid actorUserId = input.ActorUserId
            ?? throw new InvalidOperationException(
                "Authenticated actor is required for Day 2 tenant import.");
        if (writes.Count == 0)
            return;
        await tenantSettings.UpsertManyForTenantAsync(
            input.TenantId,
            writes,
            actorUserId,
            cancellationToken);
    }

    private static List<TenantSettingOverrideUpsert> Compile(
        ConfigurationManifestTenantSettingMutationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Mutations);
        ArgumentOutOfRangeException.ThrowIfEqual(input.TenantId, Guid.Empty);
        if (input.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Tenant setting mutation timestamp must use UTC kind.",
                nameof(input));
        }

        if (input.Mutations.Any(mutation => EmailDeliverySettingKeys.Contains(mutation.Key)))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationManifestFailureCodes.KeyNotAllowed);
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var writes = new List<TenantSettingOverrideUpsert>(input.Mutations.Count);
        foreach (ConfigurationManifestTenantSettingMutation mutation in input.Mutations)
        {
            ArgumentNullException.ThrowIfNull(mutation);
            if (!seenKeys.Add(mutation.Key)
                || !ConfigurationManifestCatalog.TryGetTenantSetting(
                    mutation.Key,
                    out ConfigurationManifestSettingCatalogEntry? catalogEntry)
                || catalogEntry is null
                || catalogEntry.Definition.RequiresCoordinatedMutation)
            {
                throw new InvalidOperationException(
                    "The tenant setting mutation is not owned by this boundary.");
            }

            writes.Add(new TenantSettingOverrideUpsert(
                mutation.Key,
                mutation.SerializedValue,
                IsLocked: false));
        }
        return writes;
    }
}
