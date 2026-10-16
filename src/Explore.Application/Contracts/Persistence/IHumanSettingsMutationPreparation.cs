using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Settings;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Opaque in-process capability. Implementing this interface does not create an admission.
/// Only its scoped issuer can execute it or admit a public data operation with it.
/// </summary>
public interface IPreparedHumanSettingsMutation;

/// <summary>Prepares provider effects outside the original owner's transaction.</summary>
public interface IHumanSettingsMutationPreparation
{
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateInstanceStorageSettingsCommand command,
        StorageSettingsMutation mutation, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(PatchTenantStorageSettingsCommand command,
        StorageSettingsMutation mutation, CancellationToken cancellationToken = default);
    Task<T> ExecuteStorageJoinedAsync<T>(IPreparedHumanSettingsMutation owner,
        StorageSettingsMutation storage, Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAuthProviderConfigurationCommand command,
        ProviderConfigurationMutation mutation, CancellationToken cancellationToken = default);
    Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAuthorizationProviderConfigurationCommand command,
        ProviderConfigurationMutation mutation, CancellationToken cancellationToken = default);
    Task<T> ExecuteProviderJoinedAsync<T>(IPreparedHumanSettingsMutation owner,
        ProviderConfigurationMutation provider, Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);
    Task<T> ExecutePreparedAsync<T>(IPreparedHumanSettingsMutation prepared,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);
}
