using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public sealed class TenantNavLinksState : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<TenantNavigationLinkDto> _links = [];
    private bool _loaded;

    /// <summary>Cached navigation links, ordered by <see cref="TenantNavigationLinkDto.Order"/>.</summary>
    public IReadOnlyList<TenantNavigationLinkDto> Links => _links;

    /// <summary>Raised after <see cref="Links"/> changes so subscribers can call StateHasChanged.</summary>
    public event Action? OnChange;

    /// <summary>
    /// Replaces cached navigation links without calling the API.
    /// </summary>
    public void SetLinks(IEnumerable<TenantNavigationLinkDto> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        _links = links.OrderBy(link => link.Order).ToList().AsReadOnly();
        _loaded = true;

        OnChange?.Invoke();
    }

    /// <summary>
    /// Loads links once per session. Subsequent calls are no-ops unless
    /// <see cref="RefreshAsync"/> has been called.
    /// </summary>
    public async Task EnsureLoadedAsync(ITenantNavigationService service, CancellationToken ct = default)
    {
        if (_loaded)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock
            if (_loaded)
                return;

            await FetchAsync(service, ct);
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Forces a reload from the API. Called after admin mutations (create/update/delete/reorder).
    /// </summary>
    public async Task RefreshAsync(ITenantNavigationService service, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await FetchAsync(service, ct);
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }

        OnChange?.Invoke();
    }

    private async Task FetchAsync(ITenantNavigationService service, CancellationToken ct)
    {
        try
        {
            var result = await service.GetNavigationLinksAsync();
            _links = result?.OrderBy(l => l.Order).ToList().AsReadOnly()
                     ?? (IReadOnlyList<TenantNavigationLinkDto>)[];
        }
        catch
        {
            // Navigation links are optional — fail silently
            _links = [];
        }
    }

    public void Dispose()
    {
        // Intentionally no-op: this scoped Blazor UI state can still have async
        // continuations completing during prerender/circuit teardown. Disposing
        // SemaphoreSlim in that window can surface ObjectDisposedException to the
        // global ErrorBoundary even though navigation links are best-effort UI.
    }
}
