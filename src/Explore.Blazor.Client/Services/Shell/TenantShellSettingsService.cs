using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;

namespace Explore.Blazor.Client.Services.Shell;

public sealed class TenantShellSettingsService(ISettingsClient apiClient) : ITenantShellSettingsService
{
    public const string Category = "UiShell";

    public Task<SettingGroupResponseDto> GetAsync(CancellationToken cancellationToken = default) =>
        apiClient.GetTenantScopedSettingsAsync(Category, cancellationToken: cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        apiClient.UpdateTenantSettingAsync(
            key,
            new UpdateSettingValueDto { Value = value },
            cancellationToken: cancellationToken);
}
