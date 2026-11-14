using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class StorageProbeBindingTests
{
    [Test]
    [Arguments(StorageProviders.S3Compatible)]
    [Arguments(StorageProviders.Local)]
    public async Task ExplicitTenantProviderDoesNotObserveUnusedInvalidInstanceCredentials(string provider)
    {
        using var fixture = new ProbeFixture(provider);
        fixture.Secrets.RejectInstanceCapture = true;

        var binding = await StorageProbeBinding.CaptureAsync(fixture.Services, fixture.TenantId, default);
        var credentials = await binding.ResolveCredentialsAsync(fixture.Services, default);
        string selected = await binding.SelectProviderAsync(fixture.Services, fixture.TenantId, default);
        await binding.RequireCredentialsAsync(fixture.Services, credentials, default);

        await Assert.That(selected).IsEqualTo(provider);
        await Assert.That(credentials!.Endpoint).IsEqualTo("https://tenant-storage.example.test");
    }

    [Test]
    [Arguments(SecretResolutionStatus.Invalid)]
    [Arguments(SecretResolutionStatus.Unavailable)]
    public async Task DefaultProviderAutodetectionStillRejectsUnavailableInstanceCredentials(SecretResolutionStatus status)
    {
        using var fixture = new ProbeFixture(tenantProvider: null);
        fixture.Secrets.InstanceStatus = status;
        var binding = await StorageProbeBinding.CaptureAsync(fixture.Services, fixture.TenantId, default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            binding.SelectProviderAsync(fixture.Services, fixture.TenantId, default));
    }

    [Test]
    public async Task DefaultProviderRemainsLocalWhenObservedInstanceCredentialsAreUnconfigured()
    {
        using var fixture = new ProbeFixture(tenantProvider: null);
        fixture.Secrets.InstanceStatus = SecretResolutionStatus.Unconfigured;
        var binding = await StorageProbeBinding.CaptureAsync(fixture.Services, fixture.TenantId, default);
        var credentials = await binding.ResolveCredentialsAsync(fixture.Services, default);

        string selected = await binding.SelectProviderAsync(fixture.Services, fixture.TenantId, default);
        await binding.RequireCredentialsAsync(fixture.Services, credentials, default);

        await Assert.That(selected).IsEqualTo(StorageProviders.Local);
    }

    [Test]
    [Arguments("material")]
    [Arguments("revision")]
    [Arguments("reference")]
    [Arguments("unavailable")]
    [Arguments("unconfigured")]
    public async Task ObservedInstanceCredentialDriftRejectsTheOriginalHandoff(string change)
    {
        using var fixture = new ProbeFixture(tenantProvider: null);
        var binding = await StorageProbeBinding.CaptureAsync(fixture.Services, fixture.TenantId, default);
        var credentials = await binding.ResolveCredentialsAsync(fixture.Services, default);
        await Assert.That(await binding.SelectProviderAsync(fixture.Services, fixture.TenantId, default))
            .IsEqualTo(StorageProviders.S3Compatible);
        await binding.RequireCredentialsAsync(fixture.Services, credentials, default);
        switch (change)
        {
            case "material": fixture.Secrets.InstanceValue = RandomValue(); break;
            case "revision": fixture.Secrets.InstanceRevision = Guid.CreateVersion7().ToString("N"); break;
            case "reference": fixture.Secrets.InstanceAuthority = Guid.CreateVersion7().ToString("N"); break;
            case "unavailable": fixture.Secrets.InstanceStatus = SecretResolutionStatus.Unavailable; break;
            case "unconfigured": fixture.Secrets.InstanceStatus = SecretResolutionStatus.Unconfigured; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            binding.RequireCredentialsAsync(fixture.Services, credentials, default));
    }

    private static string RandomValue() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private sealed class ProbeFixture : IDisposable
    {
        internal Guid TenantId { get; } = Guid.CreateVersion7();
        internal ProbeSecrets Secrets { get; }
        internal ServiceProvider Services { get; }

        internal ProbeFixture(string? tenantProvider)
        {
            Secrets = new ProbeSecrets(TenantId);
            SystemSetting[] instance =
            [
                Setting(GovernanceSettingKeys.Deployment.Mode, DeploymentMode.MultiTenant.ToString()),
                new() { SettingKey = GovernanceSettingKeys.TenantDelegation.LockStorage, Value = "false" },
                Setting(GovernanceSettingKeys.Storage.Endpoint, "https://instance-storage.example.test"),
                Setting(GovernanceSettingKeys.Storage.BucketName, "instance-bucket")
            ];
            List<TenantSetting> tenant =
            [
                Override(GovernanceSettingKeys.Storage.Endpoint, "https://tenant-storage.example.test"),
                Override(GovernanceSettingKeys.Storage.BucketName, "tenant-bucket")
            ];
            if (tenantProvider is not null)
                tenant.Add(Override(GovernanceSettingKeys.Storage.Provider, tenantProvider));
            var instanceReader = Substitute.For<IInstanceSystemSettingsReader>();
            instanceReader.ReadAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<SystemSetting>>(instance));
            var tenantReader = Substitute.For<IStorageProbeTenantSettingsReader>();
            tenantReader.ReadAsync(TenantId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<TenantSetting>>(tenant));
            var bindings = Substitute.For<IStorageProbeSecretBindingsReader>();
            bindings.ReadAsync(TenantId, Arg.Any<CancellationToken>()).Returns(
                Task.FromResult(((IReadOnlyList<SecretBinding>)Secrets.Bindings, JsonSerializer.Serialize(Secrets.Bindings))));
            Services = new ServiceCollection()
                .AddSingleton(instanceReader).AddSingleton(tenantReader).AddSingleton(bindings)
                .AddSingleton<IRetainedSecretResolver>(Secrets)
                .AddSingleton(Substitute.For<IFileStorageProviderResolver>())
                .AddSingleton<IOptions<LocalFileStorageOptions>>(
                    Options.Create(new LocalFileStorageOptions { RootPath = Path.GetTempPath() }))
                .BuildServiceProvider();
        }

        private static SystemSetting Setting(string key, string value) =>
            new() { SettingKey = key, Value = JsonSerializer.Serialize(value) };

        private TenantSetting Override(string key, string value) =>
            new() { TenantId = TenantId, Tenant = null!, SettingKey = key, Value = JsonSerializer.Serialize(value) };

        public void Dispose() => Services.Dispose();
    }

    private sealed class ProbeSecrets : IRetainedSecretResolver
    {
        internal IReadOnlyList<SecretBinding> Bindings { get; }
        internal bool RejectInstanceCapture { get; set; }
        internal SecretResolutionStatus InstanceStatus { get; set; } = SecretResolutionStatus.Resolved;
        internal string InstanceValue { get; set; } = RandomValue();
        internal string InstanceRevision { get; set; } = Guid.CreateVersion7().ToString("N");
        internal string InstanceAuthority { get; set; } = "Environment";
        private readonly string tenantValue = RandomValue();
        private readonly string tenantRevision = Guid.CreateVersion7().ToString("N");

        internal ProbeSecrets(Guid tenantId)
        {
            Bindings = new[] { SecretDefinitionRegistry.Keys.Storage.AccessKeyId, SecretDefinitionRegistry.Keys.Storage.SecretAccessKey }
                .SelectMany(key => new[]
                {
                    SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null, "INSTANCE_" + Guid.CreateVersion7().ToString("N")),
                    SecretBinding.CreateEnvironmentVariable(key, SecretScope.Tenant, tenantId, "TENANT_" + Guid.CreateVersion7().ToString("N"))
                }).ToArray();
        }

        public Task<RetainedSecretReference?> CaptureAsync(string settingKey, Guid? tenantId,
            SecretBinding? selectedBinding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tenantId is null && RejectInstanceCapture)
                throw new UnauthorizedAccessException("The unused instance binding is invalid.");
            return Task.FromResult<RetainedSecretReference?>(RetainedSecretReference.Capture(
                selectedBinding ?? throw new InvalidOperationException("A selected test binding is required."),
                tenantId is null ? InstanceAuthority : "Environment"));
        }

        public Task<SecretResolutionResult> ResolveAsync(RetainedSecretReference reference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool instance = reference.Scope == SecretScope.Instance;
            var result = instance ? InstanceStatus switch
            {
                SecretResolutionStatus.Invalid => SecretResolutionResult.Invalid,
                SecretResolutionStatus.Unavailable => SecretResolutionResult.Unavailable,
                SecretResolutionStatus.Unconfigured => SecretResolutionResult.Unconfigured,
                SecretResolutionStatus.Resolved => Resolved(reference, InstanceValue, InstanceRevision),
                _ => throw new InvalidOperationException("Unsupported test resolution status.")
            } : Resolved(reference, tenantValue, tenantRevision);
            return Task.FromResult(result);
        }

        private static SecretResolutionResult Resolved(RetainedSecretReference reference, string value, string revision) =>
            SecretResolutionResult.Resolved(new(reference.SettingKey, value, reference.SourceType, reference.Scope,
                reference.ScopeId, DateTimeOffset.UnixEpoch, revision));

        public Task<RetainedSecretReference> CaptureAsync(string settingKey, Guid tenantId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<RetainedSecretReference?> CaptureInstanceAsync(string settingKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
