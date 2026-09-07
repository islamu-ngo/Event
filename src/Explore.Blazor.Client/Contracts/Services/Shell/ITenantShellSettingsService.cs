// ABOUTME: Defines the Blazor service boundary for tenant workspace-shell governance settings.
// ABOUTME: Keeps Razor components on generated DTOs without calling the generated API client directly.

using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Shell;

public interface ITenantShellSettingsService
{
    Task<HalResourceOfSettingGroupResponseDto> GetAsync(CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);
}
