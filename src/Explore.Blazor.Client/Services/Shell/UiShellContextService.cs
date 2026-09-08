using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Microsoft.AspNetCore.Components.Authorization;

namespace Explore.Blazor.Client.Services.Shell;

public sealed class UiShellContextService : IUiShellContextService, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IUiShellClient _apiClient;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly CurrentUserState _currentUserState;
    private readonly ILogger<UiShellContextService> _logger;
    private UiShellContextDto? _cachedContext;
    private DateTimeOffset _cacheExpiresAt;
    private bool _disposed;

    public UiShellContextService(
        IUiShellClient apiClient,
        AuthenticationStateProvider authStateProvider,
        CurrentUserState currentUserState,
        ILogger<UiShellContextService> logger)
    {
        _apiClient = apiClient;
        _authStateProvider = authStateProvider;
        _currentUserState = currentUserState;
        _logger = logger;
        _currentUserState.OnChanged += OnCurrentUserChanged;
    }

    public async Task<UiShellContextDto?> GetContextAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAuthenticatedAsync())
        {
            return null;
        }

        try
        {
            var context = await _apiClient.GetUiShellContextAsync(cancellationToken: cancellationToken);
            CacheContext(context);
            return context;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Failed to load UI shell context. Status: {StatusCode}.",
                ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load UI shell context.");
            return null;
        }
    }

    public async Task<UiShellContextDto?> GetCachedContextAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAuthenticatedAsync())
        {
            return null;
        }

        if (_cachedContext is not null && DateTimeOffset.UtcNow <= _cacheExpiresAt)
        {
            return _cachedContext;
        }

        return await GetContextAsync(cancellationToken);
    }

    public void ResetCache()
    {
        _cachedContext = null;
        _cacheExpiresAt = default;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _currentUserState.OnChanged -= OnCurrentUserChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnCurrentUserChanged() => ResetCache();

    private async Task<bool> IsAuthenticatedAsync()
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            return authState.User.Identity?.IsAuthenticated == true;
        }
        catch
        {
            return false;
        }
    }

    private void CacheContext(UiShellContextDto? context)
    {
        _cachedContext = context;
        _cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
    }
}
