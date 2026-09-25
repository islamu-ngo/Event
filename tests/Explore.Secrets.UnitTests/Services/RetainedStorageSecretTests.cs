using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Observability;
using Explore.Secrets.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Services;

public sealed class RetainedStorageSecretTests
{
    [Test]
    public async Task RetainedReference_SurvivesBindingRetargetAndTenantRetirement_WithoutCachedFallback()
    {
        var tenant = Guid.CreateVersion7();
        var binding = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            SecretScope.Tenant, tenant, "ORIGINAL_STORAGE_ACCESS");
        binding.Id = Guid.CreateVersion7();
        var repository = Substitute.For<ISecretBindingRepository>();
        repository.GetByKeyAndScopeAsync(binding.SettingKey, SecretScope.Tenant, tenant, Arg.Any<CancellationToken>()).Returns(binding);
        var values = new Dictionary<string, string> { ["ORIGINAL_STORAGE_ACCESS"] = SecretsTestValues.CreateSecret(), ["REPLACEMENT_STORAGE_ACCESS"] = SecretsTestValues.CreateSecret() };
        var source = Substitute.For<ISecretSource>();
        source.SourceType.Returns(SecretSourceType.EnvironmentVariable);
        source.GetSecretAsync(Arg.Any<SecretBinding>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<SecretBinding>()!;
            return values.TryGetValue(request.EnvironmentVariableName!, out var value)
                ? SecretResolutionResult.Resolved(new ResolvedSecret(request.SettingKey, value, request.SourceType, request.Scope, request.ScopeId, DateTimeOffset.UtcNow))
                : SecretResolutionResult.Unavailable;
        });
        using var fixture = new ResolverFixture(repository, source, SecretProviderType.Environment);
        var retained = await fixture.Resolver.CaptureAsync(binding.SettingKey, tenant, CancellationToken.None);
        binding.SwitchToEnvironmentVariable("REPLACEMENT_STORAGE_ACCESS");
        // Populate the normal resolver cache using the SAME mutable binding ID and its new reference.
        await fixture.Resolver.ResolveAsync(binding.SettingKey, tenant, CancellationToken.None);
        repository.GetByKeyAndScopeAsync(binding.SettingKey, SecretScope.Tenant, tenant, Arg.Any<CancellationToken>()).Returns((SecretBinding?)null);
        var original = await fixture.Resolver.ResolveAsync(retained, CancellationToken.None);
        await Assert.That(original.Value).IsEqualTo(values["ORIGINAL_STORAGE_ACCESS"]);
        await Assert.That(retained.BindingId).IsEqualTo(binding.Id);
        values["ORIGINAL_STORAGE_ACCESS"] = SecretsTestValues.CreateSecret();
        var rotated = await fixture.Resolver.ResolveAsync(retained, CancellationToken.None);
        await Assert.That(rotated.Value).IsEqualTo(values["ORIGINAL_STORAGE_ACCESS"]);
        values.Remove("ORIGINAL_STORAGE_ACCESS");
        var unavailable = await fixture.Resolver.ResolveAsync(retained, CancellationToken.None);
        await Assert.That(unavailable.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(unavailable.Value).IsNull();
    }

    [Test]
    public async Task RegistryDefaultReference_IsRetainedWithoutInventingBindingRow()
    {
        var source = Substitute.For<ISecretSource>();
        source.SourceType.Returns(SecretSourceType.EnvironmentVariable);
        var repository = Substitute.For<ISecretBindingRepository>();
        using var fixture = new ResolverFixture(repository, source, SecretProviderType.Environment);
        var reference = await fixture.Resolver.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, Guid.CreateVersion7(), CancellationToken.None);
        await Assert.That(reference.BindingId).IsNull();
        await Assert.That(reference.EnvironmentVariableName).IsEqualTo(
            SecretDefinitionRegistry.TryGet(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey)!.DefaultEnvironmentVariableName);
        await Assert.That(reference.Scope).IsEqualTo(SecretScope.Instance);
    }

    [Test]
    [Arguments(SecretProviderType.UserSecrets, null, null)]
    [Arguments(SecretProviderType.Infisical, "https://changed.example.test", "original-project")]
    [Arguments(SecretProviderType.Infisical, "https://original.example.test", "changed-project")]
    public async Task ChangedAuthority_IsInvalidEvenIfReplacementSourceCanResolve(
        SecretProviderType replacement, string? endpoint, string? project)
    {
        var initialProvider = replacement == SecretProviderType.UserSecrets ? SecretProviderType.Environment : SecretProviderType.Infisical;
        var source = Substitute.For<ISecretSource>();
        source.SourceType.Returns(initialProvider == SecretProviderType.Environment ? SecretSourceType.EnvironmentVariable : SecretSourceType.Infisical);
        source.GetSecretAsync(Arg.Any<SecretBinding>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var binding = call.Arg<SecretBinding>()!;
            return SecretResolutionResult.Resolved(new ResolvedSecret(binding.SettingKey, SecretsTestValues.CreateSecret(), binding.SourceType, binding.Scope, binding.ScopeId, DateTimeOffset.UtcNow));
        });
        var repository = Substitute.For<ISecretBindingRepository>();
        using var original = new ResolverFixture(repository, source, initialProvider,
            initialProvider == SecretProviderType.Infisical ? "https://original.example.test" : null,
            initialProvider == SecretProviderType.Infisical ? "original-project" : null);
        var reference = await original.Resolver.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.AccessKeyId, Guid.CreateVersion7(), CancellationToken.None);
        using var changed = new ResolverFixture(repository, source, replacement, endpoint, project);
        var result = await changed.Resolver.ResolveAsync(reference, CancellationToken.None);
        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(result.Value).IsNull();
    }

    private sealed class ResolverFixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly MeterFactory _meters = new();
        private readonly SecretResolverMetrics _metrics;
        public SecretResolver Resolver { get; }

        public ResolverFixture(ISecretBindingRepository repository, ISecretSource source, SecretProviderType provider,
            string? endpoint = null, string? project = null)
        {
            _metrics = new SecretResolverMetrics(_meters);
            Resolver = new SecretResolver(repository, [source], _cache, _metrics, NullLogger<SecretResolver>.Instance,
                Options.Create(new SecretProviderOptions
                {
                    Provider = provider,
                    Infisical = new InfisicalOptions { Url = endpoint, ProjectId = project, Environment = "test" }
                }));
        }

        public void Dispose()
        {
            _metrics.Dispose();
            _meters.Dispose();
            _cache.Dispose();
        }
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
