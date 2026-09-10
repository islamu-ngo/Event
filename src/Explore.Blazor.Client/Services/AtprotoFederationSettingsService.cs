using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Federation;

namespace Explore.Blazor.Client.Services;

public sealed class AtprotoFederationSettingsService(ISettingsClient apiClient)
    : IAtprotoFederationSettingsService
{
    private const string TenantCategory = "AtprotoFederation";

    public Task<HalResourceOfSettingGroupResponseDto> GetInstanceAsync(
        CancellationToken cancellationToken = default) =>
        apiClient.GetInstanceAtprotoFederationSettingsAsync(cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateInstanceAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        apiClient.UpdateInstanceAtprotoFederationSettingAsync(
            key,
            new UpdateSettingValueDto { Value = value },
            cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> SetInstanceLockAsync(
        string key,
        bool isLocked,
        CancellationToken cancellationToken = default) =>
        isLocked
            ? apiClient.LockInstanceAtprotoFederationSettingAsync(key, cancellationToken: cancellationToken)
            : apiClient.UnlockInstanceAtprotoFederationSettingAsync(key, cancellationToken: cancellationToken);

    public Task<HalResourceOfSettingGroupResponseDto> GetTenantAsync(
        CancellationToken cancellationToken = default) =>
        apiClient.GetTenantScopedSettingsAsync(TenantCategory, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateTenantAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        apiClient.UpdateTenantSettingAsync(
            key,
            new UpdateSettingValueDto { Value = value },
            cancellationToken: cancellationToken);
}
