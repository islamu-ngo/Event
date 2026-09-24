using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Keycloak;

public sealed class KeycloakConnectionOwnershipPersistenceTests
{
    [Test]
    public async Task DeploymentOwnershipWithoutResolvedBinding_IsNotReportedConfigured()
    {
        await using EventVisitorCapabilitySqliteFixture fixture =
            await EventVisitorCapabilitySqliteFixture.CreateAsync();
        AuthProviderConfigurationService service = CreateService(
            fixture,
            new StaticSecretResolver(
                new Dictionary<string, string>(StringComparer.Ordinal),
                SecretResolutionResult.Unconfigured));

        AuthProviderConfigurationDto result = await service.ReadConfigurationAsync();

        await Assert.That(result.KeycloakClientSecretOwnership.Mode)
            .IsEqualTo("deployment-managed");
        await Assert.That(result.KeycloakClientSecretOwnership.Configured).IsFalse();
    }

    [Test]
    public async Task ResolvedSelectedBinding_IsReportedConfiguredWithoutDatabaseSecret()
    {
        await using EventVisitorCapabilitySqliteFixture fixture =
            await EventVisitorCapabilitySqliteFixture.CreateAsync();
        string secretCanary = $"runtime-{Guid.CreateVersion7():N}";
        AuthProviderConfigurationService service = CreateService(
            fixture,
            new StaticSecretResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [SecretDefinitionRegistry.Keys.Keycloak.Endpoint] =
                        "https://identity.example.test",
                    [SecretDefinitionRegistry.Keys.Keycloak.Realm] = "operators",
                    [SecretDefinitionRegistry.Keys.Keycloak.ClientId] = "event-bff",
                    [SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret] =
                        secretCanary
                }));

        AuthProviderConfigurationDto result = await service.ReadConfigurationAsync();

        await Assert.That(result.KeycloakClientSecretOwnership.Configured).IsTrue();
        await Assert.That(result.KeycloakClientSecret).IsEmpty();
        await Assert.That(result.ToString()).DoesNotContain(secretCanary);
    }

    private static AuthProviderConfigurationService CreateService(
        EventVisitorCapabilitySqliteFixture fixture,
        ISecretResolver secretResolver)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var unitOfWork = new EfCoreUnitOfWork(fixture.Context);
        var mutationLock = new RelationalSettingMutationLock(fixture.Context, unitOfWork);
        var writer = new VisitorAccessSettingsWriter(
            fixture.Context,
            mutationLock,
            unitOfWork,
            new EventParticipationConfigurationRepository(fixture.Context),
            configuration);
        return new AuthProviderConfigurationService(
            new SystemSettingRepository(fixture.Context, mutationLock),
            configuration,
            unitOfWork,
            mutationLock,
            writer,
            new KeycloakConnectionResolver(secretResolver, configuration),
            fixture.Services.GetServices<INotificationHandler<SettingChangedNotification>>());
    }

    private sealed class StaticSecretResolver(
        IReadOnlyDictionary<string, string> values,
        SecretResolutionResult? missing = null) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            if (!values.TryGetValue(settingKey, out string? value))
            {
                return Task.FromResult(missing ?? SecretResolutionResult.Unconfigured);
            }

            return Task.FromResult(SecretResolutionResult.Resolved(
                new ResolvedSecret(
                    settingKey,
                    value,
                    SecretSourceType.EnvironmentVariable,
                    SecretScope.Instance,
                    ScopeId: null,
                    DateTimeOffset.UtcNow)));
        }

        public Task<SecretResolutionResult> ResolveQualifiedAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            string qualifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(
            Guid tenantId,
            Guid bindingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
