using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.Caching.Memory;

namespace Explore.Blazor.Services;

public interface IBffResolverConfigurationProvider
{
    Task<ResolverConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default);
}

public sealed class BffResolverConfigurationProvider(
    IInstanceMessagingSettingsClient apiClient,
    IMemoryCache cache,
    ILogger<BffResolverConfigurationProvider> logger) : IBffResolverConfigurationProvider
{
    private const string CacheKey = "BffResolverConfiguration";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<ResolverConfigurationDto> GetConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<ResolverConfigurationDto>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var configuration = await apiClient.GetInstanceResolverConfigurationAsync(
                cancellationToken: cancellationToken);
            cache.Set(CacheKey, configuration, CacheDuration);
            return configuration;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Resolver configuration API read failed with {FailureType}; using the safe path-routing default.",
                ex.GetType().Name);
            return CreateFallback();
        }
    }

    private static ResolverConfigurationDto CreateFallback() => new()
    {
        HeaderEnabled = true,
        PathEnabled = true,
        PathPrefix = "/t",
        SubdomainEnabled = false,
        CustomDomainEnabled = false,
        InstanceBaseDomain = string.Empty,
        AllowTenantCustomDomains = false
    };
}
