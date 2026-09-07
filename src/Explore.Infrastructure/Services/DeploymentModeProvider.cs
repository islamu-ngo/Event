using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public sealed class DeploymentModeProvider : IDeploymentModeProvider
{
    internal const string CacheKey = "DeploymentMode:Current";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IOptionsMonitor<DeploymentSettings> _settings;
    private readonly IConfiguration _configuration;
    private readonly IDistributedCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeploymentModeProvider> _logger;

    public DeploymentModeProvider(
        IOptionsMonitor<DeploymentSettings> settings,
        IConfiguration configuration,
        IDistributedCache cache,
        IServiceScopeFactory scopeFactory,
        ILogger<DeploymentModeProvider> logger)
    {
        _settings = settings;
        _configuration = configuration;
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<DeploymentMode> GetCurrentModeAsync(CancellationToken ct = default)
    {
        // Layer 1: distributed cache
        if (!IsCacheDisabled())
        {
            var cached = await TryGetCachedModeAsync(ct);
            if (cached is not null)
                return cached.Value;
        }

        // Layer 2: database (scoped repo accessed via factory)
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider
            .GetRequiredService<IInstanceBootstrapStateRepository>();
        var bootstrap = await repo.GetCurrent(ct);

        // Pre-onboarding (fresh install): InstanceBootstrapState is null or incomplete.
        // Return SingleTenant so ApiTenantResolutionMiddleware falls back to the default tenant
        // rather than 404'ing every request. The configured onboarding mode is exposed separately
        // and persisted when onboarding completes.
        if (bootstrap?.Status != InstanceBootstrapStatus.Completed)
        {
            if (!IsCacheDisabled())
            {
                await TrySetCachedModeAsync(DeploymentMode.SingleTenant, ct);
            }

            _logger.LogDebug("Deployment mode resolved from DB (pre-onboarding fallback): SingleTenant");
            return DeploymentMode.SingleTenant;
        }

        // Post-onboarding: trust the typed persisted selection.
        var mode = bootstrap.DeploymentMode;

        if (!IsCacheDisabled())
        {
            await TrySetCachedModeAsync(mode, ct);
        }

        _logger.LogDebug("Deployment mode resolved from DB: {Mode}", mode);
        return mode;
    }

    public Task<DeploymentMode> GetConfiguredOnboardingModeAsync(CancellationToken ct = default)
        => Task.FromResult(ResolveConfiguredMode());

    public async Task<bool> IsSingleTenantAsync(CancellationToken ct = default)
        => await GetCurrentModeAsync(ct) == DeploymentMode.SingleTenant;

    public async Task InvalidateCacheAsync()
        => await TryRemoveCachedModeAsync();

    private bool IsCacheDisabled()
    {
        return _configuration.GetValue<bool>("Deployment:DisableModeCache")
            || _configuration.GetValue<bool>("Testing:DisableDeploymentModeCache");
    }

    private async Task<DeploymentMode?> TryGetCachedModeAsync(CancellationToken ct)
    {
        try
        {
            var cached = await _cache.GetStringAsync(CacheKey, ct);
            return cached is not null && Enum.TryParse<DeploymentMode>(cached, out var cachedMode)
                ? cachedMode
                : null;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Distributed cache unavailable while resolving deployment mode. Falling back to database.");
            return null;
        }
    }

    private async Task TrySetCachedModeAsync(DeploymentMode mode, CancellationToken ct)
    {
        try
        {
            await _cache.SetStringAsync(
                CacheKey,
                mode.ToString(),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl
                },
                ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Distributed cache unavailable while storing deployment mode. Continuing without cache.");
        }
    }

    private async Task TryRemoveCachedModeAsync()
    {
        try
        {
            await _cache.RemoveAsync(CacheKey);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Distributed cache unavailable while invalidating deployment mode cache.");
        }
    }

    private DeploymentMode ResolveConfiguredMode()
    {
        var configuredMode = _configuration["Deployment:Mode"];
        if (TryParseDeploymentMode(configuredMode, out var parsedMode))
        {
            return parsedMode;
        }

        return _settings.CurrentValue.Mode;
    }

    private static bool TryParseDeploymentMode(string? rawValue, out DeploymentMode mode)
    {
        mode = DeploymentMode.SingleTenant;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return Enum.TryParse(normalized, ignoreCase: true, out mode);
    }
}
