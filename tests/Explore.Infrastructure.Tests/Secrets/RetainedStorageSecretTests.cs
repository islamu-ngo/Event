using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Secrets;

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
        var values = new Dictionary<string, string?>
        {
            ["ORIGINAL_STORAGE_ACCESS"] = Guid.NewGuid().ToString("N"),
            ["REPLACEMENT_STORAGE_ACCESS"] = Guid.NewGuid().ToString("N")
        };
        await using var authority = ResolverTestAuthority.Local(values);
        using var fixture = new ResolverFixture(repository, authority);
        var retained = await fixture.Resolver.CaptureAsync(binding.SettingKey, tenant, CancellationToken.None);
        binding.SwitchToEnvironmentVariable("REPLACEMENT_STORAGE_ACCESS");
        // Populate the normal resolver cache using the SAME mutable binding ID and its new reference.
        var replacement = await fixture.Resolver.ResolveAsync(binding.SettingKey, tenant, CancellationToken.None);
        await Assert.That(replacement.Value).IsEqualTo(values["REPLACEMENT_STORAGE_ACCESS"]);
        repository.GetByKeyAndScopeAsync(binding.SettingKey, SecretScope.Tenant, tenant, Arg.Any<CancellationToken>()).Returns((SecretBinding?)null);
        var original = await fixture.Resolver.ResolveAsync(retained, CancellationToken.None);
        await Assert.That(original.Value).IsEqualTo(values["ORIGINAL_STORAGE_ACCESS"]);
        await Assert.That(retained.BindingId).IsEqualTo(binding.Id);
        await Assert.That(retained.EnvironmentVariableName).IsEqualTo("ORIGINAL_STORAGE_ACCESS");
        await Assert.That(retained.ScopeId).IsEqualTo(tenant);
        values["ORIGINAL_STORAGE_ACCESS"] = Guid.NewGuid().ToString("N");
        // Local values rotate only through a newly opened process owner.
        await using var rotatedAuthority = ResolverTestAuthority.Local(values);
        using var rotatedFixture = new ResolverFixture(repository, rotatedAuthority);
        var rotated = await rotatedFixture.Resolver.ResolveAsync(retained, CancellationToken.None);
        await Assert.That(rotated.Value).IsEqualTo(values["ORIGINAL_STORAGE_ACCESS"]);
        await Assert.That(rotated.Secret!.BindingRevision).IsNotEqualTo(original.Secret!.BindingRevision);
        values.Remove("ORIGINAL_STORAGE_ACCESS");
        await using var absentAuthority = ResolverTestAuthority.Local(values);
        using var absentFixture = new ResolverFixture(repository, absentAuthority);
        var unavailable = await absentFixture.Resolver.ResolveAsync(retained, CancellationToken.None);
        await Assert.That(unavailable.Status).IsEqualTo(SecretResolutionStatus.Unconfigured);
        await Assert.That(unavailable.Value).IsNull();
        await authority.Owner.DisposeAsync();
        var retired = await fixture.Resolver.ResolveAsync(retained, CancellationToken.None);
        await Assert.That(retired.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(retired.Value).IsNull();
    }

    [Test]
    public async Task RegistryDefaultReference_IsRetainedWithoutInventingBindingRow()
    {
        var repository = Substitute.For<ISecretBindingRepository>();
        await using var authority = ResolverTestAuthority.Local(new Dictionary<string, string?>());
        using var fixture = new ResolverFixture(repository, authority);
        var reference = await fixture.Resolver.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, Guid.CreateVersion7(), CancellationToken.None);
        await Assert.That(reference.BindingId).IsNull();
        await Assert.That(reference.EnvironmentVariableName).IsEqualTo(
            SecretDefinitionRegistry.TryGet(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey)!.DefaultEnvironmentVariableName);
        await Assert.That(reference.Scope).IsEqualTo(SecretScope.Instance);
    }

    [Test]
    [Arguments("provider")]
    [Arguments("endpoint")]
    [Arguments("project")]
    public async Task ChangedAuthority_IsInvalidEvenIfReplacementSourceCanResolve(
        string changedCoordinate)
    {
        string key = SecretDefinitionRegistry.Keys.Storage.AccessKeyId;
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(key);
        var values = new Dictionary<string, string?>
        {
            [definition.DefaultEnvironmentVariableName] = Guid.NewGuid().ToString("N")
        };
        await using var initial = changedCoordinate == "provider"
            ? ResolverTestAuthority.Local(values)
            : await ResolverTestAuthority.Infisical();
        var repository = Substitute.For<ISecretBindingRepository>();
        using var original = new ResolverFixture(repository, initial);
        var reference = await original.Resolver.CaptureAsync(key, Guid.CreateVersion7(), CancellationToken.None);
        await using var replacement = changedCoordinate == "provider"
            ? ResolverTestAuthority.Local(values, provider: SecretAuthorityKind.UserSecrets)
            : await ResolverTestAuthority.Infisical(project: changedCoordinate == "project" ? "changed-project" : "original-project",
                http: changedCoordinate == "project" ? initial.Http : null);
        if (replacement.Http is not null)
            replacement.Http.Values[definition.DefaultInfisicalKey] = Guid.NewGuid().ToString("N");
        using var changed = new ResolverFixture(repository, replacement);
        var replacementBinding = replacement.Owner.Provider == SecretAuthorityKind.UserSecrets
            ? SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null, definition.DefaultEnvironmentVariableName)
            : SecretBinding.CreateInfisical(key, SecretScope.Instance, null, "staging",
                definition.DefaultInfisicalPath, definition.DefaultInfisicalKey);
        var replacementValue = await replacement.Source.GetSecretAsync(replacementBinding);
        await Assert.That(replacementValue.IsResolved).IsTrue();
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

        public ResolverFixture(ISecretBindingRepository repository, ResolverTestAuthority authority)
        {
            _metrics = new SecretResolverMetrics(_meters);
            Resolver = new SecretResolver(repository, [authority.Source], new SecretResolverCacheState(_cache),
                _metrics, NullLogger<SecretResolver>.Instance, authority.Owner);
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
