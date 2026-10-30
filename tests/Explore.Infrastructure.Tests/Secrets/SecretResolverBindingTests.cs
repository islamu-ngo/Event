using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretResolverBindingTests : IAsyncDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly TestMeterFactory _meterFactory = new();
    private readonly SecretResolverMetrics _metrics;
    private readonly SecretResolverCacheState _cacheState;
    private readonly List<ResolverTestAuthority> _authorities = [];

    public SecretResolverBindingTests()
    {
        _metrics = new SecretResolverMetrics(_meterFactory);
        _cacheState = new SecretResolverCacheState(_cache);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var authority in _authorities)
            await authority.DisposeAsync();
        _metrics.Dispose();
        _meterFactory.Dispose();
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    [Arguments(SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret)]
    [Arguments(SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret)]
    [Arguments(SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks)]
    public async Task RuntimeCredentialReferenceMutationCannotReuseWarmCredential(string settingKey)
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(settingKey);
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            settingKey, SecretScope.Instance, null, definition.DefaultEnvironmentVariableName);
        binding.Id = Guid.CreateVersion7();
        var authority = LocalAuthority(new Dictionary<string, string?>
        {
            [definition.DefaultEnvironmentVariableName] = Guid.NewGuid().ToString("N")
        }, SecretConsumerRole.Bff);
        var source = new RecordingSecretSource(authority.Source);
        var resolver = new SecretResolver(new FakeSecretBindingRepository([binding]), [source],
            _cacheState, _metrics, NullLogger<SecretResolver>.Instance, authority.Owner);
        SecretResolutionResult initial = await resolver.ResolveAsync(settingKey, null);
        await Assert.That(initial.IsResolved).IsTrue();

        binding.EnvironmentVariableName = "UNRELATED_BACKEND_CREDENTIAL";
        SecretResolutionResult changed = await resolver.ResolveAsync(settingKey, null);

        await Assert.That(changed.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(changed.Value).IsNull();
        await Assert.That(source.CallCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret)]
    [Arguments(SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret)]
    [Arguments(SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks)]
    public async Task RuntimeCredentialLegacyFolderCannotReachSelectedAuthority(string settingKey)
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(settingKey);
        SecretBinding binding = SecretBinding.CreateInfisical(settingKey, SecretScope.Instance,
            null, "staging", "/legacy/shared", definition.DefaultInfisicalKey);
        binding.Id = Guid.CreateVersion7();
        var authority = await InfisicalAuthority(SecretConsumerRole.Bff);
        var source = new RecordingSecretSource(authority.Source);
        var resolver = new SecretResolver(new FakeSecretBindingRepository([binding]), [source],
            _cacheState, _metrics, NullLogger<SecretResolver>.Instance, authority.Owner);

        SecretResolutionResult result = await resolver.ResolveAsync(settingKey, null);

        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Invalid);
        await Assert.That(result.Value).IsNull();
        await Assert.That(source.LastBinding).IsNull();
    }

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
        string secretValue = Guid.NewGuid().ToString("N");
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
    public async Task ResolveAsync_WithoutStoredBinding_UsesAuthoritativeEnvironmentReference()
    {
        var authority = LocalAuthority(new Dictionary<string, string?>
        {
            ["MAIL_SMTP_PASSWORD"] = Guid.NewGuid().ToString("N")
        });
        var source = new RecordingSecretSource(authority.Source);
        var resolver = ResolverWithDefaults(authority, source);

        SecretResolutionResult resolved = await resolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Smtp.Password,
            tenantId: null,
            CancellationToken.None);

        await Assert.That(resolved.IsResolved).IsTrue();
        await Assert.That(source.LastBinding!.EnvironmentVariableName).IsEqualTo("MAIL_SMTP_PASSWORD");
        await Assert.That(source.LastBinding.SourceType).IsEqualTo(SecretSourceType.EnvironmentVariable);
    }

    [Test]
    public async Task ResolveAsync_WithoutStoredBinding_UsesAuthoritativeInfisicalReference()
    {
        var authority = await InfisicalAuthority();
        authority.Http!.Values["MAIL_SMTP_PASSWORD"] = Guid.NewGuid().ToString("N");
        var source = new RecordingSecretSource(authority.Source);
        var resolver = ResolverWithDefaults(authority, source);

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
        SecretAuthorityKind.Infisical,
        SecretSourceType.EnvironmentVariable)]
    [Arguments(
        SecretAuthorityKind.Environment,
        SecretSourceType.Infisical)]
    [Arguments(
        SecretAuthorityKind.UserSecrets,
        SecretSourceType.Infisical)]
    public async Task ResolveAsync_WhenStoredInstanceBindingUsesDifferentAuthority_IsInvalidWithoutFallback(
        SecretAuthorityKind selectedProvider,
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
        var local = LocalAuthority(new Dictionary<string, string?>
        {
            ["MAIL_SMTP_PASSWORD"] = Guid.NewGuid().ToString("N")
        }, provider: selectedProvider == SecretAuthorityKind.Infisical
            ? SecretAuthorityKind.Environment : selectedProvider);
        var remote = await InfisicalAuthority();
        remote.Http!.Values["MAIL_SMTP_PASSWORD"] = Guid.NewGuid().ToString("N");
        var authority = selectedProvider == SecretAuthorityKind.Infisical ? remote : local;
        var environment = new RecordingSecretSource(local.Source);
        var infisical = new RecordingSecretSource(remote.Source);
        var resolver = new SecretResolver(
            new FakeSecretBindingRepository([stale]),
            [environment, infisical],
            _cacheState,
            _metrics,
            NullLogger<SecretResolver>.Instance,
            authority.Owner);

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
        var local = LocalAuthority(new Dictionary<string, string?>
        {
            ["MAIL_SMTP_PASSWORD"] = Guid.NewGuid().ToString("N")
        });
        var selected = await InfisicalAuthority();
        var environment = new RecordingSecretSource(local.Source);
        var infisical = new RecordingSecretSource(selected.Source);
        var environmentResolver = new SecretResolver(
            repository,
            [environment, infisical],
            _cacheState,
            _metrics,
            NullLogger<SecretResolver>.Instance,
            local.Owner);

        SecretResolutionResult initial = await environmentResolver.ResolveAsync(
            binding.SettingKey,
            tenantId: null,
            CancellationToken.None);
        var infisicalResolver = new SecretResolver(
            repository,
            [environment, infisical],
            _cacheState,
            _metrics,
            NullLogger<SecretResolver>.Instance,
            selected.Owner);

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
        string firstValue = Guid.NewGuid().ToString("N");
        string secondValue = Guid.NewGuid().ToString("N");
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
                [binding.Id] = Guid.NewGuid().ToString("N"),
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
        string firstValue = Guid.NewGuid().ToString("N");
        string secondValue = Guid.NewGuid().ToString("N");
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactUnqualifiedTenantResolutionCannotInheritInstanceCredential(bool configured)
    {
        Guid tenantId = Guid.CreateVersion7();
        const string key = SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey;
        var instance = SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null, "INSTANCE_AI_KEY");
        instance.Id = Guid.CreateVersion7();
        var tenant = SecretBinding.CreateEnvironmentVariable(key, SecretScope.Tenant, tenantId, "TENANT_AI_KEY");
        tenant.Id = Guid.CreateVersion7();
        string instanceValue = Guid.NewGuid().ToString("N");
        string tenantValue = Guid.NewGuid().ToString("N");
        var resolver = Resolver(configured ? [instance, tenant] : [instance], new Dictionary<Guid, string>
        {
            [instance.Id] = instanceValue,
            [tenant.Id] = tenantValue
        });

        var result = await resolver.ResolveQualifiedAsync(key, SecretScope.Tenant, tenantId, "");

        await Assert.That(result.Status).IsEqualTo(configured ? SecretResolutionStatus.Resolved : SecretResolutionStatus.Unconfigured);
        await Assert.That(result.Value).IsEqualTo(configured ? tenantValue : null);
        if (configured)
        {
            await Assert.That(result.Scope).IsEqualTo(SecretScope.Tenant);
            await Assert.That(result.ScopeId).IsEqualTo(tenantId);
        }
    }

    [Test]
    public async Task CompatibleAiReferenceCannotSelectOfficialCredential()
    {
        Guid tenantId = Guid.CreateVersion7();
        const string key = SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey;
        var official = SecretBinding.CreateEnvironmentVariable(key, SecretScope.Tenant, tenantId, "OFFICIAL_AI_KEY");
        official.Id = Guid.CreateVersion7();
        var resolver = Resolver([official], new Dictionary<Guid, string>
        {
            [official.Id] = Guid.NewGuid().ToString("N")
        });

        var result = await resolver.ResolveQualifiedAsync(SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey,
            SecretScope.Tenant, tenantId, "");

        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Unconfigured);
        await Assert.That(result.Value).IsNull();
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
        string tenantValue = Guid.NewGuid().ToString("N");
        string instanceValue = Guid.NewGuid().ToString("N");
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
                [binding.Id] = Guid.NewGuid().ToString("N"),
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
            [first.Id] = Guid.NewGuid().ToString("N"),
            [second.Id] = Guid.NewGuid().ToString("N")
        };
        var resolver = Resolver(bindings, values, [first, second]);

        SecretResolutionResult initial = await resolver.ResolveAsync(settingKey, null, CancellationToken.None);
        bindings[0] = second;
        SecretResolutionResult switched = await resolver.ResolveAsync(settingKey, null, CancellationToken.None);

        await Assert.That(initial.Value).IsEqualTo(values[first.Id]);
        await Assert.That(switched.Value).IsEqualTo(values[second.Id]);
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
        string before = Guid.NewGuid().ToString("N");
        string after = Guid.NewGuid().ToString("N");
        var authority = LocalAuthority(new Dictionary<string, string?>
        {
            ["SMTP_PASSWORD"] = before,
            ["REPLACEMENT_SMTP_PASSWORD"] = after
        });
        var resolver = new SecretResolver(new FakeSecretBindingRepository([binding]), [authority.Source],
            _cacheState, _metrics, NullLogger<SecretResolver>.Instance, authority.Owner);

        var original = await resolver.ResolveAsync(settingKey, null, CancellationToken.None);
        binding.SwitchToEnvironmentVariable("REPLACEMENT_SMTP_PASSWORD");
        await resolver.InvalidateAsync(settingKey, SecretScope.Instance, null, CancellationToken.None);
        SecretResolutionResult refreshed = await resolver.ResolveAsync(settingKey, null, CancellationToken.None);

        await Assert.That(original.Value).IsEqualTo(before);
        await Assert.That(refreshed.Value).IsEqualTo(after);
        await Assert.That(refreshed.Secret!.BindingRevision).IsNotEqualTo(original.Secret!.BindingRevision);
    }

    private SecretResolver Resolver(IReadOnlyList<SecretBinding> bindings, IReadOnlyDictionary<Guid, string> values,
        IReadOnlyList<SecretBinding>? sourceBindings = null)
    {
        var authority = LocalAuthority((sourceBindings ?? bindings).ToDictionary(
            binding => binding.EnvironmentVariableName!, binding => (string?)values[binding.Id]));
        return new SecretResolver(
            new FakeSecretBindingRepository(bindings),
            [authority.Source],
            _cacheState,
            _metrics,
            NullLogger<SecretResolver>.Instance,
            authority.Owner);
    }

    [Test]
    public async Task ProviderFailureCannotExportExceptionOrPrivateCoordinatesThroughResolverLogger()
    {
        string canary = Guid.NewGuid().ToString("N");
        var binding = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Smtp.Password,
            SecretScope.Instance, null, "MAIL_SMTP_PASSWORD");
        binding.Id = Guid.CreateVersion7();
        var authority = LocalAuthority(new Dictionary<string, string?>
        {
            [binding.EnvironmentVariableName!] = canary
        });
        var logger = new SecretDiagnosticLog<SecretResolver>();
        var resolver = new SecretResolver(new FakeSecretBindingRepository([binding]),
            [new FailingSecretSource(canary)], _cacheState, _metrics, logger, authority.Owner);

        var result = await resolver.ResolveAsync(binding.SettingKey, null);

        await Assert.That(result.Status).IsEqualTo(SecretResolutionStatus.Unavailable);
        await Assert.That(logger.Output).Contains("secret_source_unavailable");
        await Assert.That(logger.Output).DoesNotContain(canary);
        await Assert.That(logger.Output).DoesNotContain(nameof(PrivateSourceException));
        await Assert.That(logger.Output).DoesNotContain(binding.EnvironmentVariableName!);
    }

    private sealed class PrivateSourceException(string message) : Exception(message);

    private sealed class FailingSecretSource(string canary) : ISecretSource, ISecretRevisionOwner
    {
        public SecretSourceType SourceType => SecretSourceType.EnvironmentVariable;
        public bool IsRevisionCurrent(SecretBinding binding, string? revision) => false;
        public Task<SecretResolutionResult> GetSecretAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
            Task.FromException<SecretResolutionResult>(new PrivateSourceException(canary + "\r\nforged-source-event"));
        public Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
            Task.FromException<bool>(new PrivateSourceException(canary));
    }

    private SecretResolver ResolverWithDefaults(
        ResolverTestAuthority authority,
        ISecretSource source) =>
        new(
            new FakeSecretBindingRepository([]),
            [source],
            _cacheState,
            _metrics,
            NullLogger<SecretResolver>.Instance,
            authority.Owner);

    private ResolverTestAuthority LocalAuthority(IReadOnlyDictionary<string, string?> values,
        SecretConsumerRole consumer = SecretConsumerRole.Api,
        SecretAuthorityKind provider = SecretAuthorityKind.Environment)
    {
        var authority = ResolverTestAuthority.Local(values, consumer, provider);
        _authorities.Add(authority);
        return authority;
    }

    private async Task<ResolverTestAuthority> InfisicalAuthority(SecretConsumerRole consumer = SecretConsumerRole.Api)
    {
        var authority = await ResolverTestAuthority.Infisical(consumer);
        _authorities.Add(authority);
        return authority;
    }

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

    private sealed class RecordingSecretSource(ISecretSource source) : ISecretSource, ISecretRevisionOwner
    {
        public SecretSourceType SourceType => source.SourceType;
        public SecretBinding? LastBinding { get; private set; }
        public int CallCount { get; private set; }

        public Task<SecretResolutionResult> GetSecretAsync(
            SecretBinding binding,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastBinding = binding;
            return source.GetSecretAsync(binding, cancellationToken);
        }

        public bool IsRevisionCurrent(SecretBinding binding, string? revision) =>
            ((ISecretRevisionOwner)source).IsRevisionCurrent(binding, revision);

        public Task<bool> ValidateAsync(
            SecretBinding binding,
            CancellationToken cancellationToken = default) => source.ValidateAsync(binding, cancellationToken);
    }
}

