using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class SetupSecretProviderTests
{
    [Test]
    public async Task ConfiguredRandomSecret_UnclaimedInstance_AcceptsSecretThroughConfigurationSeam()
    {
        var configuredSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SETUP_SECRET"] = configuredSecret })
            .Build();
        var provider = new SetupSecretProvider(
            configuration,
            CreateScopeFactory(CreatePendingBootstrap()));

        await provider.InitializeAsync();

        await Assert.That(provider.IsSetupModeActive).IsEqualTo(true);
        await Assert.That(provider.ValidateSecret(configuredSecret)).IsEqualTo(true);
    }

    [Test]
    public async Task Constructor_EnvSecretPresent_UsesEnvironmentValue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SETUP_SECRET"] = "my-test-secret" })
            .Build();
        var scopeFactory = CreateScopeFactory(null);
        var provider = new SetupSecretProvider(config, scopeFactory);

        await Assert.That(provider.IsFromEnvironmentVariable).IsEqualTo(true);
        await Assert.That(provider.ValidateSecret("my-test-secret")).IsEqualTo(true);
    }

    [Test]
    public async Task Constructor_EnvSecretMissing_UsesDefaultTemporaryFilePath()
    {
        var provider = new SetupSecretProvider(
            new ConfigurationBuilder().Build(),
            CreateScopeFactory(null));

        await Assert.That(provider.IsSetupSecretRequired).IsEqualTo(true);
        await Assert.That(provider.IsFromEnvironmentVariable).IsEqualTo(false);
        await Assert.That(provider.GeneratedSecretFilePath)
            .IsEqualTo(Path.Combine(Path.GetTempPath(), "islamu-event", "setup-secret"));
        await Assert.That(provider.ValidateSecret("not-the-generated-secret")).IsEqualTo(false);
    }

    [Test]
    public async Task Constructor_MultiReplicaWithoutSharedExplicitAuthorityFailsClosed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hosting:ReplicaCount"] = "2",
                ["SETUP_SECRET"] = string.Empty
            })
            .Build();

        var action = () => new SetupSecretProvider(configuration, CreateScopeFactory(null));

        await Assert.That(action).Throws<InvalidOperationException>()
            .WithMessageContaining("deployment-owned authority");
    }

    [Test]
    public async Task InitializeAsync_EmptySecretWithFilePath_PersistsGeneratedSecretOnce()
    {
        using var directory = new TemporaryDirectory();
        var secretPath = Path.Combine(directory.Path, "setup-secret");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SETUP_SECRET"] = string.Empty,
                ["SETUP_SECRET_FILE"] = secretPath
            })
            .Build();

        using var firstProvider = new SetupSecretProvider(
            configuration,
            CreateScopeFactory(CreatePendingBootstrap()));
        await firstProvider.InitializeAsync();
        var generatedSecret = (await File.ReadAllTextAsync(secretPath)).Trim();

        using var restartedProvider = new SetupSecretProvider(
            configuration,
            CreateScopeFactory(CreatePendingBootstrap()));
        await restartedProvider.InitializeAsync();

        await Assert.That(generatedSecret.Length).IsEqualTo(32);
        await Assert.That(firstProvider.GeneratedSecretFilePath).IsEqualTo(secretPath);
        await Assert.That(firstProvider.ValidateSecret(generatedSecret)).IsTrue();
        await Assert.That(restartedProvider.ValidateSecret(generatedSecret)).IsTrue();

        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.GetUnixFileMode(secretPath))
                .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public async Task InitializeAsync_ConcurrentSingleAuthorityCreationConverges()
    {
        using var directory = new TemporaryDirectory();
        var secretPath = Path.Combine(directory.Path, "setup-secret");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hosting:ReplicaCount"] = "1",
                ["SETUP_SECRET_FILE"] = secretPath
            })
            .Build();
        SetupSecretProvider[] providers = Enumerable.Range(0, 16)
            .Select(_ => new SetupSecretProvider(
                configuration,
                CreateScopeFactory(CreatePendingBootstrap())))
            .ToArray();

        await Task.WhenAll(providers.Select(provider => provider.InitializeAsync()));
        string generated = (await File.ReadAllTextAsync(secretPath)).Trim();

        await Assert.That(providers.All(provider => provider.ValidateSecret(generated))).IsTrue();
        foreach (SetupSecretProvider provider in providers)
        {
            provider.Dispose();
        }
    }

    [Test]
    public async Task Constructor_ExplicitSecret_OverridesAndDeletesPersistedGeneratedSecret()
    {
        using var directory = new TemporaryDirectory();
        var secretPath = Path.Combine(directory.Path, "setup-secret");
        var generatedConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SETUP_SECRET_FILE"] = secretPath })
            .Build();
        using (var generatedProvider = new SetupSecretProvider(
                   generatedConfiguration,
                   CreateScopeFactory(CreatePendingBootstrap())))
        {
            await generatedProvider.InitializeAsync();
        }

        var explicitConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SETUP_SECRET"] = "operator-provided-secret",
                ["SETUP_SECRET_FILE"] = secretPath
            })
            .Build();
        using var explicitProvider = new SetupSecretProvider(explicitConfiguration, CreateScopeFactory(null));

        await Assert.That(File.Exists(secretPath)).IsFalse();
        await Assert.That(explicitProvider.IsFromEnvironmentVariable).IsTrue();
        await Assert.That(explicitProvider.GeneratedSecretFilePath).IsNull();
        await Assert.That(explicitProvider.ValidateSecret("operator-provided-secret")).IsTrue();
    }

    [Test]
    public async Task Lock_PersistedGeneratedSecret_DeletesFile()
    {
        using var directory = new TemporaryDirectory();
        var secretPath = Path.Combine(directory.Path, "setup-secret");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SETUP_SECRET_FILE"] = secretPath })
            .Build();
        using var provider = new SetupSecretProvider(
            configuration,
            CreateScopeFactory(CreatePendingBootstrap()));
        await provider.InitializeAsync();

        provider.Lock();

        await Assert.That(File.Exists(secretPath)).IsFalse();
    }

    [Test]
    public async Task InitializeAsync_CompletedBootstrap_DeletesPersistedGeneratedSecret()
    {
        using var directory = new TemporaryDirectory();
        var secretPath = Path.Combine(directory.Path, "setup-secret");
        await File.WriteAllTextAsync(secretPath, "stale-generated-secret");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SETUP_SECRET_FILE"] = secretPath })
            .Build();
        using var provider = new SetupSecretProvider(
            configuration,
            CreateScopeFactory(CreateCompletedBootstrap()));

        await provider.InitializeAsync();

        await Assert.That(provider.IsSetupModeActive).IsFalse();
        await Assert.That(File.Exists(secretPath)).IsFalse();
    }

    [Test]
    public async Task InitializeAsync_UnwritableGeneratedSecretPath_RequiresExplicitSecret()
    {
        using var directory = new TemporaryDirectory();
        var blockingFile = Path.Combine(directory.Path, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "occupied");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SETUP_SECRET_FILE"] = Path.Combine(blockingFile, "setup-secret")
            })
            .Build();
        using var provider = new SetupSecretProvider(
            configuration,
            CreateScopeFactory(CreatePendingBootstrap()));

        InvalidOperationException exception = await Assert.That(
                async () => await provider.InitializeAsync())
            .Throws<InvalidOperationException>();

        await Assert.That(exception.Message).Contains("SETUP_SECRET");
        await Assert.That(provider.IsSetupModeActive).IsTrue();
    }

    [Test]
    public async Task Constructor_SetupSecretRequiredOmitted_DefaultsToRequired()
    {
        var provider = new SetupSecretProvider(
            new ConfigurationBuilder().Build(),
            CreateScopeFactory(null));

        await Assert.That(provider.IsSetupSecretRequired).IsEqualTo(true);
        await Assert.That(provider.IsFromEnvironmentVariable).IsEqualTo(false);
    }

    [Test]
    public async Task Constructor_SetupSecretRequiredFalseWithoutTrustedProvisioning_FailsClosedAsRequired()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SETUP_SECRET_REQUIRED"] = "false" })
            .Build();
        var provider = new SetupSecretProvider(configuration, CreateScopeFactory(null));

        await Assert.That(provider.IsSetupSecretRequired).IsEqualTo(true);
        await Assert.That(provider.IsTrustedManagedProvisioningConfigured).IsEqualTo(false);
        await Assert.That(provider.IsFromEnvironmentVariable).IsEqualTo(false);
        await Assert.That(provider.ValidateSecret("unknown-secret")).IsEqualTo(false);
    }

    [Test]
    public async Task Constructor_SetupSecretRequiredFalseWithTrustedManagedProvisioning_DisablesInteractiveSetupSecret()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SETUP_SECRET_REQUIRED"] = "false",
                ["PROVISIONING_TRUSTED"] = "true",
                ["PROVISIONING_MODE"] = "managed-provider",
                ["MANAGED_CLIENT_EXTERNAL_PROVIDER"] = "erp",
                ["PHYSICAL_TENANCY_MODE"] = "shared-database"
            })
            .Build();
        var provider = new SetupSecretProvider(configuration, CreateScopeFactory(null));

        await Assert.That(provider.IsSetupSecretRequired).IsEqualTo(false);
        await Assert.That(provider.IsTrustedManagedProvisioningConfigured).IsEqualTo(true);
        await Assert.That(provider.ValidateSecret(null)).IsEqualTo(false);
        await Assert.That(provider.ValidateSecret("anything")).IsEqualTo(false);
    }

    [Test]
    public async Task ValidateSecret_CorrectSecret_ReturnsTrue()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var provider = CreateProvider(secret);

        await Assert.That(provider.ValidateSecret(secret)).IsEqualTo(true);
    }

    [Test]
    public async Task ValidateSecret_WrongSecret_ReturnsFalse()
    {
        var provider = CreateProvider();

        await Assert.That(provider.ValidateSecret("wrong-secret")).IsEqualTo(false);
    }

    [Test]
    public async Task ValidateSecret_NullOrEmpty_ReturnsFalse()
    {
        var provider = CreateProvider();

        await Assert.That(provider.ValidateSecret(null)).IsEqualTo(false);
        await Assert.That(provider.ValidateSecret(string.Empty)).IsEqualTo(false);
    }

    [Test]
    public async Task ValidateSecret_AfterLock_ReturnsFalse()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var provider = CreateProvider(secret);

        provider.Lock();

        await Assert.That(provider.ValidateSecret(secret)).IsEqualTo(false);
    }

    [Test]
    public async Task IsSetupModeActive_WhenLocked_ReturnsFalse()
    {
        var provider = CreateProvider();

        provider.Lock();

        await Assert.That(provider.IsSetupModeActive).IsEqualTo(false);
    }

    [Test]
    public async Task IsSetupModeActive_WhenBootstrapComplete_ReturnsFalse()
    {
        var provider = CreateProvider(CreateCompletedBootstrap());
        await provider.InitializeAsync();

        await Assert.That(provider.IsSetupModeActive).IsEqualTo(false);
    }

    [Test]
    public async Task IsSetupModeActive_WhenBootstrapNotCompleteAndNotLocked_ReturnsTrue()
    {
        var provider = CreateProvider(CreatePendingBootstrap());
        await provider.InitializeAsync();

        await Assert.That(provider.IsSetupModeActive).IsEqualTo(true);
    }

    [Test]
    public async Task IsSetupModeActive_BeforeInitialize_ReturnsFalse()
    {
        var provider = CreateProvider(CreatePendingBootstrap());

        await Assert.That(provider.IsSetupModeActive).IsEqualTo(false);
    }

    [Test]
    public async Task InitializeAsync_WhenBootstrapStateCannotBeRead_FailsClosed()
    {
        var repository = Substitute.For<IInstanceBootstrapStateRepository>();
        repository.GetCurrent(Arg.Any<CancellationToken>())
            .Returns<Task<InstanceBootstrapState?>>(_ => throw new InvalidOperationException("database unavailable"));
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.GetService(typeof(IInstanceBootstrapStateRepository)).Returns(repository);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var provider = new SetupSecretProvider(new ConfigurationBuilder().Build(), scopeFactory);

        await Assert.That(async () => await provider.InitializeAsync()).Throws<InvalidOperationException>();
        await Assert.That(provider.IsSetupModeActive).IsFalse();
    }

    [Test]
    public async Task Lock_AppliesSetupAndValidationTransitions()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var provider = CreateProvider(secret, CreatePendingBootstrap());
        await provider.InitializeAsync();

        await Assert.That(provider.IsSetupModeActive).IsEqualTo(true);

        provider.Lock();

        await Assert.That(provider.IsSetupModeActive).IsEqualTo(false);
        await Assert.That(provider.ValidateSecret(secret)).IsEqualTo(false);
    }

    private static SetupSecretProvider CreateProvider(InstanceBootstrapState? state = null)
    {
        return CreateProvider(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), state);
    }

    private static SetupSecretProvider CreateProvider(string configuredSecret, InstanceBootstrapState? state = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SETUP_SECRET"] = configuredSecret })
            .Build();
        return new SetupSecretProvider(configuration, CreateScopeFactory(state));
    }

    private static IServiceScopeFactory CreateScopeFactory(InstanceBootstrapState? bootstrapState)
    {
        var repository = Substitute.For<IInstanceBootstrapStateRepository>();
        repository.GetCurrent().Returns(Task.FromResult(bootstrapState));

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.GetService(typeof(IInstanceBootstrapStateRepository)).Returns(repository);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return scopeFactory;
    }

    private static InstanceBootstrapState CreatePendingBootstrap() =>
        InstanceBootstrapState.CreateInteractivePending(
            Guid.CreateVersion7(),
            DeploymentMode.SingleTenant,
            DateTime.UtcNow);

    private static InstanceBootstrapState CreateCompletedBootstrap()
    {
        DateTime completedAt = DateTime.UtcNow;
        InstanceBootstrapState bootstrap = InstanceBootstrapState.CreateInteractivePending(
            Guid.CreateVersion7(),
            DeploymentMode.SingleTenant,
            completedAt.AddMinutes(-1));
        bootstrap.CompleteInteractive(Guid.CreateVersion7(), completedAt);
        return bootstrap;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("islamu-setup-secret-").FullName;

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
