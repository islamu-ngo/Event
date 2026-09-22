using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Services;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Configuration;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class KeycloakConnectionInspectionTests
{
    [Test]
    public async Task Doctor_RejectsBrowserAudienceOverrideBeforeProviderInspection()
    {
        var handler = new RunKeycloakRealmDoctorQueryHandler(
            CreateRuntimeConnectionResolver(),
            new RejectingAdminClient());

        KeycloakRealmDoctorResultDto result = await handler.QueryAsync(
            new RunKeycloakRealmDoctorQuery
            {
                Request = new KeycloakRealmDoctorRequestDto
                {
                    ApiClientId = "event-bff"
                }
            },
            CancellationToken.None);

        await Assert.That(result.OverallStatus).IsEqualTo("blocked");
        await Assert.That(result.Checks.Single().Code)
            .IsEqualTo("keycloak_target_conflict");
    }

    [Test]
    public async Task Preview_RejectsBrowserAudienceOverrideBeforeProviderInspection()
    {
        var handler = new PreviewKeycloakRealmSyncQueryHandler(
            CreateRuntimeConnectionResolver(),
            new RejectingAdminClient(),
            KeycloakRealmDesiredStateBuilder.CreateDefault());

        KeycloakRealmSyncPlanDto result = await handler.QueryAsync(
            new PreviewKeycloakRealmSyncQuery
            {
                Request = new KeycloakRealmSyncPreviewRequestDto
                {
                    ApiClientId = string.Empty
                }
            },
            CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo("blocked");
        await Assert.That(result.Diagnostics.Single().Code)
            .IsEqualTo("keycloak_target_conflict");
    }

    [Test]
    public async Task RuntimeConnection_UsesOnlySelectedAuthorityValues()
    {
        string secretCanary = $"runtime-{Guid.CreateVersion7():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Audience"] = "event-api"
            })
            .Build();
        var resolver = new KeycloakConnectionResolver(
            new RuntimeSecretResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [SecretDefinitionRegistry.Keys.Keycloak.Endpoint] = "https://identity.example.test/base",
                    [SecretDefinitionRegistry.Keys.Keycloak.Realm] = "operators",
                    [SecretDefinitionRegistry.Keys.Keycloak.ClientId] = "event-bff",
                    [SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret] = secretCanary
                }),
            configuration);

        KeycloakConnectionResolution result = await resolver.ResolveRuntimeAsync();

        await Assert.That(result.Status).IsEqualTo(KeycloakConnectionStatus.Resolved);
        await Assert.That(result.Authority!.AbsoluteUri)
            .IsEqualTo("https://identity.example.test/base/realms/operators");
        await Assert.That(result.Realm).IsEqualTo("operators");
        await Assert.That(result.BlazorClientId).IsEqualTo("event-bff");
        await Assert.That(result.ApiClientId).IsEqualTo("event-api");
        await Assert.That(result.ClientSecret).IsEqualTo(secretCanary);
        await Assert.That(result.ToString()).DoesNotContain(secretCanary);
    }

    [Test]
    public async Task RuntimeConnection_PreservesUnavailableSelectedAuthority()
    {
        var resolver = new KeycloakConnectionResolver(
            new RuntimeSecretResolver(
                new Dictionary<string, string>(StringComparer.Ordinal),
                SecretResolutionResult.Unavailable));

        KeycloakConnectionResolution result = await resolver.ResolveRuntimeAsync();

        await Assert.That(result.Status).IsEqualTo(KeycloakConnectionStatus.Unavailable);
        await Assert.That(result.ClientSecret).IsNull();
    }

    [Test]
    public async Task RuntimeConnection_RejectsConflictingClientIds()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Audience"] = "EVENT-CLIENT"
            })
            .Build();
        var resolver = new KeycloakConnectionResolver(
            new RuntimeSecretResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [SecretDefinitionRegistry.Keys.Keycloak.Endpoint] = "https://identity.example.test",
                    [SecretDefinitionRegistry.Keys.Keycloak.Realm] = "operators",
                    [SecretDefinitionRegistry.Keys.Keycloak.ClientId] = "event-client",
                    [SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret] =
                        $"runtime-{Guid.CreateVersion7():N}"
                }),
            configuration);

        KeycloakConnectionResolution result = await resolver.ResolveRuntimeAsync();

        await Assert.That(result.Status).IsEqualTo(KeycloakConnectionStatus.Invalid);
        await Assert.That(result.ClientSecret).IsNull();
    }

    [Test]
    public async Task EmptyAdministratorForm_IsNeverSatisfiedByDeploymentSecrets()
    {
        string usernameCanary = $"admin-{Guid.CreateVersion7():N}";
        string passwordCanary = $"password-{Guid.CreateVersion7():N}";
        var resolver = new KeycloakConnectionResolver(
            new CanarySecretResolver(usernameCanary, passwordCanary));

        KeycloakAdministratorCredentialResolution result =
            await resolver.ResolveAdministratorCredentialsAsync(
                username: string.Empty,
                password: string.Empty);

        await Assert.That(result.Status).IsEqualTo(KeycloakAdministratorCredentialStatus.Missing);
        await Assert.That(result.Username).IsNull();
        await Assert.That(result.Password).IsNull();
        await Assert.That(result.ToString()).DoesNotContain(usernameCanary);
        await Assert.That(result.ToString()).DoesNotContain(passwordCanary);
    }

    [Test]
    public async Task FreshCompleteAdministratorForm_IsAcceptedForOneRequest()
    {
        string username = $"admin-{Guid.CreateVersion7():N}";
        string password = $"password-{Guid.CreateVersion7():N}";
        var resolver = new KeycloakConnectionResolver(
            new CanarySecretResolver(
                $"deployment-user-{Guid.CreateVersion7():N}",
                $"deployment-password-{Guid.CreateVersion7():N}"));

        KeycloakAdministratorCredentialResolution result =
            await resolver.ResolveAdministratorCredentialsAsync(username, password);

        await Assert.That(result.Status).IsEqualTo(KeycloakAdministratorCredentialStatus.Resolved);
        await Assert.That(result.Username).IsEqualTo(username);
        await Assert.That(result.Password).IsEqualTo(password);
        await Assert.That(result.ToString()).DoesNotContain(username);
        await Assert.That(result.ToString()).DoesNotContain(password);
    }

    [Test]
    public async Task PartialAdministratorForm_FailsClosed()
    {
        var resolver = new KeycloakConnectionResolver(
            new CanarySecretResolver(
                $"deployment-user-{Guid.CreateVersion7():N}",
                $"deployment-password-{Guid.CreateVersion7():N}"));

        KeycloakAdministratorCredentialResolution result =
            await resolver.ResolveAdministratorCredentialsAsync(
                username: $"admin-{Guid.CreateVersion7():N}",
                password: string.Empty);

        await Assert.That(result.Status).IsEqualTo(KeycloakAdministratorCredentialStatus.Invalid);
        await Assert.That(result.Username).IsNull();
        await Assert.That(result.Password).IsNull();
    }

    private static KeycloakConnectionResolver CreateRuntimeConnectionResolver()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Audience"] = "event-api"
            })
            .Build();
        return new KeycloakConnectionResolver(
            new RuntimeSecretResolver(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [SecretDefinitionRegistry.Keys.Keycloak.Endpoint] =
                        "https://identity.example.test",
                    [SecretDefinitionRegistry.Keys.Keycloak.Realm] = "operators",
                    [SecretDefinitionRegistry.Keys.Keycloak.ClientId] = "event-bff",
                    [SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret] =
                        $"runtime-{Guid.CreateVersion7():N}"
                }),
            configuration);
    }

    private sealed class CanarySecretResolver(
        string usernameCanary,
        string passwordCanary) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            string value = settingKey == SecretDefinitionRegistry.Keys.Keycloak.AdminUsername
                ? usernameCanary
                : passwordCanary;
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

    private sealed class RuntimeSecretResolver(
        IReadOnlyDictionary<string, string> values,
        SecretResolutionResult? missingResult = null) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            if (!values.TryGetValue(settingKey, out string? value))
            {
                return Task.FromResult(missingResult ?? SecretResolutionResult.Unconfigured);
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

    private sealed class RejectingAdminClient : IKeycloakAdminClient
    {
        public Task<KeycloakAdminInspectionResult> InspectAsync(
            KeycloakAdminInspectionRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Conflicting browser target reached provider inspection.");
    }
}
