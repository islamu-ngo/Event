using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace Event.Application.UnitTests.Services;

public sealed class AuthProviderConfigurationServiceTests
{
    [Test]
    public async Task ReadConfigurationResolvesNormalizedPrimaryProviderMetadata()
    {
        var repository = new InMemorySystemSettingRepository();
        repository.Seed(new SystemSetting
        {
            SettingKey = GovernanceSettingKeys.Authentication.PrimaryProviderId,
            Value = ((int)AuthenticationProviderKind.Local).ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        });
        var service = CreateService(repository);

        AuthProviderConfigurationDto result = await service.ReadConfigurationAsync();

        await Assert.That(result.PrimaryProviderId)
            .IsEqualTo((int)AuthenticationProviderKind.Local);
        await Assert.That(result.PrimaryProviderCode).IsEqualTo("local");
        await Assert.That(result.PrimaryProviderName).IsEqualTo("Local Identity");
    }

    [Test]
    public async Task ApplyConfigurationPersistsPrimaryProviderAsLookupIdentifier()
    {
        var repository = new InMemorySystemSettingRepository();
        var service = CreateService(repository);

        await service.ApplyConfigurationAsync(new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Local,
            PrimaryProviderCode = "local",
            PrimaryProviderName = "Local Identity"
        });

        SystemSetting persisted = repository.Require(
            GovernanceSettingKeys.Authentication.PrimaryProviderId);
        await Assert.That(persisted.Value)
            .IsEqualTo(((int)AuthenticationProviderKind.Local).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task ReadConfigurationForAtprotoPrimaryForcesAtprotoLoginEnabled()
    {
        var repository = new InMemorySystemSettingRepository();
        repository.Seed(new SystemSetting
        {
            SettingKey = GovernanceSettingKeys.Authentication.PrimaryProviderId,
            Value = ((int)AuthenticationProviderKind.Atproto).ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        });
        repository.Seed(new SystemSetting
        {
            SettingKey = GovernanceSettingKeys.Authentication.AtprotoLoginEnabled,
            Value = "false"
        });
        var service = CreateService(repository);

        AuthProviderConfigurationDto result =
            await service.ReadConfigurationAsync();

        await Assert.That(result.PrimaryProviderId)
            .IsEqualTo((int)AuthenticationProviderKind.Atproto);
        await Assert.That(result.PrimaryProviderCode).IsEqualTo("atproto");
        await Assert.That(result.PrimaryProviderName).IsEqualTo("AT Protocol");
        await Assert.That(result.AtprotoLoginEnabled).IsTrue();
    }

    [Test]
    public async Task ApplyAtprotoPrimaryPersistsEnabledAtprotoAxis()
    {
        var repository = new InMemorySystemSettingRepository();
        var service = CreateService(repository);

        await service.ApplyConfigurationAsync(new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Atproto,
            PrimaryProviderCode = "atproto",
            PrimaryProviderName = "AT Protocol",
            AtprotoLoginEnabled = false,
            AtprotoPublicUrl = "https://events.example.test"
        });

        await Assert.That(repository.Require(
                GovernanceSettingKeys.Authentication.AtprotoLoginEnabled).Value)
            .IsEqualTo("true");
    }

    private static AuthProviderConfigurationService CreateService(
        ISystemSettingRepository repository) =>
        new(
            repository,
            new ConfigurationBuilder().Build(),
            new PassThroughUnitOfWork());

    private sealed class InMemorySystemSettingRepository : ISystemSettingRepository
    {
        private readonly Dictionary<string, SystemSetting> _settings =
            new(StringComparer.Ordinal);

        internal void Seed(SystemSetting setting) => _settings[setting.SettingKey] = setting;

        internal SystemSetting Require(string key) => _settings[key];

        public Task<SystemSetting?> GetByKey(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings.GetValueOrDefault(key));

        public Task<string?> UpsertAsync(
            SystemSetting setting,
            CancellationToken cancellationToken = default)
        {
            _settings[setting.SettingKey] = setting;
            return Task.FromResult<string?>(setting.SettingKey);
        }

        public Task<string?> UpsertInCurrentTransactionAsync(
            SystemSetting setting,
            CancellationToken cancellationToken = default) =>
            UpsertAsync(setting, cancellationToken);

        public Task<string?> UpsertLockAsync(
            SystemSetting setting,
            CancellationToken cancellationToken = default) =>
            UpsertAsync(setting, cancellationToken);

        public Task<List<SystemSetting>> GetAllSettings(
            string? category = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings.Values
                .Where(setting => category is null || setting.Category == category)
                .ToList());

        public Task<bool> IsLocked(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings.GetValueOrDefault(key)?.IsLocked == true);
    }

    private sealed class PassThroughUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) =>
            operation(ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) =>
            operation(ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) =>
            operation(ct);
    }
}
