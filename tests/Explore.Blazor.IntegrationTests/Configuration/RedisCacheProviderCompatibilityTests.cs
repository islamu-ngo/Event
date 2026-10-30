using Explore.Blazor.IntegrationTests.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Explore.Blazor.IntegrationTests.Configuration;

[ClassDataSource<RedisBffTicketStoreTests.Authority>(Shared = SharedType.PerClass)]
public sealed class RedisCacheProviderCompatibilityTests(RedisBffTicketStoreTests.Authority authority)
{
    [Test]
    public async Task AdmittedServerSupportsNativeCacheValuesSlidingRefreshAndRemoval()
    {
        var selected = authority.Options();
        var endpoint = new Uri(selected.Endpoint!);
        var connection = new ConfigurationOptions { Password = selected.Password };
        connection.EndPoints.Add(endpoint.Host, endpoint.Port);
        string prefix = $"cache-compatibility:{Guid.NewGuid():N}:";
        using var cache = new RedisCache(Options.Create(new RedisCacheOptions
        {
            ConfigurationOptions = connection,
            InstanceName = prefix
        }));
        const string key = "entry";
        const string value = "native-cache-provider-value";
        var database = authority.Connection.GetDatabase();
        RedisKey storedKey = prefix + key;
        TimeSpan ttlRoundingAllowance = TimeSpan.FromSeconds(1);

        await cache.SetStringAsync(key, value, new DistributedCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5)
        });
        await Assert.That(await database.KeyExistsAsync(storedKey)).IsTrue();
        await Assert.That(await cache.GetStringAsync(key)).IsEqualTo(value);

        await Assert.That(await database.KeyExpireAsync(storedKey, TimeSpan.FromSeconds(30))).IsTrue();
        TimeSpan? shortened = await database.KeyTimeToLiveAsync(storedKey);
        await Assert.That(shortened is { } before
            && before > TimeSpan.Zero && before <= TimeSpan.FromSeconds(30) + ttlRoundingAllowance).IsTrue();
        await cache.RefreshAsync(key);
        TimeSpan? refreshed = await database.KeyTimeToLiveAsync(storedKey);
        await Assert.That(refreshed is { } after
            && after > TimeSpan.FromMinutes(4) && after <= TimeSpan.FromMinutes(5) + ttlRoundingAllowance).IsTrue();

        await cache.RemoveAsync(key);
        await Assert.That(await cache.GetStringAsync(key)).IsNull();
        await Assert.That(await database.KeyExistsAsync(storedKey)).IsFalse();
    }
}
