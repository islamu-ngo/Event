using Explore.ServiceDefaults.HealthChecks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Extensions;

public static class DistributedCacheRegistrationExtensions
{
    public static WebApplicationBuilder AddResilientDistributedCache(
        this WebApplicationBuilder builder,
        string connectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        var connectionString = builder.Configuration.GetConnectionString(connectionName);

        using var bootstrapLoggerFactory = LoggerFactory.Create(static logging =>
        {
            logging.AddSimpleConsole(static options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss.fff ";
            });
            logging.SetMinimumLevel(LogLevel.Information);
        });
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Explore.Blazor.Cache");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddDistributedMemoryCache();
            bootstrapLogger.LogWarning(
                "Distributed cache backend: memory. No ConnectionStrings:{ConnectionName} value was configured; Redis is optional for local and degraded deployments.",
                connectionName);
            return builder;
        }

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = "Explore.Blazor:";
        });

        var redisDescriptor = builder.Services.Last(descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        builder.Services.Remove(redisDescriptor);

        builder.Services.TryAddSingleton<MemoryDistributedCache>();
        builder.Services.AddSingleton(sp => new RedisDistributedCachePrimary(
            (IDistributedCache)CreateService(redisDescriptor, sp)));
        builder.Services.AddSingleton<RedisFallbackDistributedCache>();
        builder.Services.AddSingleton<IDistributedCache>(sp => sp.GetRequiredService<RedisFallbackDistributedCache>());
        builder.Services.AddSingleton<IDistributedCacheBackendState>(sp => sp.GetRequiredService<RedisFallbackDistributedCache>());
        builder.Services.AddHostedService<DistributedCacheStartupProbe>();

        bootstrapLogger.LogInformation(
            "Distributed cache backend: Redis with in-memory fallback. ConnectionStrings:{ConnectionName} is configured.",
            connectionName);
        return builder;
    }

    private static object CreateService(ServiceDescriptor descriptor, IServiceProvider serviceProvider)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(serviceProvider);
        }

        if (descriptor.ImplementationType is not null)
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);
        }

        throw new InvalidOperationException("Unable to create the Redis distributed cache service.");
    }

    private sealed class RedisDistributedCachePrimary(IDistributedCache cache)
    {
        public IDistributedCache Cache { get; } = cache;
    }

    private sealed class RedisFallbackDistributedCache(
        RedisDistributedCachePrimary primary,
        MemoryDistributedCache fallback,
        ILogger<RedisFallbackDistributedCache> logger) : IDistributedCache, IDistributedCacheBackendState
    {
        private int fallbackActivated;

        public string BackendName => "redis";

        public bool IsConfigured => true;

        public bool IsDegraded => IsFallbackActive();

        public string Status => IsFallbackActive()
            ? "Redis is unavailable; in-memory fallback is active."
            : "Redis is active.";

        public byte[]? Get(string key)
        {
            if (IsFallbackActive())
            {
                return fallback.Get(key);
            }

            try
            {
                return primary.Cache.Get(key);
            }
            catch (Exception ex)
            {
                ActivateFallback(ex);
                return fallback.Get(key);
            }
        }

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            if (IsFallbackActive())
            {
                return await fallback.GetAsync(key, token).ConfigureAwait(false);
            }

            try
            {
                return await primary.Cache.GetAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ActivateFallback(ex);
                return await fallback.GetAsync(key, token).ConfigureAwait(false);
            }
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            fallback.Set(key, value, options);

            if (IsFallbackActive())
            {
                return;
            }

            try
            {
                primary.Cache.Set(key, value, options);
            }
            catch (Exception ex)
            {
                ActivateFallback(ex);
            }
        }

        public async Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            await fallback.SetAsync(key, value, options, token).ConfigureAwait(false);

            if (IsFallbackActive())
            {
                return;
            }

            try
            {
                await primary.Cache.SetAsync(key, value, options, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ActivateFallback(ex);
            }
        }

        public void Refresh(string key)
        {
            fallback.Refresh(key);

            if (IsFallbackActive())
            {
                return;
            }

            try
            {
                primary.Cache.Refresh(key);
            }
            catch (Exception ex)
            {
                ActivateFallback(ex);
            }
        }

        public async Task RefreshAsync(string key, CancellationToken token = default)
        {
            await fallback.RefreshAsync(key, token).ConfigureAwait(false);

            if (IsFallbackActive())
            {
                return;
            }

            try
            {
                await primary.Cache.RefreshAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ActivateFallback(ex);
            }
        }

        public void Remove(string key)
        {
            fallback.Remove(key);

            if (IsFallbackActive())
            {
                return;
            }

            try
            {
                primary.Cache.Remove(key);
            }
            catch (Exception ex)
            {
                ActivateFallback(ex);
            }
        }

        public async Task RemoveAsync(string key, CancellationToken token = default)
        {
            await fallback.RemoveAsync(key, token).ConfigureAwait(false);

            if (IsFallbackActive())
            {
                return;
            }

            try
            {
                await primary.Cache.RemoveAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ActivateFallback(ex);
            }
        }

        private bool IsFallbackActive() => Volatile.Read(ref fallbackActivated) == 1;

        private void ActivateFallback(Exception exception)
        {
            if (Interlocked.Exchange(ref fallbackActivated, 1) == 0)
            {
                logger.LogWarning(
                    exception,
                    "Redis distributed cache is unavailable; degraded to in-memory cache for this Blazor BFF instance.");
            }
        }
    }

    private sealed class DistributedCacheStartupProbe(
        IDistributedCache cache,
        ILogger<DistributedCacheStartupProbe> logger) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            const string probeKey = "startup:distributed-cache-probe";
            await cache.SetStringAsync(
                probeKey,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                },
                cancellationToken).ConfigureAwait(false);
            await cache.RemoveAsync(probeKey, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Distributed cache startup probe completed.");
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
