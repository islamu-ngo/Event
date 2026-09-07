using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Shell;

public interface ITenantShellSettingsService
{
    Task<SettingGroupResponseDto> GetAsync(CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);
}
