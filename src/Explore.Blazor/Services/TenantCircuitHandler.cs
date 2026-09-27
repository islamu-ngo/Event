using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Explore.Blazor.Services;

public class TenantCircuitHandler : CircuitHandler
{
    private readonly ITenantRouteContextAccessor _tenantRouteContextAccessor;
    private readonly NavigationManager _navigationManager;
    private readonly IBffResolverConfigurationProvider _resolverConfigurationProvider;
    private readonly Explore.Blazor.Client.Models.OnboardingRequestOrigin _requestOrigin;
    private PathString _deploymentPathBase;
    private string? _pathPrefix;
    private IReadOnlyCollection<string>? _reservedSlugs;
    private bool _pathEnabled;
    private bool _subscribed;

    public TenantCircuitHandler(
        ITenantRouteContextAccessor tenantRouteContextAccessor,
        NavigationManager navigationManager,
        IBffResolverConfigurationProvider resolverConfigurationProvider,
        Explore.Blazor.Client.Models.OnboardingRequestOrigin requestOrigin)
    {
        // Retain the scoped request snapshot while the circuit's initial HTTP context exists.
        _requestOrigin = requestOrigin;
        _tenantRouteContextAccessor = tenantRouteContextAccessor;
        _navigationManager = navigationManager;
        _resolverConfigurationProvider = resolverConfigurationProvider;
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var configuration = await _resolverConfigurationProvider.GetConfigurationAsync(cancellationToken);
        _pathEnabled = configuration.PathEnabled == true;
        _pathPrefix = configuration.PathPrefix;
        _reservedSlugs = configuration.ReservedSlugs as IReadOnlyCollection<string> ??
            configuration.ReservedSlugs?.ToArray();
        var deploymentPath = _requestOrigin.Url is null
            ? string.Empty
            : new Uri(_requestOrigin.Url).AbsolutePath.TrimEnd('/');
        var initialTenantSlug = _tenantRouteContextAccessor.TenantSlug;
        if (!string.IsNullOrEmpty(initialTenantSlug))
        {
            var prefix = _pathPrefix?.Trim().Trim('/') ?? string.Empty;
            var tenantRoute = (prefix.Length > 0 ? "/" + prefix : string.Empty) + "/" + initialTenantSlug;
            if (deploymentPath.EndsWith(tenantRoute, StringComparison.OrdinalIgnoreCase))
            {
                deploymentPath = deploymentPath[..^tenantRoute.Length];
            }
        }

        _deploymentPathBase = new PathString(deploymentPath);
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
        if (!_pathEnabled || !Uri.TryCreate(location, UriKind.Absolute, out var uri))
        {
            _tenantRouteContextAccessor.Clear();
            return;
        }

        var path = new PathString(uri.AbsolutePath);
        if (_deploymentPathBase.HasValue &&
            !path.StartsWithSegments(_deploymentPathBase, out path))
        {
            _tenantRouteContextAccessor.Clear();
            return;
        }

        if (!TenantRoutePathMatcher.TryMatch(
                path,
                _pathPrefix,
                out var tenantSlug,
                out _,
                out _,
                _reservedSlugs))
        {
            _tenantRouteContextAccessor.Clear();
            return;
        }

        _tenantRouteContextAccessor.SetTenantSlug(tenantSlug);
    }
}
