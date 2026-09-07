using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Shell;

public interface IUiShellContextService
{
    Task<UiShellContextDto?> GetContextAsync(CancellationToken cancellationToken = default);
    Task<UiShellContextDto?> GetCachedContextAsync(CancellationToken cancellationToken = default);
    void ResetCache();
}
