using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Explore.Blazor.Services;

public class TenantCircuitHandler : CircuitHandler
{
    private readonly ITenantRouteContextAccessor _tenantRouteContextAccessor;
    private readonly NavigationManager _navigationManager;
    private readonly IBffResolverConfigurationProvider _resolverConfigurationProvider;
    private string? _pathPrefix;
    private bool _pathEnabled;
    private bool _subscribed;

    public TenantCircuitHandler(
        ITenantRouteContextAccessor tenantRouteContextAccessor,
        NavigationManager navigationManager,
        IBffResolverConfigurationProvider resolverConfigurationProvider)
    {
        _tenantRouteContextAccessor = tenantRouteContextAccessor;
        _navigationManager = navigationManager;
        _resolverConfigurationProvider = resolverConfigurationProvider;
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var configuration = await _resolverConfigurationProvider.GetConfigurationAsync(cancellationToken);
        _pathEnabled = configuration.PathEnabled == true;
        _pathPrefix = configuration.PathPrefix;
        UpdateTenantSlug(_navigationManager.Uri);

        if (!_subscribed)
        {
            _navigationManager.LocationChanged += OnLocationChanged;
            _subscribed = true;
        }
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            _navigationManager.LocationChanged -= OnLocationChanged;
            _subscribed = false;
        }

        _tenantRouteContextAccessor.Clear();
        return Task.CompletedTask;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
    {
        return async context =>
        {
            UpdateTenantSlug(_navigationManager.Uri);
            using var tenantScope = _tenantRouteContextAccessor.BeginActivityScope();
            await next(context);
        };
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        UpdateTenantSlug(args.Location);
    }

    private void UpdateTenantSlug(string location)
    {
        if (!_pathEnabled ||
            !Uri.TryCreate(location, UriKind.Absolute, out var uri) ||
            !TenantRoutePathMatcher.TryMatch(
                new PathString(uri.AbsolutePath),
                _pathPrefix,
                out var tenantSlug,
                out _,
                out _))
        {
            _tenantRouteContextAccessor.Clear();
            return;
        }

        _tenantRouteContextAccessor.SetTenantSlug(tenantSlug);
    }
}
