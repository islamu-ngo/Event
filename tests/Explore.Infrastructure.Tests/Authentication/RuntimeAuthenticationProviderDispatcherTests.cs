using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Authentication;

public sealed class RuntimeAuthenticationProviderDispatcherTests
{
    [Test]
    public async Task DeploymentProviderOverridesPersistedModeWithoutDatabaseRead()
    {
        var repository = new InMemorySystemSettingRepository
        {
            ThrowOnRead = true
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = new RuntimeAuthenticationProviderDispatcher(
            repository,
            cache,
            Options.Create(new AuthenticationProviderDeploymentOptions
            {
                Provider = "keycloak"
            }));

        AuthenticationProviderKind result =
            await dispatcher.GetActivePrimaryProviderAsync(CancellationToken.None);

        await Assert.That(result).IsEqualTo(AuthenticationProviderKind.Keycloak);
        await Assert.That(repository.ReadCount).IsEqualTo(0);
    }

    [Test]
    public async Task AtprotoDeploymentProviderOverridesDatabaseWithoutReadingIt()
    {
        var repository = new InMemorySystemSettingRepository
        {
            ThrowOnRead = true
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = new RuntimeAuthenticationProviderDispatcher(
            repository,
            cache,
            Options.Create(new AuthenticationProviderDeploymentOptions
            {
                Provider = "atproto",
                AtprotoLoginEnabled = true
            }));

        AuthenticationProviderKind result =
            await dispatcher.GetActivePrimaryProviderAsync(CancellationToken.None);

        await Assert.That(result).IsEqualTo(AuthenticationProviderKind.Atproto);
        await Assert.That(repository.ReadCount).IsEqualTo(0);
    }

    [Test]
    public async Task PersistedModeRemainsCachedUntilExplicitInvalidation()
    {
        var repository = new InMemorySystemSettingRepository();
        repository.Set(AuthenticationProviderKind.Local);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = new RuntimeAuthenticationProviderDispatcher(
            repository,
            cache,
            Options.Create(new AuthenticationProviderDeploymentOptions()));

        AuthenticationProviderKind initial =
            await dispatcher.GetActivePrimaryProviderAsync(CancellationToken.None);
        repository.Set(AuthenticationProviderKind.Keycloak);
        AuthenticationProviderKind cached =
            await dispatcher.GetActivePrimaryProviderAsync(CancellationToken.None);
        dispatcher.InvalidateInstanceMode();
        AuthenticationProviderKind refreshed =
            await dispatcher.GetActivePrimaryProviderAsync(CancellationToken.None);

        await Assert.That(initial).IsEqualTo(AuthenticationProviderKind.Local);
        await Assert.That(cached).IsEqualTo(AuthenticationProviderKind.Local);
        await Assert.That(refreshed).IsEqualTo(AuthenticationProviderKind.Keycloak);
        await Assert.That(repository.ReadCount).IsEqualTo(2);
    }

    [Test]
    public async Task PersistedAtprotoPrimaryProviderIsSupported()
    {
        var repository = new InMemorySystemSettingRepository();
        repository.Set(AuthenticationProviderKind.Atproto);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = new RuntimeAuthenticationProviderDispatcher(
            repository,
            cache,
            Options.Create(new AuthenticationProviderDeploymentOptions()));

        AuthenticationProviderKind result =
            await dispatcher.GetActivePrimaryProviderAsync(CancellationToken.None);

        await Assert.That(result).IsEqualTo(AuthenticationProviderKind.Atproto);
    }

    [Test]
    public async Task UnsupportedPersistedPrimaryProviderFailsClosed()
    {
        var repository = new InMemorySystemSettingRepository();
        repository.Set(AuthenticationProviderKind.Google);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var dispatcher = new RuntimeAuthenticationProviderDispatcher(
            repository,
            cache,
            Options.Create(new AuthenticationProviderDeploymentOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.GetActivePrimaryProviderAsync(CancellationToken.None));
    }

    private sealed class InMemorySystemSettingRepository : ISystemSettingRepository
    {
        private SystemSetting? _setting;

        internal bool ThrowOnRead { get; init; }
        internal int ReadCount { get; private set; }

        internal void Set(AuthenticationProviderKind provider) =>
            _setting = new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Authentication.PrimaryProviderId,
                Value = ((int)provider).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            };

        public Task<SystemSetting?> GetByKey(
            string key,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Simulated setting-store failure.");
            }

            return Task.FromResult(_setting);
        }

        public Task<string?> UpsertAsync(
            SystemSetting setting,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> UpsertInCurrentTransactionAsync(
            SystemSetting setting,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> UpsertLockAsync(
            SystemSetting setting,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<List<SystemSetting>> GetAllSettings(
            string? category = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsLocked(
            string key,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
