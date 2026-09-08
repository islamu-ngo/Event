using Explore.Application.Configuration;
using Explore.Application.Contracts.Secrets;
using Explore.Application;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure;
using Explore.Infrastructure.Services.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class PromotionCodeDigestServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly Guid OtherTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000102");
    private static readonly Guid EventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000201");
    private static readonly Guid OtherEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000202");

    [Test]
    public async Task ComputeActiveAsync_NormalizesAndSeparatesTenantEventScope()
    {
        var service = Service(activeVersion: 2, Key(2));

        var first = await service.ComputeActiveAsync(TenantId, EventId, " ramadan-25 ", CancellationToken.None);
        var same = await service.ComputeActiveAsync(TenantId, EventId, "RAMADAN-25", CancellationToken.None);
        var otherTenant = await service.ComputeActiveAsync(OtherTenantId, EventId, "RAMADAN-25", CancellationToken.None);
        var otherEvent = await service.ComputeActiveAsync(TenantId, OtherEventId, "RAMADAN-25", CancellationToken.None);

        await Assert.That(first.KeyVersion).IsEqualTo(2);
        await Assert.That(Convert.FromBase64String(first.Value).Length).IsEqualTo(32);
        await Assert.That(service.Matches(first.Value, same.Value)).IsTrue();
        await Assert.That(service.Matches(first.Value, otherTenant.Value)).IsFalse();
        await Assert.That(service.Matches(first.Value, otherEvent.Value)).IsFalse();
    }

    [Test]
    public async Task ComputeCandidatesAsync_UsesDistinctPersistedVersionsAndRequiresEveryRetainedKey()
    {
        var service = Service(activeVersion: 3, Key(1), Key(3));

        var candidates = await service.ComputeCandidatesAsync(TenantId, EventId, "eid", [3, 1, 3], CancellationToken.None);

        await Assert.That(candidates.Select(candidate => candidate.KeyVersion)).IsEquivalentTo([1, 3]);
        await Assert.That(candidates.Select(candidate => candidate.Value).Distinct().Count()).IsEqualTo(2);
        await Assert.That(async () => await service.ComputeCandidatesAsync(TenantId, EventId, "eid", [1, 2], CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ComputeCandidatesAsync_NonpositivePersistedVersionFailsBeforeResolvingAnyKey()
    {
        var resolver = new CountingSecretResolver(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v1"] = Key(1).Value,
        });
        var service = new PromotionCodeDigestService(
            resolver,
            Options.Create(new PromotionCodeLookupOptions { ActiveKeyVersion = 1 }));

        await Assert.That(async () => await service.ComputeCandidatesAsync(TenantId, EventId, "eid", [0, 1], CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(resolver.QualifiedResolveCount).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeActiveAsync_RejectsInvalidVersionAndShortKeys()
    {
        await Assert.That(async () => await Service(activeVersion: 0, Key(1)).ComputeActiveAsync(TenantId, EventId, "eid", CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await Service(activeVersion: 1, (1, Convert.ToBase64String(new byte[16]))).ComputeActiveAsync(TenantId, EventId, "eid", CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ConfigureApplicationServices_ValidatesPromotionCodeActiveKeyVersionAtStartup()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Promotions:CodeLookup:ActiveKeyVersion"] = "0",
            })
            .Build();
        var services = new ServiceCollection();

        services.ConfigureApplicationServices(configuration);
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        await Assert.That(() => provider.GetRequiredService<IOptions<PromotionCodeLookupOptions>>().Value)
            .Throws<OptionsValidationException>();
    }

    private static PromotionCodeDigestService Service(int activeVersion, params (int Version, string Value)[] keys) =>
        new(
            new FakeSecretResolver(keys.ToDictionary(key => $"v{key.Version}", key => key.Value, StringComparer.Ordinal)),
            Options.Create(new PromotionCodeLookupOptions { ActiveKeyVersion = activeVersion }));

    private static (int Version, string Value) Key(int version)
    {
        byte[] bytes = Enumerable.Range(0, 32).Select(i => (byte)(version + i)).ToArray();
        return (version, Convert.ToBase64String(bytes));
    }

    private sealed class FakeSecretResolver(IReadOnlyDictionary<string, string> keys) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(settingKey, SecretDefinitionRegistry.Keys.Promotions.CodeLookupHmacKey, StringComparison.Ordinal)
                || scope != SecretScope.Instance
                || scopeId is not null
                || !keys.TryGetValue(qualifier, out string? value))
            {
                return Task.FromResult(SecretResolutionResult.Unconfigured);
            }

            return Task.FromResult(SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, value, SecretSourceType.EnvironmentVariable, scope, scopeId, DateTime.UtcNow)));
        }

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CountingSecretResolver(IReadOnlyDictionary<string, string> keys) : ISecretResolver
    {
        public int QualifiedResolveCount { get; private set; }

        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default)
        {
            QualifiedResolveCount++;
            return keys.TryGetValue(qualifier, out string? value)
                ? Task.FromResult(SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, value, SecretSourceType.EnvironmentVariable, scope, scopeId, DateTime.UtcNow)))
                : Task.FromResult(SecretResolutionResult.Unconfigured);
        }

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
