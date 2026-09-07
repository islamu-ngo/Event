using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Onboarding;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class AuthorizationProviderConfigurationServiceTests
{
    [Test]
    public async Task ReadConfigurationAsync_IgnoresLegacyStoredAdminCredentials()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos"));
        repository.GetByKey(GovernanceSettingKeys.Cerbos.GrpcEndpoint)
            .Returns(CreateSetting(GovernanceSettingKeys.Cerbos.GrpcEndpoint, "https://cerbosgrpc.example.com:443"));
        repository.GetByKey(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint)
            .Returns(CreateSetting(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint, "https://tenant-cerbos.example.com:3592"));
        repository.GetByKey(InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername)
            .Returns(CreateSetting(InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername, "admin"));
        repository.GetByKey(InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword)
            .Returns(CreateSetting(InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword, "secret"));
        var service = CreateService(repository);

        var configuration = await service.ReadConfigurationAsync();

        await Assert.That(configuration.CerbosAdminEndpoint).IsEqualTo("https://tenant-cerbos.example.com:3592");
        await Assert.That(configuration.CerbosAdminUsernameConfigured).IsFalse();
        await Assert.That(configuration.CerbosAdminPasswordConfigured).IsFalse();
    }

    [Test]
    public async Task ReadConfigurationAsync_WhenAdminApiCredentialsResolve_ReturnsConfiguredFlagsWithoutSecrets()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos"));
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => SecretResolutionResult.Resolved(new ResolvedSecret(
                call.ArgAt<string>(0),
                Guid.NewGuid().ToString("N"),
                Explore.Domain.Enums.SecretSourceType.EnvironmentVariable,
                Explore.Domain.Enums.SecretScope.Instance,
                null,
                DateTimeOffset.UtcNow)));
        var service = CreateService(repository, secretResolver: secretResolver);

        var result = await service.ReadConfigurationAsync();

        await Assert.That(result.CerbosAdminUsernameConfigured).IsTrue();
        await Assert.That(result.CerbosAdminPasswordConfigured).IsTrue();
    }


    [Test]
    public async Task ReadConfigurationAsync_WhenEnvironmentEndpointExists_BootstrapsEditableApplicationManagedMetadata()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cerbos:GrpcEndpoint"] = "cerbosgrpc.openislamu.org:443"
            })
            .Build();
        var service = CreateService(repository, configuration: configuration);

        var result = await service.ReadConfigurationAsync();

        await Assert.That(result.CerbosGrpcEndpoint).IsEqualTo("cerbosgrpc.openislamu.org:443");
        await Assert.That(result.CerbosEndpointOwnership.Mode).IsEqualTo("application-managed");
        await Assert.That(result.CerbosEndpointOwnership.Source).IsEqualTo("deployment-bootstrap");
        await Assert.That(result.CerbosEndpointOwnership.Badge).IsEqualTo("Bootstrap from Deployment");
        await Assert.That(result.CerbosEndpointOwnership.Editable).IsTrue();
        await Assert.That(result.CerbosEndpointOwnership.BootstrapAvailable).IsTrue();
        await Assert.That(result.CerbosEndpointOwnership.Description)
            .Contains("If you modify them, saved application settings will be used from now on");
        await Assert.That(result.Provider).IsEqualTo("local");
        await Assert.That(result.AuthorizationProviderConfigured).IsFalse();
        await Assert.That(result.AuthorizationProviderManagedByDeployment).IsFalse();
    }

    [Test]
    public async Task ReadConfigurationAsync_WithExplicitLocalProvider_IsReadyWithoutCerbos()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "local",
                ["Cerbos:GrpcEndpoint"] = "https://unused.example.test:443"
            })
            .Build();
        var packageService = Substitute.For<IPolicyPackageService>();
        var service = CreateService(repository, configuration: configuration, packageService: packageService);

        var result = await service.ReadConfigurationAsync();
        var reconciliation = await service.ReconcileDeploymentProviderAsync();

        await Assert.That(result.Provider).IsEqualTo("local");
        await Assert.That(result.AuthorizationProviderManagedByDeployment).IsTrue();
        await Assert.That(result.AuthorizationProviderConfigured).IsTrue();
        await Assert.That(result.AuthorizationProviderBootstrapStatus).IsEqualTo(AuthorizationProviderBootstrapState.Ready);
        await Assert.That(reconciliation.Succeeded).IsTrue();
        await packageService.DidNotReceive().PublishAsync(Arg.Any<CancellationToken>());
        await packageService.DidNotReceive().PublishInstanceAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReadConfigurationAsync_WithExplicitCerbosProvider_RemainsPendingUntilServerReconciliationSucceeds()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "local"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "cerbos",
                ["Cerbos:GrpcEndpoint"] = "https://cerbos.example.test:443"
            })
            .Build();
        var state = new AuthorizationProviderBootstrapState();
        var service = CreateService(repository, configuration: configuration, bootstrapState: state);

        var pending = await service.ReadConfigurationAsync();
        state.MarkReady("cerbos", endpointVerified: true, policiesSynchronized: true, "ready");
        var ready = await service.ReadConfigurationAsync();

        await Assert.That(pending.Provider).IsEqualTo("cerbos");
        await Assert.That(pending.AuthorizationProviderManagedByDeployment).IsTrue();
        await Assert.That(pending.AuthorizationProviderConfigured).IsFalse();
        await Assert.That(pending.AuthorizationProviderBootstrapStatus).IsEqualTo(AuthorizationProviderBootstrapState.Pending);
        await Assert.That(ready.AuthorizationProviderConfigured).IsTrue();
        await Assert.That(ready.CerbosEndpointVerified).IsTrue();
        await Assert.That(ready.CerbosPoliciesSynchronized).IsTrue();
    }

    [Test]
    public async Task ReconcileDeploymentProviderAsync_WhenCanceled_RecordsSafeFailedState()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "cerbos",
                ["Cerbos:GrpcEndpoint"] = "https://cerbos.example.test:443"
            })
            .Build();
        var packageService = Substitute.For<IPolicyPackageService>();
        var service = CreateService(repository, configuration: configuration, packageService: packageService);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ReconcileDeploymentProviderAsync(cancellation.Token));
        var result = await service.ReadConfigurationAsync();

        await Assert.That(result.AuthorizationProviderBootstrapStatus)
            .IsEqualTo(AuthorizationProviderBootstrapState.Failed);
        await Assert.That(result.AuthorizationProviderConfigured).IsFalse();
        await Assert.That(result.AuthorizationProviderBootstrapMessage)
            .IsEqualTo("Automatic Cerbos setup was canceled before completion.");
        await packageService.DidNotReceive().PublishAsync(Arg.Any<CancellationToken>());
        await packageService.DidNotReceive().PublishInstanceAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BootstrapState_ConcurrentCallersShareOneReconciliation()
    {
        var state = new AuthorizationProviderBootstrapState();
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;

        async Task<Explore.Application.Authorization.AuthorizationProviderReconciliationResult> Reconcile(
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executions);
            operationStarted.TrySetResult();
            await releaseOperation.Task.WaitAsync(cancellationToken);
            return new(true, true, true, true, "ready");
        }

        var owner = state.RunSingleFlightAsync(Reconcile, CancellationToken.None);
        await operationStarted.Task;
        var joiner = state.RunSingleFlightAsync(Reconcile, CancellationToken.None);
        releaseOperation.TrySetResult();

        var results = await Task.WhenAll(owner, joiner);

        await Assert.That(executions).IsEqualTo(1);
        await Assert.That(results.All(result => result.Succeeded)).IsTrue();
    }

    [Test]
    public async Task BootstrapState_CanceledJoinerDoesNotCancelOwner()
    {
        var state = new AuthorizationProviderBootstrapState();
        var operationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;

        async Task<Explore.Application.Authorization.AuthorizationProviderReconciliationResult> Reconcile(
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref executions);
            operationStarted.TrySetResult();
            await releaseOperation.Task.WaitAsync(cancellationToken);
            return new(true, true, true, true, "ready");
        }

        var owner = state.RunSingleFlightAsync(Reconcile, CancellationToken.None);
        await operationStarted.Task;
        using var joinerCancellation = new CancellationTokenSource();
        var joiner = state.RunSingleFlightAsync(Reconcile, joinerCancellation.Token);
        joinerCancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => joiner);
        releaseOperation.TrySetResult();
        var ownerResult = await owner;

        await Assert.That(executions).IsEqualTo(1);
        await Assert.That(ownerResult.Succeeded).IsTrue();
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithDeploymentManagedProvider_RejectsApplicationOverride()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "local"
            })
            .Build();
        var service = CreateService(repository, configuration: configuration);

        await Assert.That(() => service.ApplyConfigurationAsync(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://cerbos.example.test:443"
        })).Throws<InvalidOperationException>();

        await repository.DidNotReceive().UpsertAsync(
            Arg.Any<SystemSetting>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReadConfigurationAsync_WhenDatabaseEndpointExists_DatabaseWinsOverEnvironmentBootstrap()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos"));
        repository.GetByKey(GovernanceSettingKeys.Cerbos.GrpcEndpoint)
            .Returns(CreateSetting(GovernanceSettingKeys.Cerbos.GrpcEndpoint, "https://saved-cerbos.example.com:443"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cerbos:GrpcEndpoint"] = "cerbosgrpc.openislamu.org:443"
            })
            .Build();
        var service = CreateService(repository, configuration: configuration);

        var result = await service.ReadConfigurationAsync();

        await Assert.That(result.CerbosGrpcEndpoint).IsEqualTo("https://saved-cerbos.example.com:443");
        await Assert.That(result.AuthorizationProviderConfigured).IsTrue();
        await Assert.That(result.CerbosEndpointOwnership.Source).IsEqualTo("application");
        await Assert.That(result.CerbosEndpointOwnership.BootstrapAvailable).IsFalse();
    }

    [Test]
    public async Task ReadConfigurationAsync_WhenEndpointDeploymentManaged_UsesEnvironmentAndReturnsReadOnlyMetadata()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(GovernanceSettingKeys.Cerbos.GrpcEndpoint)
            .Returns(CreateSetting(GovernanceSettingKeys.Cerbos.GrpcEndpoint, "https://saved-cerbos.example.com:443"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cerbos:GrpcEndpoint"] = "cerbosgrpc.openislamu.org:443",
                ["Secrets:Ownership:DeploymentManagedKeys:0"] = GovernanceSettingKeys.Cerbos.GrpcEndpoint
            })
            .Build();
        var service = CreateService(repository, configuration: configuration);

        var result = await service.ReadConfigurationAsync();

        await Assert.That(result.CerbosGrpcEndpoint).IsEqualTo("cerbosgrpc.openislamu.org:443");
        await Assert.That(result.CerbosEndpointOwnership.Mode).IsEqualTo("deployment-managed");
        await Assert.That(result.CerbosEndpointOwnership.Source).IsEqualTo("deployment");
        await Assert.That(result.CerbosEndpointOwnership.Badge).IsEqualTo("Managed by Deployment");
        await Assert.That(result.CerbosEndpointOwnership.Editable).IsFalse();
        await Assert.That(result.CerbosEndpointOwnership.BootstrapAvailable).IsFalse();
    }

    [Test]
    public async Task ApplyConfigurationAsync_WhenEndpointAndCredentialsDeploymentManaged_DoesNotPersistOwnedValues()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Ownership:DeploymentManagedKeys:0"] = GovernanceSettingKeys.Cerbos.GrpcEndpoint,
                ["Secrets:Ownership:DeploymentManagedKeys:1"] = InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername,
                ["Secrets:Ownership:DeploymentManagedKeys:2"] = InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword
            })
            .Build();
        var service = CreateService(repository, configuration: configuration);

        await service.ApplyConfigurationAsync(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://edited-cerbos.example.com:443"
        });

        await repository.Received(1).UpsertAsync(
            Arg.Is<SystemSetting>(x => x.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().UpsertAsync(
            Arg.Is<SystemSetting>(x =>
                x.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint
                || x.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername
                || x.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithAdminEndpoint_NeverPersistsCredentials()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var invalidator = Substitute.For<IAuthorizationProviderModeCacheInvalidator>();
        var cerbosConfigResolver = Substitute.For<ICerbosConfigResolver>();
        var service = CreateService(repository, invalidator, cerbosConfigResolver);

        await service.ApplyConfigurationAsync(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443",
            CerbosAdminEndpoint = "https://tenant-cerbos.example.com/base"
        });

        await repository.Received(1).UpsertAsync(
            Arg.Is<SystemSetting>(x =>
                x.SettingKey == GovernanceSettingKeys.Cerbos.CustomAdminEndpoint
                && JsonSerializer.Deserialize<string>(x.Value) == "https://tenant-cerbos.example.com/base"),
            Arg.Any<CancellationToken>());
        await repository.DidNotReceive().UpsertAsync(
            Arg.Is<SystemSetting>(x =>
                x.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername
                || x.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword),
            Arg.Any<CancellationToken>());
        cerbosConfigResolver.Received(1).InvalidateCache();
        invalidator.Received(1).InvalidateInstanceMode();
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithBareEndpoints_NormalizesBeforeStorage()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var service = CreateService(repository);

        await service.ApplyConfigurationAsync(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "cerbosgrpc.openislamu.org:443",
            CerbosAdminEndpoint = "cerbosapi.openislamu.org:3592"
        });

        await repository.Received(1).UpsertAsync(
            Arg.Is<SystemSetting>(x =>
                x.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint
                && JsonSerializer.Deserialize<string>(x.Value) == "https://cerbosgrpc.openislamu.org:443"),
            Arg.Any<CancellationToken>());
        await repository.Received(1).UpsertAsync(
            Arg.Is<SystemSetting>(x =>
                x.SettingKey == GovernanceSettingKeys.Cerbos.CustomAdminEndpoint
                && JsonSerializer.Deserialize<string>(x.Value) == "https://cerbosapi.openislamu.org:3592"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithLocalProvider_InvalidatesRuntimeProviderModeCache()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var invalidator = Substitute.For<IAuthorizationProviderModeCacheInvalidator>();
        var cerbosConfigResolver = Substitute.For<ICerbosConfigResolver>();
        var service = CreateService(repository, invalidator, cerbosConfigResolver);

        await service.ApplyConfigurationAsync(new AuthorizationProviderConfigurationDto
        {
            Provider = "local",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443"
        });

        await repository.Received(1).UpsertAsync(
            Arg.Is<SystemSetting>(x =>
                x.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider
                && JsonSerializer.Deserialize<string>(x.Value) == "local"),
            Arg.Any<CancellationToken>());
        await repository.Received(1).UpsertAsync(
            Arg.Is<SystemSetting>(x =>
                x.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint
                && JsonSerializer.Deserialize<string>(x.Value) == string.Empty),
            Arg.Any<CancellationToken>());
        cerbosConfigResolver.Received(1).InvalidateCache();
        invalidator.Received(1).InvalidateInstanceMode();
    }

    [Test]
    public async Task ApplyConfigurationAsync_WhenCredentialsAreOmitted_PreservesExistingSecrets()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var service = CreateService(repository);

        await service.ApplyConfigurationAsync(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443",
            CerbosAdminEndpoint = "https://tenant-cerbos.example.com"
        });

        await repository.DidNotReceive().UpsertAsync(
            Arg.Is<SystemSetting>(x =>
                x.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername
                || x.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithUnsafeAdminEndpoint_DoesNotPersistOrInvalidateProviderModeCache()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var invalidator = Substitute.For<IAuthorizationProviderModeCacheInvalidator>();
        var cerbosConfigResolver = Substitute.For<ICerbosConfigResolver>();
        var service = CreateService(repository, invalidator, cerbosConfigResolver);

        await Assert.That(async () => await service.ApplyConfigurationAsync(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443",
            CerbosAdminEndpoint = "https://127.0.0.1:3592"
        }))
            .Throws<InvalidOperationException>();

        await repository.DidNotReceive().UpsertAsync(
            Arg.Any<SystemSetting>(),
            Arg.Any<CancellationToken>());
        cerbosConfigResolver.DidNotReceive().InvalidateCache(Arg.Any<Guid?>());
        invalidator.DidNotReceive().InvalidateInstanceMode();
    }

    [Test]
    public async Task VerifyCerbosAdminEndpointAsync_WithUnsafeEndpoint_ReturnsFalseWithoutPersistingSecrets()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var service = CreateService(repository);

        var result = await service.VerifyCerbosAdminEndpointAsync("https://127.0.0.1:3592");

        await Assert.That(result).IsFalse();
        await repository.DidNotReceive().UpsertAsync(
            Arg.Any<SystemSetting>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task VerifyCerbosAdminEndpointAsync_WithBareRemoteHost_NormalizesAndAccepts()
    {
        var repository = Substitute.For<ISystemSettingRepository>();
        var service = CreateService(repository);

        var result = await service.VerifyCerbosAdminEndpointAsync("cerbosapi.openislamu.org:3592");

        await Assert.That(result).IsTrue();
    }

    private static AuthorizationProviderConfigurationService CreateService(
        ISystemSettingRepository repository,
        IAuthorizationProviderModeCacheInvalidator? invalidator = null,
        ICerbosConfigResolver? cerbosConfigResolver = null,
        ISecretResolver? secretResolver = null,
        IConfiguration? configuration = null,
        IPolicyPackageService? packageService = null,
        AuthorizationProviderBootstrapState? bootstrapState = null)
    {
        configuration ??= new ConfigurationBuilder().Build();
        if (secretResolver is null)
        {
            secretResolver = Substitute.For<ISecretResolver>();
            secretResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Unconfigured);
        }
        var options = Options.Create(new CerbosPolicyPackageOptions());
        return new AuthorizationProviderConfigurationService(
            repository,
            configuration,
            secretResolver,
            new CerbosAdminEndpointValidator(options),
            invalidator ?? Substitute.For<IAuthorizationProviderModeCacheInvalidator>(),
            cerbosConfigResolver ?? Substitute.For<ICerbosConfigResolver>(),
            packageService ?? Substitute.For<IPolicyPackageService>(),
            Options.Create(new AuthorizationProviderDeploymentOptions
            {
                Provider = configuration["Authorization:Provider"]
            }),
            bootstrapState ?? new AuthorizationProviderBootstrapState(),
            Substitute.For<ILogger<AuthorizationProviderConfigurationService>>());
    }

    private static SystemSetting CreateSetting(string key, string value)
    {
        return new SystemSetting
        {
            Id = Guid.NewGuid(),
            SettingKey = key,
            Value = JsonSerializer.Serialize(value),
            ValueType = SettingValueType.String,
            IsLocked = true,
            CreatedAt = DateTime.UtcNow
        };
    }
}
