using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public class AuthorizationProviderConfigurationServiceTests
{
    [Test]
    public async Task ReadConfigurationAsync_IgnoresLegacyStoredAdminCredentials()
    {
        string retiredUsername = Guid.CreateVersion7().ToString("N");
        string retiredPassword = Guid.CreateVersion7().ToString("N");
        var reader = new DetachedSettingsReader(
            CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos"),
            CreateSetting(GovernanceSettingKeys.Cerbos.GrpcEndpoint, "https://cerbosgrpc.example.com:443"),
            CreateSetting(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint, "https://tenant-cerbos.example.com:3592"),
            CreateSetting(InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername, retiredUsername),
            CreateSetting(InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword, retiredPassword));
        var service = CreateService(reader);

        var configuration = await service.ReadConfigurationAsync();

        await Assert.That(configuration.CerbosAdminEndpoint).IsEqualTo("https://tenant-cerbos.example.com:3592");
        await Assert.That(JsonSerializer.Serialize(configuration)).DoesNotContain(retiredPassword);
        await Assert.That(JsonSerializer.Serialize(configuration)).DoesNotContain(retiredUsername);
    }

    [Test]
    public async Task RuntimeSecretCatalogueRejectsRetiredCerbosAdministratorBindings()
    {
        await Assert.That(Explore.Domain.Secrets.SecretDefinitionRegistry.IsKnown(
            InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername)).IsFalse();
        await Assert.That(Explore.Domain.Secrets.SecretDefinitionRegistry.IsKnown(
            InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword)).IsFalse();
    }


    [Test]
    public async Task ReadConfigurationAsync_WhenEnvironmentEndpointExists_BootstrapsEditableApplicationManagedMetadata()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cerbos:GrpcEndpoint"] = "cerbosgrpc.openislamu.org:443"
            })
            .Build();
        var service = CreateService(configuration: configuration);

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
        var reader = new DetachedSettingsReader(
            CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "local",
                ["Cerbos:GrpcEndpoint"] = "https://unused.example.test:443"
            })
            .Build();
        var packageService = Substitute.For<IPolicyPackageService>();
        var service = CreateService(reader, configuration: configuration, packageService: packageService);

        var result = await service.ReadConfigurationAsync();
        var reconciliation = await service.ReconcileDeploymentProviderAsync();

        await Assert.That(result.Provider).IsEqualTo("local");
        await Assert.That(result.AuthorizationProviderManagedByDeployment).IsTrue();
        await Assert.That(result.AuthorizationProviderConfigured).IsTrue();
        await Assert.That(result.AuthorizationProviderBootstrapStatus).IsEqualTo(AuthorizationProviderBootstrapState.Ready);
        await Assert.That(reconciliation.Succeeded).IsTrue();
        await packageService.DidNotReceive().PublishAsync(Arg.Any<SyncAuthorizationPolicyPackageCommand>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReadConfigurationAsync_WithExplicitCerbosProvider_RemainsPendingUntilServerReconciliationSucceeds()
    {
        var reader = new DetachedSettingsReader(
            CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "local"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "cerbos",
                ["Cerbos:GrpcEndpoint"] = "https://cerbos.example.test:443"
            })
            .Build();
        var state = new AuthorizationProviderBootstrapState();
        var packageService = Substitute.For<IPolicyPackageService>();
        var service = CreateService(reader, configuration: configuration, packageService: packageService,
            bootstrapState: state);

        var pending = await service.ReadConfigurationAsync();
        packageService.IsPublicationReadyAsync(Arg.Any<CancellationToken>()).Returns(true);
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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "cerbos",
                ["Cerbos:GrpcEndpoint"] = "https://cerbos.example.test:443"
            })
            .Build();
        var packageService = Substitute.For<IPolicyPackageService>();
        var service = CreateService(configuration: configuration, packageService: packageService);
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
        await packageService.DidNotReceive().PublishAsync(Arg.Any<SyncAuthorizationPolicyPackageCommand>(), Arg.Any<CancellationToken>());
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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:Provider"] = "local"
            })
            .Build();
        await using var fixture = await AdministratorAsync(configuration);
        var service = CreateMutationService(fixture);

        var failure = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            service.ApplyConfigurationAsync(Command(fixture, new()
            {
                Provider = "cerbos",
                CerbosGrpcEndpoint = "https://cerbos.example.test:443"
            })));
        await Assert.That(failure!.Errors.Select(error => error.PropertyName).ToArray())
            .IsEquivalentTo(new[] { "Provider" });

        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AnyAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider
            || row.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint
            || row.SettingKey == GovernanceSettingKeys.Cerbos.CustomAdminEndpoint)).IsFalse();
    }

    [Test]
    public async Task ReadConfigurationAsync_WhenDatabaseEndpointExists_DatabaseWinsOverEnvironmentBootstrap()
    {
        var reader = new DetachedSettingsReader(
            CreateSetting(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos"),
            CreateSetting(GovernanceSettingKeys.Cerbos.GrpcEndpoint, "https://saved-cerbos.example.com:443"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cerbos:GrpcEndpoint"] = "cerbosgrpc.openislamu.org:443"
            })
            .Build();
        var service = CreateService(reader, configuration: configuration);

        var result = await service.ReadConfigurationAsync();

        await Assert.That(result.CerbosGrpcEndpoint).IsEqualTo("https://saved-cerbos.example.com:443");
        await Assert.That(result.AuthorizationProviderConfigured).IsTrue();
        await Assert.That(result.CerbosEndpointOwnership.Source).IsEqualTo("application");
        await Assert.That(result.CerbosEndpointOwnership.BootstrapAvailable).IsFalse();
    }

    [Test]
    public async Task ReadConfigurationAsync_WhenEndpointDeploymentManaged_UsesEnvironmentAndReturnsReadOnlyMetadata()
    {
        var reader = new DetachedSettingsReader(
            CreateSetting(GovernanceSettingKeys.Cerbos.GrpcEndpoint, "https://saved-cerbos.example.com:443"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cerbos:GrpcEndpoint"] = "cerbosgrpc.openislamu.org:443",
                ["Secrets:Ownership:DeploymentManagedKeys:0"] = GovernanceSettingKeys.Cerbos.GrpcEndpoint
            })
            .Build();
        var service = CreateService(reader, configuration: configuration);

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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Ownership:DeploymentManagedKeys:0"] = GovernanceSettingKeys.Cerbos.GrpcEndpoint,
                ["Secrets:Ownership:DeploymentManagedKeys:1"] = InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername,
                ["Secrets:Ownership:DeploymentManagedKeys:2"] = InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword
            })
            .Build();
        await using var fixture = await AdministratorAsync(configuration);
        var service = CreateMutationService(fixture);

        await service.ApplyConfigurationAsync(Command(fixture, new()
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://edited-cerbos.example.com:443"
        }));

        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"cerbos\"");
        await Assert.That(await observer.SystemSettings.AnyAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint
            || row.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername
            || row.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword)).IsFalse();
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithAdminEndpoint_NeverPersistsCredentials()
    {
        await using var fixture = await AdministratorAsync();
        var activation = new SettledCacheActivation(fixture);
        var service = CreateMutationService(fixture, activation: activation);

        await service.ApplyConfigurationAsync(Command(fixture, new()
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443",
            CerbosAdminEndpoint = "https://tenant-cerbos.example.com"
        }));

        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == GovernanceSettingKeys.Cerbos.CustomAdminEndpoint)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"https://tenant-cerbos.example.com\"");
        await Assert.That(await observer.SystemSettings.AnyAsync(row =>
            row.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername
            || row.SettingKey == InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword)).IsFalse();
        await Assert.That(activation.ProviderAtModeActivation).IsEqualTo("\"cerbos\"");
        await Assert.That(activation.ProviderAtConfigActivation).IsEqualTo("\"cerbos\"");
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithBareEndpoints_NormalizesBeforeStorage()
    {
        await using var fixture = await AdministratorAsync();
        var service = CreateMutationService(fixture);

        await service.ApplyConfigurationAsync(Command(fixture, new()
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "cerbosgrpc.openislamu.org:443",
            CerbosAdminEndpoint = "cerbosapi.openislamu.org:3592"
        }));

        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"https://cerbosgrpc.openislamu.org:443\"");
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == GovernanceSettingKeys.Cerbos.CustomAdminEndpoint)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"https://cerbosapi.openislamu.org:3592\"");
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithLocalProvider_InvalidatesRuntimeProviderModeCache()
    {
        await using var fixture = await AdministratorAsync();
        var activation = new SettledCacheActivation(fixture);
        var service = CreateMutationService(fixture, activation: activation);

        await service.ApplyConfigurationAsync(Command(fixture, new()
        {
            Provider = "local",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443"
        }));

        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"local\"");
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"\"");
        await Assert.That(activation.ProviderAtModeActivation).IsEqualTo("\"local\"");
        await Assert.That(activation.ProviderAtConfigActivation).IsEqualTo("\"local\"");
    }

    [Test]
    public async Task ApplyConfigurationAsync_WhenCredentialsAreOmitted_PreservesExistingSecrets()
    {
        await using var fixture = await AdministratorAsync();
        var service = CreateMutationService(fixture);
        string[] secretKeys = [InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername,
            InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword];
        // These are opaque retired-row sentinels, not authentication credentials.
        foreach (string key in secretKeys)
            fixture.Context.SystemSettings.Add(CreateSetting(key, Guid.CreateVersion7().ToString("N")));
        await fixture.Context.SaveChangesAsync();
        var original = await fixture.Context.SystemSettings.AsNoTracking()
            .Where(row => secretKeys.Contains(row.SettingKey)).ToDictionaryAsync(row => row.SettingKey, row => row.Value);

        await service.ApplyConfigurationAsync(Command(fixture, new()
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443",
            CerbosAdminEndpoint = "https://tenant-cerbos.example.com"
        }));

        await using var observer = fixture.CreateIndependentContext();
        foreach (string key in secretKeys)
            await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key)
                .Select(row => row.Value).SingleAsync()).IsEqualTo(original[key]);
    }

    [Test]
    public async Task ApplyConfigurationAsync_WithUnsafeAdminEndpoint_DoesNotPersistOrInvalidateProviderModeCache()
    {
        await using var fixture = await AdministratorAsync();
        var activation = new SettledCacheActivation(fixture);
        var service = CreateMutationService(fixture, activation: activation);

        var failure = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            service.ApplyConfigurationAsync(Command(fixture, new()
            {
                Provider = "cerbos",
                CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443",
                CerbosAdminEndpoint = "https://127.0.0.1:3592"
            })));
        await Assert.That(failure!.Errors.Select(error => error.PropertyName).ToArray())
            .IsEquivalentTo(new[] { "CerbosAdminEndpoint" });

        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.SystemSettings.AnyAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider
            || row.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint
            || row.SettingKey == GovernanceSettingKeys.Cerbos.CustomAdminEndpoint)).IsFalse();
        await Assert.That(activation.ProviderAtModeActivation).IsNull();
        await Assert.That(activation.ProviderAtConfigActivation).IsNull();
    }

    [Test]
    public async Task VerifyCerbosAdminEndpointAsync_WithUnsafeEndpoint_ReturnsFalseWithoutPersistingSecrets()
    {
        await using var fixture = await AdministratorAsync();
        var service = CreateMutationService(fixture);
        await using var observer = fixture.CreateIndependentContext();
        var before = JsonSerializer.Serialize(await observer.SystemSettings.AsNoTracking()
            .OrderBy(row => row.Id).ToListAsync());

        var result = await service.VerifyCerbosAdminEndpointAsync("https://127.0.0.1:3592");

        await Assert.That(result).IsFalse();
        await Assert.That(JsonSerializer.Serialize(await observer.SystemSettings.AsNoTracking()
            .OrderBy(row => row.Id).ToListAsync())).IsEqualTo(before);
    }

    [Test]
    public async Task VerifyCerbosAdminEndpointAsync_WithBareRemoteHost_NormalizesAndAccepts()
    {
        var service = CreateService();

        var result = await service.VerifyCerbosAdminEndpointAsync("cerbosapi.openislamu.org:3592");

        await Assert.That(result).IsTrue();
    }

    private static AuthorizationProviderConfigurationService CreateService(
        IInstanceSystemSettingsReader? reader = null,
        IAuthorizationProviderModeCacheInvalidator? invalidator = null,
        ICerbosConfigResolver? cerbosConfigResolver = null,
        IConfiguration? configuration = null,
        IPolicyPackageService? packageService = null,
        AuthorizationProviderBootstrapState? bootstrapState = null)
    {
        configuration ??= new ConfigurationBuilder().Build();
        var readOnlyFactory = Substitute.For<IPolicyMutationExecutionScopeFactory>();
        readOnlyFactory.CreateAsync(Arg.Any<PolicyMutationScope>(), Arg.Any<CancellationToken>())
            .Returns<IPolicyMutationExecutionScope>(_ =>
                throw new InvalidOperationException("An observation fixture cannot acquire a mutation owner."));
        return new AuthorizationProviderConfigurationService(
            reader ?? new DetachedSettingsReader(),
            configuration,
            new CerbosAdminEndpointValidator(),
            invalidator ?? Substitute.For<IAuthorizationProviderModeCacheInvalidator>(),
            cerbosConfigResolver ?? Substitute.For<ICerbosConfigResolver>(),
            packageService ?? Substitute.For<IPolicyPackageService>(),
            Options.Create(new AuthorizationProviderDeploymentOptions
            {
                Provider = configuration["Authorization:Provider"]
            }),
            bootstrapState ?? new AuthorizationProviderBootstrapState(),
            Substitute.For<ILogger<AuthorizationProviderConfigurationService>>(),
            readOnlyFactory,
            new HealthyCerbosEndpointProbe());
    }

    private static async Task<ResourceVisitorAuthenticatedFixture> AdministratorAsync(IConfiguration? configuration = null)
    {
        var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(configureServices: services =>
        {
            services.RemoveAll<ICerbosEndpointHealthProbe>();
            services.AddSingleton<ICerbosEndpointHealthProbe>(new HealthyCerbosEndpointProbe());
            if (configuration is not null)
            {
                var baseline = services.Last(descriptor => descriptor.ServiceType == typeof(IConfiguration))
                    .ImplementationFactory
                    ?? throw new InvalidOperationException("The fixture must expose its baseline configuration factory.");
                services.RemoveAll<IConfiguration>();
                services.AddSingleton<IConfiguration>(provider => new ConfigurationBuilder()
                    .AddConfiguration((IConfiguration)baseline(provider))
                    .AddConfiguration(configuration)
                    .Build());
                services.AddOptions<AuthorizationProviderDeploymentOptions>()
                    .PostConfigure<IConfiguration>((options, deployment) =>
                        options.Provider = deployment["Authorization:Provider"]);
            }
        });
        try
        {
            var role = await fixture.Context.Roles.SingleAsync(row => row.MasterCode == "platform.admin");
            fixture.Context.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.UserId,
                User = null!,
                RoleId = role.Id,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await fixture.Context.SaveChangesAsync();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private static UpdateAuthorizationProviderConfigurationCommand Command(
        ResourceVisitorAuthenticatedFixture fixture, AuthorizationProviderConfigurationWriteDto configuration) => new()
        {
            UserId = fixture.UserId,
            Patch = new() { Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(configuration) }
        };

    private static AuthorizationProviderConfigurationService CreateMutationService(
        ResourceVisitorAuthenticatedFixture fixture, SettledCacheActivation? activation = null)
    {
        activation ??= new SettledCacheActivation(fixture);
        return new AuthorizationProviderConfigurationService(
            new MutationSnapshotOnlyReader(), fixture.Services.GetRequiredService<IConfiguration>(),
            new CerbosAdminEndpointValidator(), activation, activation,
            fixture.Services.GetRequiredService<IPolicyPackageService>(),
            fixture.Services.GetRequiredService<IOptions<AuthorizationProviderDeploymentOptions>>(),
            new AuthorizationProviderBootstrapState(),
            fixture.Services.GetRequiredService<ILogger<AuthorizationProviderConfigurationService>>(),
            fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>(),
            fixture.Services.GetRequiredService<ICerbosEndpointHealthProbe>());
    }

    private sealed class HealthyCerbosEndpointProbe : ICerbosEndpointHealthProbe
    {
        public Task<bool> VerifyAsync(string grpcEndpoint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
    }

    private sealed class DetachedSettingsReader(params SystemSetting[] settings) : IInstanceSystemSettingsReader
    {
        public Task<IReadOnlyList<SystemSetting>> ReadAsync(
            IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SystemSetting>>(
                settings.Where(setting => keys.Contains(setting.SettingKey)).ToArray());
        }
    }

    private sealed class MutationSnapshotOnlyReader : IInstanceSystemSettingsReader
    {
        public Task<IReadOnlyList<SystemSetting>> ReadAsync(
            IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A prepared mutation must read its original snapshot.");
    }

    private sealed class SettledCacheActivation(ResourceVisitorAuthenticatedFixture fixture)
        : IAuthorizationProviderModeCacheInvalidator, ICerbosConfigResolver
    {
        internal string? ProviderAtModeActivation { get; private set; }
        internal string? ProviderAtConfigActivation { get; private set; }
        public void InvalidateInstanceMode() => ProviderAtModeActivation = ReadCommittedProvider();
        public void InvalidateCache(Guid? tenantId = null) => ProviderAtConfigActivation = ReadCommittedProvider();
        public Task<Explore.Application.Models.CerbosConfiguration?> ResolveAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("This observer records activation, not authorization decisions.");
        private string? ReadCommittedProvider()
        {
            using var observer = fixture.CreateIndependentContext();
            return observer.SystemSettings.AsNoTracking().Where(row =>
                row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                .Select(row => row.Value).SingleOrDefault();
        }
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
