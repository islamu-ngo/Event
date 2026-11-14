using Explore.Blazor.Client.Clients;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;

namespace Explore.Blazor.Client.Services;

public interface IFooterPresentationService
{
    event Action? Changed;
    Task<FooterConfigDto?> GetAsync(Guid? selectedTenant);
    void ResetCache();
}

/// <summary>Owns footer loading for the current browser context and rejects superseded responses.</summary>
public sealed class FooterPresentationService : IFooterPresentationService, IDisposable
{
    private readonly IFooterClient client;
    private readonly NavigationManager navigation;
    private readonly IPublicExperienceService settings;
    private readonly AuthenticationStateProvider authentication;
    private readonly ILogger<FooterPresentationService> logger;
    private readonly TimeProvider clock;
    private FooterConfigDto? cached;
    private Task<FooterConfigDto?>? loading;
    private DateTimeOffset expires;
    private Guid? tenant;
    private long generation;
    private bool disposed;

    public event Action? Changed;

    public FooterPresentationService(IFooterClient client, NavigationManager navigation,
        IPublicExperienceService settings, AuthenticationStateProvider authentication,
        ILogger<FooterPresentationService> logger, TimeProvider clock)
    {
        this.client = client;
        this.navigation = navigation;
        this.settings = settings;
        this.authentication = authentication;
        this.logger = logger;
        this.clock = clock;
        navigation.LocationChanged += OnLocationChanged;
        settings.SettingsChanged += ResetCache;
        authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    public async Task<FooterConfigDto?> GetAsync(Guid? selectedTenant)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (tenant != selectedTenant)
        {
            Invalidate();
            tenant = selectedTenant;
        }
        if (cached is not null && clock.GetUtcNow() < expires)
            return cached;
        var pending = loading ??= LoadAsync(generation);
        try { return await pending; }
        finally
        {
            if (ReferenceEquals(loading, pending))
                loading = null;
        }
    }

    private async Task<FooterConfigDto?> LoadAsync(long selectedGeneration)
    {
        try
        {
            var response = await client.GetFooterConfigAsync(cancellationToken: CancellationToken.None);
            if (disposed || selectedGeneration != generation)
                return null;
            cached = response;
            expires = clock.GetUtcNow().AddMinutes(5);
            return response;
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Footer configuration is unavailable. Status: {StatusCode}.", exception.StatusCode);
            return null;
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Footer configuration transport is unavailable.");
            return null;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Footer configuration transport timed out.");
            return null;
        }
    }

    public void ResetCache()
    {
        if (disposed) return;
        Invalidate();
        Changed?.Invoke();
    }

    private void Invalidate()
    {
        generation++;
        cached = null;
        expires = default;
        loading = null;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        if (!disposed) Changed?.Invoke();
    }
    private void OnAuthenticationChanged(Task<AuthenticationState> state) => ResetCache();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Invalidate();
        navigation.LocationChanged -= OnLocationChanged;
        settings.SettingsChanged -= ResetCache;
        authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
        GC.SuppressFinalize(this);
    }
}