/// <summary>Owns a selected native authority without ambient environment or User Secrets access.</summary>
internal sealed class ResolverTestAuthority : IAsyncDisposable
{
    private readonly ConfigurationRoot? _configuration;
    private readonly bool _ownsHttp;
    internal SecretAuthorityAcquisition Owner { get; }
    internal ISecretSource Source { get; }
    internal SecretKitHttpAuthority? Http { get; }

    private ResolverTestAuthority(SecretAuthorityAcquisition owner, ISecretSource source,
        ConfigurationRoot? configuration = null, SecretKitHttpAuthority? http = null, bool ownsHttp = false)
    {
        Owner = owner;
        Source = source;
        _configuration = configuration;
        Http = http;
        _ownsHttp = ownsHttp;
    }

    internal static ResolverTestAuthority Local(IReadOnlyDictionary<string, string?> values,
        SecretConsumerRole consumer = SecretConsumerRole.Api,
        SecretAuthorityKind provider = SecretAuthorityKind.Environment)
    {
        var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var owner = SecretAuthorityAcquisition.OpenLocal(new(provider, "Testing", consumer), configuration);
        return new(owner, new EnvironmentSecretSource(owner), configuration);
    }

    internal static async Task<ResolverTestAuthority> Infisical(
        SecretConsumerRole consumer = SecretConsumerRole.Api, string project = "original-project",
        SecretKitHttpAuthority? http = null)
    {
        bool ownsHttp = http is null;
        http ??= await SecretKitHttpAuthority.StartAsync();
        try
        {
            using var credential = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
            var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
                new(SecretAuthorityKind.Infisical, "Testing", consumer),
                [consumer == SecretConsumerRole.Bff
                    ? new("/runtime/bff/oidc/google", "GOOGLE_CLIENT_SECRET", "runtime")
                    : new("/smtp", "MAIL_SMTP_PASSWORD", "runtime")],
                new(http.Origin, project, "staging", Guid.NewGuid().ToString("N"),
                    InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)),
                credential, new SecretAuthorityLoginAttempt());
            if (!opened.IsSuccess)
                throw new InvalidOperationException($"Test authority acquisition failed: {opened.Status}");
            var owner = opened.Value;
            return new(owner, new InfisicalSecretSource(owner), http: http, ownsHttp: ownsHttp);
        }
        catch
        {
            if (ownsHttp)
                await http.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Owner.DisposeAsync();
        if (_ownsHttp && Http is not null)
            await Http.DisposeAsync();
        _configuration?.Dispose();
    }
}
