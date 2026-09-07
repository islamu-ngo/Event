// ABOUTME: Defines the typed Blazor boundary for AT Protocol federation governance reads and writes.
// ABOUTME: Keeps Razor components dependent on HAL-aware application services instead of the generated API client.

using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Federation;

public interface IAtprotoFederationSettingsService
{
    Task<HalResourceOfSettingGroupResponseDto> GetInstanceAsync(
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateInstanceAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> SetInstanceLockAsync(
        string key,
        bool isLocked,
        CancellationToken cancellationToken = default);

    Task<HalResourceOfSettingGroupResponseDto> GetTenantAsync(
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateTenantAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);
}
