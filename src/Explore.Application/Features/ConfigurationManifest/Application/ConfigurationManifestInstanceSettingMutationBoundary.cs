namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Collections.Immutable;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Notifications;
using Explore.Application.Settings;

public sealed record ConfigurationManifestInstanceSettingMutation(
    string Key,
    string SerializedValue);

public sealed record ConfigurationManifestInstanceSettingMutationInput(
    IReadOnlyList<ConfigurationManifestInstanceSettingMutation> Mutations,
    Guid? ActorUserId,
    DateTime OccurredAtUtc);

public sealed record ConfigurationManifestInstanceSettingMutationResult(
    bool Success,
    string? FailureCode,
    string Message,
    ImmutableArray<SettingChangedNotification> DeferredNotifications);

public interface IConfigurationManifestInstanceSettingMutationBoundary
{
    Task<ConfigurationManifestInstanceSettingMutationResult>
        ApplyInCurrentTransactionAsync(
            ConfigurationManifestInstanceSettingMutationInput input,
            CancellationToken cancellationToken = default);
}

public sealed class ConfigurationManifestInstanceSettingMutationBoundary(
    SettingUpsertService scalarSettings,
    IPublicationPolicyMutationBoundary publicationPolicy)
    : IConfigurationManifestInstanceSettingMutationBoundary
{
    private const string SuccessMessage =
        "The instance manifest settings were applied.";

    public async Task<ConfigurationManifestInstanceSettingMutationResult>
        ApplyInCurrentTransactionAsync(
            ConfigurationManifestInstanceSettingMutationInput input,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Mutations);
        if (input.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Instance setting mutation timestamp must use UTC kind.",
                nameof(input));
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var ordinary = new List<ConfigurationManifestInstanceSettingMutation>();
        var coordinated = new List<PublicationPolicySettingMutation>();
        foreach (ConfigurationManifestInstanceSettingMutation mutation
                 in input.Mutations)
        {
            ArgumentNullException.ThrowIfNull(mutation);
            if (!seenKeys.Add(mutation.Key)
                || !ConfigurationManifestCatalog.TryGetInstanceSetting(
                    mutation.Key,
                    out ConfigurationManifestSettingCatalogEntry? catalogEntry)
                || catalogEntry is null)
            {
                throw new InvalidOperationException(
                    "The instance setting mutation is not admitted by the manifest catalog.");
            }

            if (catalogEntry.Definition.RequiresCoordinatedMutation)
            {
                coordinated.Add(new PublicationPolicySettingMutation(
                    mutation.Key,
                    PublicationPolicyMutationKind.Set,
                    mutation.SerializedValue,
                    TenantId: null,
                    IsLocked: false));
            }
            else
            {
                ordinary.Add(mutation);
            }
        }

        var notifications = ImmutableArray.CreateBuilder<SettingChangedNotification>(
            input.Mutations.Count);
        if (coordinated.Count > 0)
        {
            PublicationPolicyMutationResult result =
                await publicationPolicy.ApplyInstanceInCurrentTransactionAsync(
                    new PublicationPolicyInstanceMutationRequest(
                        input.ActorUserId ?? Guid.Empty,
                        input.OccurredAtUtc,
                        [.. coordinated]),
                    cancellationToken);
            if (!result.Success)
            {
                return new ConfigurationManifestInstanceSettingMutationResult(
                    Success: false,
                    result.FailureCode,
                    result.Message,
                    []);
            }

            notifications.AddRange(result.DeferredNotifications);
        }

        foreach (ConfigurationManifestInstanceSettingMutation mutation
                 in ordinary.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            InstanceSettingMutationResult result =
                await scalarSettings.UpsertInstanceValueInCurrentTransactionAsync(
                    new InstanceSettingMutationInput(
                        mutation.Key,
                        mutation.SerializedValue,
                        input.ActorUserId,
                        input.OccurredAtUtc),
                    cancellationToken);
            notifications.Add(result.Notification);
        }

        return new ConfigurationManifestInstanceSettingMutationResult(
            Success: true,
            FailureCode: null,
            SuccessMessage,
            notifications.MoveToImmutable());
    }
}
