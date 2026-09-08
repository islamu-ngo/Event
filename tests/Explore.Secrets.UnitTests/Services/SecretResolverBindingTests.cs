using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Observability;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.UnitTests.Services;

public sealed class SecretResolverBindingTests
{
    [Test]
    public async Task ISecretResolver_RequiresExplicitQualifiedResolutionImplementation()
    {
        var method = typeof(ISecretResolver).GetMethod(nameof(ISecretResolver.ResolveQualifiedAsync));

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.IsAbstract).IsTrue();
    }

    [Test]
    public async Task ISecretResolver_ReturnsTypedResolutionOutcome()
    {
        var method = typeof(ISecretResolver).GetMethod(nameof(ISecretResolver.ResolveAsync));

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType.GenericTypeArguments.Single().Name)
            .IsEqualTo("SecretResolutionResult");
    }

    [Test]
    public async Task ResolveAsync_CharacterizesExistingTenantToInstanceBindingFallback()
    {
        Guid tenantId = Guid.CreateVersion7();
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Stripe.WebhookSecret,
            SecretScope.Instance,
            scopeId: null,
            "WEBHOOK_SECRET");
        binding.Id = Guid.CreateVersion7();
        string secretValue = SecretsTestValues.CreateSecret();
        var resolver = Resolver([binding], new Dictionary<Guid, string> { [binding.Id] = secretValue });

        SecretResolutionResult resolved = await resolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Stripe.WebhookSecret,
            tenantId,
            CancellationToken.None);

        await Assert.That(resolved.IsResolved).IsTrue();
        await Assert.That(resolved.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(resolved.Value).IsEqualTo(secretValue);
    }

    [Test]
    public async Task ResolveAsync_WithoutStoredBinding_UsesCanonicalEnvironmentReference()
    {
        var source = new RecordingSecretSource(SecretSourceType.EnvironmentVariable);
        var resolver = ResolverWithDefaults(SecretProviderType.Environment, source);

        SecretResolutionResult resolved = await resolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Smtp.Password,
            tenantId: null,
            CancellationToken.None);

        await Assert.That(resolved.IsResolved).IsTrue();
        await Assert.That(source.LastBinding!.EnvironmentVariableName).IsEqualTo("MAIL_SMTP_PASSWORD");
        await Assert.That(source.LastBinding.SourceType).IsEqualTo(SecretSourceType.EnvironmentVariable);
    }

    [Test]
    public async Task ResolveAsync_WithoutStoredBinding_UsesCanonicalInfisicalReference()
    {
        var source = new RecordingSecretSource(SecretSourceType.Infisical);
        var resolver = ResolverWithDefaults(SecretProviderType.Infisical, source, "staging");

        SecretResolutionResult resolved = await resolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Smtp.Password,
            tenantId: null,
            CancellationToken.None);

        await Assert.That(resolved.IsResolved).IsTrue();
        await Assert.That(source.LastBinding!.InfisicalEnvironment).IsEqualTo("staging");
        await Assert.That(source.LastBinding.InfisicalPath).IsEqualTo("/smtp");
        await Assert.That(source.LastBinding.InfisicalKey).IsEqualTo("MAIL_SMTP_PASSWORD");
    }

    [Test]
    [Arguments(
        SecretProviderType.Infisical,
        SecretSourceType.EnvironmentVariable)]
    [Arguments(
        SecretProviderType.Environment,
        SecretSourceType.Infisical)]
    [Arguments(
        SecretProviderType.UserSecrets,
        SecretSourceType.Infisical)]
    public async Task ResolveAsync_WhenStoredInstanceBindingUsesDifferentAuthority_IsInvalidWithoutFallback(
        SecretProviderType selectedProvider,
        SecretSourceType storedSource)
    {
        SecretBinding stale = storedSource == SecretSourceType.Infisical
            ? SecretBinding.CreateInfisical(
                SecretDefinitionRegistry.Keys.Smtp.Password,
                SecretScope.Instance,
                scopeId: null,
                "staging",
                "/smtp",
                "MAIL_SMTP_PASSWORD")
            : SecretBinding.CreateEnvironmentVariable(
                SecretDefinitionRegistry.Keys.Smtp.Password,
                SecretScope.Instance,
                scopeId: null,
                "MAIL_SMTP_PASSWORD");
        stale.Id = Guid.CreateVersion7();
        var environment = new RecordingSecretSource(
            SecretSourceType.EnvironmentVariable);
        var infisical = new RecordingSecretSource(SecretSourceType.Infisical);
        var resolver = new SecretResolver(
            new FakeSecretBindingRepository([stale]),
            [environment, infisical],
            new MemoryCache(new MemoryCacheOptions()),
            new SecretResolverMetrics(new TestMeterFactory()),
            NullLogger<SecretResolver>.Instance,
            Options.Create(new SecretProviderOptions
            {
                Provider = selectedProvider,
                Infisical = new InfisicalOptions { Environment = "staging" }
            }));

        SecretResolutionResult resolved = await resolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Smtp.Password,
            tenantId: null,
            CancellationToken.None);

        await Assert.That(resolved.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(resolved.Secret).IsNull();
        await Assert.That(resolved.Value).IsNull();
        await Assert.That(resolved.Source).IsNull();
        await Assert.That(environment.CallCount).IsEqualTo(0);
        await Assert.That(environment.LastBinding).IsNull();
        await Assert.That(infisical.CallCount).IsEqualTo(0);
        await Assert.That(infisical.LastBinding).IsNull();
    }

    [Test]
    public async Task ResolveAsync_WhenCachedBindingAuthorityChanges_IsInvalidWithoutCacheReuse()
    {
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Smtp.Password,
            SecretScope.Instance,
            scopeId: null,
            "MAIL_SMTP_PASSWORD");
        binding.Id = Guid.CreateVersion7();
        var repository = new FakeSecretBindingRepository([binding]);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var meterFactory = new TestMeterFactory();
        using var metrics = new SecretResolverMetrics(meterFactory);
        var environment = new RecordingSecretSource(SecretSourceType.EnvironmentVariable);
        var infisical = new RecordingSecretSource(SecretSourceType.Infisical);
        var environmentResolver = new SecretResolver(
            repository,
            [environment, infisical],
            cache,
            metrics,
            NullLogger<SecretResolver>.Instance,
            Options.Create(new SecretProviderOptions { Provider = SecretProviderType.Environment }));

        SecretResolutionResult initial = await environmentResolver.ResolveAsync(
            binding.SettingKey,
            tenantId: null,
            CancellationToken.None);
        var infisicalResolver = new SecretResolver(
            repository,
            [environment, infisical],
            cache,
            metrics,
            NullLogger<SecretResolver>.Instance,
            Options.Create(new SecretProviderOptions
            {
                Provider = SecretProviderType.Infisical,
                Infisical = new InfisicalOptions { Environment = "staging" }
            }));

        SecretResolutionResult resolved = await infisicalResolver.ResolveAsync(
            binding.SettingKey,
            tenantId: null,
            CancellationToken.None);

        await Assert.That(initial.IsResolved).IsTrue();
        await Assert.That(environment.CallCount).IsEqualTo(1);
        await Assert.That(resolved.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(resolved.Secret).IsNull();
        await Assert.That(resolved.Value).IsNull();
        await Assert.That(resolved.Source).IsNull();
        await Assert.That(infisical.CallCount).IsEqualTo(0);
        await Assert.That(infisical.LastBinding).IsNull();
    }

    [Test]
    public async Task ResolveTenantBindingAsync_UsesExactQualifiedTenantBinding()
    {
        Guid tenantId = Guid.CreateVersion7();
        SecretBinding first = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
            SecretScope.Tenant,
            tenantId,
            "TOKEN_A",
            qualifier: "connection-a");
        first.Id = Guid.CreateVersion7();
        SecretBinding second = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
            SecretScope.Tenant,
            tenantId,
            "TOKEN_B",
            qualifier: "connection-b");
        second.Id = Guid.CreateVersion7();
        string firstValue = SecretsTestValues.CreateSecret();
        string secondValue = SecretsTestValues.CreateSecret();
        var resolver = Resolver([first, second], new Dictionary<Guid, string>
        {
            [first.Id] = firstValue,
            [second.Id] = secondValue
        });

        SecretResolutionResult resolved = await resolver.ResolveTenantBindingAsync(tenantId, second.Id, CancellationToken.None);

        await Assert.That(resolved.IsResolved).IsTrue();
        await Assert.That(resolved.Value).IsEqualTo(secondValue);
        await Assert.That(resolved.Scope).IsEqualTo(SecretScope.Tenant);
        await Assert.That(resolved.ScopeId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task ResolveTenantBindingAsync_DoesNotFallBackToOtherTenantBinding()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid otherTenantId = Guid.CreateVersion7();
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret,
            SecretScope.Tenant,
            otherTenantId,
            "WEBHOOK_SECRET",
            qualifier: "binding");
        binding.Id = Guid.CreateVersion7();
        var resolver = Resolver(
            [binding],
            new Dictionary<Guid, string>
            {
                [binding.Id] = SecretsTestValues.CreateSecret(),
            });

        SecretResolutionResult resolved = await resolver.ResolveTenantBindingAsync(tenantId, binding.Id, CancellationToken.None);

        await Assert.That(resolved.Status).IsEqualTo(SecretResolutionStatus.Unconfigured);
    }

    [Test]
    public async Task ConcurrentTenantResolution_DoesNotShareCachedSecretAcrossTenants()
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        SecretBinding first = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
            SecretScope.Tenant,
            firstTenantId,
            "FIRST_TENANT_TOKEN");
        first.Id = Guid.CreateVersion7();
        SecretBinding second = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
            SecretScope.Tenant,
            secondTenantId,
            "SECOND_TENANT_TOKEN");
        second.Id = Guid.CreateVersion7();
        string firstValue = SecretsTestValues.CreateSecret();
        string secondValue = SecretsTestValues.CreateSecret();
        var resolver = Resolver([first, second], new Dictionary<Guid, string>
        {
            [first.Id] = firstValue,
            [second.Id] = secondValue
        });

        Task<SecretResolutionResult>[] resolutions = Enumerable.Range(0, 64)
            .Select(index => resolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
                index % 2 == 0 ? firstTenantId : secondTenantId,
                CancellationToken.None))
            .ToArray();

        SecretResolutionResult[] results = await Task.WhenAll(resolutions);
        for (int index = 0; index < results.Length; index++)
        {
            Guid expectedTenant = index % 2 == 0 ? firstTenantId : secondTenantId;
            string expectedValue = index % 2 == 0 ? firstValue : secondValue;
            await Assert.That(results[index].IsResolved).IsTrue();
            await Assert.That(results[index].ScopeId).IsEqualTo(expectedTenant);
            await Assert.That(results[index].Value).IsEqualTo(expectedValue);
        }
    }

    [Test]
    public async Task ResolveQualifiedAsync_UsesExactQualifierWithoutTenantFallback()
    {
        Guid tenantId = Guid.CreateVersion7();
        SecretBinding tenantBinding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            SecretScope.Tenant,
            tenantId,
            "TENANT_PROMOTION_KEY",
            qualifier: "v7");
        tenantBinding.Id = Guid.CreateVersion7();
        SecretBinding instanceBinding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            SecretScope.Instance,
            scopeId: null,
            "INSTANCE_PROMOTION_KEY",
            qualifier: "v7");
        instanceBinding.Id = Guid.CreateVersion7();
        string tenantValue = SecretsTestValues.CreateSecret();
        string instanceValue = SecretsTestValues.CreateSecret();
        var resolver = Resolver([tenantBinding, instanceBinding], new Dictionary<Guid, string>
        {
            [tenantBinding.Id] = tenantValue,
            [instanceBinding.Id] = instanceValue
        });

        SecretResolutionResult resolved = await resolver.ResolveQualifiedAsync(
            SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            SecretScope.Instance,
            scopeId: null,
            "v7",
            CancellationToken.None);

        await Assert.That(resolved.IsResolved).IsTrue();
        await Assert.That(resolved.Value).IsEqualTo(instanceValue);
        await Assert.That(resolved.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(resolved.ScopeId).IsNull();
    }

    [Test]
    public async Task ResolveQualifiedAsync_MissingQualifierReturnsUnconfigured()
    {
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Promotions.CodeLookupHmacKey,
            SecretScope.Instance,
            scopeId: null,
            "PROMOTION_KEY",
            qualifier: "v1");
        binding.Id = Guid.CreateVersion7();
        var resolver = Resolver(
            [binding],
            new Dictionary<Guid, string>
            {
                [binding.Id] = SecretsTestValues.CreateSecret(),
            });

        SecretResolutionResult resolved = await resolver.ResolveQualifiedAsync(
            SecretDefinitionRegistry.Keys.Promotions.CodeLookupHmacKey,
            SecretScope.Instance,
            scopeId: null,
            "v2",
            CancellationToken.None);

        await Assert.That(resolved.Status).IsEqualTo(SecretResolutionStatus.Unconfigured);
    }

    [Test]
    public async Task SourceReferenceIdentityPreventsCrossBindingCacheReuse()
    {
        string settingKey = SecretDefinitionRegistry.Keys.Smtp.Password;
        SecretBinding first = SecretBinding.CreateEnvironmentVariable(
            settingKey,
            SecretScope.Instance,
            scopeId: null,
            "FIRST_SMTP_PASSWORD");
        first.Id = Guid.CreateVersion7();
        SecretBinding second = SecretBinding.CreateEnvironmentVariable(
            settingKey,
            SecretScope.Instance,
            scopeId: null,
            "SECOND_SMTP_PASSWORD");
        second.Id = Guid.CreateVersion7();
        var bindings = new List<SecretBinding> { first };
        var values = new Dictionary<Guid, string>
        {
            [first.Id] = "first-value",
            [second.Id] = "second-value"
        };
        var resolver = Resolver(bindings, values);

        SecretResolutionResult initial = await resolver.ResolveAsync(settingKey, null, CancellationToken.None);
        bindings[0] = second;
        SecretResolutionResult switched = await resolver.ResolveAsync(settingKey, null, CancellationToken.None);

        await Assert.That(initial.Value).IsEqualTo("first-value");
        await Assert.That(switched.Value).IsEqualTo("second-value");
    }

    [Test]
    public async Task InvalidationReloadsChangedMetadataValue()
    {
        string settingKey = SecretDefinitionRegistry.Keys.Smtp.Password;
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            settingKey,
            SecretScope.Instance,
            scopeId: null,
            "SMTP_PASSWORD");
        binding.Id = Guid.CreateVersion7();
        var values = new Dictionary<Guid, string> { [binding.Id] = "before" };
        var resolver = Resolver([binding], values);

        _ = await resolver.ResolveAsync(settingKey, null, CancellationToken.None);
        values[binding.Id] = "after";
        await resolver.InvalidateAsync(settingKey, SecretScope.Instance, null, CancellationToken.None);
        SecretResolutionResult refreshed = await resolver.ResolveAsync(settingKey, null, CancellationToken.None);

        await Assert.That(refreshed.Value).IsEqualTo("after");
    }

    private static SecretResolver Resolver(IReadOnlyList<SecretBinding> bindings, IReadOnlyDictionary<Guid, string> values)
    {
        return new SecretResolver(
            new FakeSecretBindingRepository(bindings),
            [new FakeSecretSource(values)],
            new MemoryCache(new MemoryCacheOptions()),
            new SecretResolverMetrics(new TestMeterFactory()),
            NullLogger<SecretResolver>.Instance,
            Options.Create(new SecretProviderOptions { Provider = SecretProviderType.Environment }));
    }

    private static SecretResolver ResolverWithDefaults(
        SecretProviderType provider,
        ISecretSource source,
        string infisicalEnvironment = "") =>
        new(
            new FakeSecretBindingRepository([]),
            [source],
            new MemoryCache(new MemoryCacheOptions()),
            new SecretResolverMetrics(new TestMeterFactory()),
            NullLogger<SecretResolver>.Instance,
            Options.Create(new SecretProviderOptions
            {
                Provider = provider,
                Infisical = new InfisicalOptions { Environment = infisicalEnvironment }
            }));

    private sealed class FakeSecretBindingRepository(IReadOnlyList<SecretBinding> bindings) : ISecretBindingRepository
    {
        public Task<SecretBinding?> GetByTenantAndIdAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(bindings.SingleOrDefault(binding => binding.ScopeId == tenantId && binding.Id == bindingId));
        public Task<SecretBinding?> GetByKeyAndScopeAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(bindings.SingleOrDefault(binding => binding.SettingKey == settingKey && binding.Scope == scope && binding.ScopeId == scopeId && binding.Qualifier == string.Empty));
        public Task<SecretBinding?> GetByKeyScopeAndQualifierAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(bindings.SingleOrDefault(binding => binding.SettingKey == settingKey && binding.Scope == scope && binding.ScopeId == scopeId && binding.Qualifier == qualifier));
        public Task<IReadOnlyList<SecretBinding>> GetByScopeAsync(SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretBinding>>([.. bindings.Where(binding => binding.Scope == scope && binding.ScopeId == scopeId)]);
        public Task<IReadOnlyList<SecretBinding>> GetAllForKeyAsync(string settingKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretBinding>>([.. bindings.Where(binding => binding.SettingKey == settingKey)]);
        public Task<bool> ExistsForScopeAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(bindings.Any(binding => binding.SettingKey == settingKey && binding.Scope == scope && binding.ScopeId == scopeId && binding.Qualifier == string.Empty));
        public Task<bool> ExistsForScopeAndQualifierAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(bindings.Any(binding => binding.SettingKey == settingKey && binding.Scope == scope && binding.ScopeId == scopeId && binding.Qualifier == qualifier));
        public Task<SecretBinding?> GetById(Guid id) => Task.FromResult(bindings.SingleOrDefault(binding => binding.Id == id));
        public Task<IReadOnlyList<SecretBinding>> GetAll() => Task.FromResult(bindings);
        public Task<(IReadOnlyList<SecretBinding> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => Task.FromResult((bindings, bindings.Count));
        public Task<bool> Exists(Guid id) => Task.FromResult(bindings.Any(binding => binding.Id == id));
        public Task<SecretBinding> Create(SecretBinding entity) => throw new NotImplementedException();
        public Task Update(SecretBinding entity) => throw new NotImplementedException();
        public Task Delete(SecretBinding entity) => throw new NotImplementedException();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name ?? "test");
        public void Dispose() { }
    }

    private sealed class FakeSecretSource(IReadOnlyDictionary<Guid, string> values) : ISecretSource
    {
        public SecretSourceType SourceType => SecretSourceType.EnvironmentVariable;
        public Task<SecretResolutionResult> GetSecretAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
            Task.FromResult(values.TryGetValue(binding.Id, out var value)
                ? SecretResolutionResult.Resolved(new ResolvedSecret(
                    binding.SettingKey,
                    value,
                    binding.SourceType,
                    binding.Scope,
                    binding.ScopeId,
                    DateTime.UtcNow))
                : SecretResolutionResult.Unconfigured);
        public Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
            Task.FromResult(values.ContainsKey(binding.Id));
    }

    private sealed class RecordingSecretSource(SecretSourceType sourceType) : ISecretSource
    {
        public SecretSourceType SourceType => sourceType;
        public SecretBinding? LastBinding { get; private set; }
        public int CallCount { get; private set; }

        public Task<SecretResolutionResult> GetSecretAsync(
            SecretBinding binding,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastBinding = binding;
            return Task.FromResult(SecretResolutionResult.Resolved(new ResolvedSecret(
                binding.SettingKey,
                SecretsTestValues.CreateSecret(),
                binding.SourceType,
                binding.Scope,
                binding.ScopeId,
                DateTime.UtcNow)));
        }

        public Task<bool> ValidateAsync(
            SecretBinding binding,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
