using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Observability;
using Explore.Secrets.Services;
using Explore.Secrets.Sources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Services;

public sealed class ConsumerSecretBindingTests
{
    [Test]
    [Arguments("/keycloak")]
    [Arguments("/cerbos")]
    [Arguments("/provisioning/keycloak")]
    [Arguments("/infrastructure/cerbos")]
    [Arguments("/runtime/bff/oidc/google")]
    [Arguments("/smtp")]
    public async Task StaleCoordinatesCannotReachFactoryOrCachedAuthority(string path)
    {
        var binding = SecretBinding.CreateInfisical(SecretDefinitionRegistry.Keys.Smtp.Password,
            SecretScope.Instance, null, "test", "/smtp", "MAIL_SMTP_PASSWORD");
        binding.Id = Guid.CreateVersion7();
        var factory = new CanaryFactory();
        var options = Options.Create(new SecretProviderOptions
        {
            Provider = SecretProviderType.Infisical,
            Consumer = SecretConsumer.Api,
            Infisical = new() { Environment = "test" }
        });
        var source = new InfisicalSecretSource(factory, NullLogger<InfisicalSecretSource>.Instance, options);
        var repository = Substitute.For<ISecretBindingRepository>();
        repository.GetByKeyAndScopeAsync(binding.SettingKey, SecretScope.Instance, null, Arg.Any<CancellationToken>())
            .Returns(binding);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meters = new MeterFactory();
        using var metrics = new SecretResolverMetrics(meters);
        var resolver = new SecretResolver(repository, [source], cache, metrics, NullLogger<SecretResolver>.Instance, options);

        var initial = await resolver.ResolveAsync(binding.SettingKey, null);
        await Assert.That(initial.Value).IsEqualTo(factory.Canary);
        int acquisitions = factory.Acquisitions;
        binding.InfisicalPath = path;
        if (path == "/smtp")
            binding.InfisicalKey = "KEYCLOAK_ADMIN_PASSWORD";
        var rejected = await resolver.ResolveAsync(binding.SettingKey, null);
        await Assert.That(rejected.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(rejected.Value).IsNull();
        await Assert.That(factory.Acquisitions).IsEqualTo(acquisitions);
        var direct = await source.GetSecretAsync(binding);
        await Assert.That(direct.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(factory.Acquisitions).IsEqualTo(acquisitions);
    }

    [Test]
    [Arguments("KEYCLOAK_ADMIN_PASSWORD")]
    [Arguments("CERBOS_ADMIN_PASSWORD")]
    [Arguments("INFISICAL_BFF_CLIENT_SECRET")]
    [Arguments("EVENT_DP_BFF_RING")]
    public async Task EnvironmentBindingCannotReadAnotherAuthority(string variable)
    {
        var binding = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Smtp.Password,
            SecretScope.Instance, null, variable);
        var options = Options.Create(new SecretProviderOptions { Provider = SecretProviderType.Environment });
        var host = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        host.EnvironmentName.Returns("Testing");
        var source = new EnvironmentSecretSource(options, new UserSecretsAuthority(host));
        var result = await source.GetSecretAsync(binding);
        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(result.Value).IsNull();
    }

    private sealed class CanaryFactory : IInfisicalClientFactory, IInfisicalClient
    {
        public string Canary { get; } = SecretsTestValues.CreateSecret();
        public int Acquisitions { get; private set; }
        public Task<IInfisicalClient?> GetClientAsync(CancellationToken cancellationToken = default)
        {
            Acquisitions++;
            return Task.FromResult<IInfisicalClient?>(this);
        }
        public Task<SecretProviderValue?> GetSecretAsync(string environment, string folderPath, string secretName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SecretProviderValue?>(new(Canary, Guid.CreateVersion7().ToString("N")));
        public Task<bool> WriteSecretAsync(string environment, string folderPath, string secretName,
            ReadOnlyMemory<byte> secretValue, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("unexpected_write");
    }

    private sealed class MeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];
        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }
        public void Dispose()
        {
            foreach (var meter in _meters) meter.Dispose();
        }
    }
}
