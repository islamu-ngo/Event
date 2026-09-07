using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class CerbosConfigResolverTests : IDisposable
{
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ITenantContext _tenantContext;
    private readonly MemoryCache _cache;
    private readonly CerbosConfigCacheRegistry _cacheRegistry;
    private readonly ICerbosClientFactory _clientFactory;
    private readonly ISecretResolver _secretResolver;
    private readonly ILogger<CerbosConfigResolver> _logger;

    private static readonly Guid TenantId = Guid.NewGuid();

    public CerbosConfigResolverTests()
    {
        _settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        _tenantContext = Substitute.For<ITenantContext>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _cacheRegistry = new CerbosConfigCacheRegistry();
        _clientFactory = Substitute.For<ICerbosClientFactory>();
        _secretResolver = Substitute.For<ISecretResolver>();
        _logger = Substitute.For<ILogger<CerbosConfigResolver>>();

        _tenantContext.TenantId.Returns(TenantId);
        _settingsResolver.ResolveAsync<bool>(
                GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.Mode,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns("custom_endpoint");
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(string.Empty);
        _secretResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    [Test]
    public async Task InvalidateCache_SpecificTenant_AllowsRefreshAndEvictsOldEndpointClient()
    {
        var endpoint = "https://tenant-old.example.com:443";
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.CustomEndpoint,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => endpoint);
        var resolver = CreateResolver();

        var first = await resolver.ResolveAsync();
        endpoint = "https://tenant-new.example.com:443";
        var cached = await resolver.ResolveAsync();

        resolver.InvalidateCache(TenantId);
        var refreshed = await resolver.ResolveAsync();

        await Assert.That(first?.Endpoint).IsEqualTo("https://tenant-old.example.com:443");
        await Assert.That(cached?.Endpoint).IsEqualTo("https://tenant-old.example.com:443");
        await Assert.That(refreshed?.Endpoint).IsEqualTo("https://tenant-new.example.com:443");
        _clientFactory.Received(1).Evict("https://tenant-old.example.com:443");
    }

    [Test]
    public async Task InvalidateCache_AllTenants_RemovesTrackedCacheEntriesAndEvictsClients()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.CustomEndpoint,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var context = call.ArgAt<SettingContext>(1);
                return context.TenantId == tenantA
                    ? "https://tenant-a-old.example.com:443"
                    : "https://tenant-b-old.example.com:443";
            });

        var resolverA = CreateResolver(tenantA);
        var resolverB = CreateResolver(tenantB);

        var firstA = await resolverA.ResolveAsync();
        var firstB = await resolverB.ResolveAsync();

        resolverA.InvalidateCache();

        await Assert.That(firstA?.Endpoint).IsEqualTo("https://tenant-a-old.example.com:443");
        await Assert.That(firstB?.Endpoint).IsEqualTo("https://tenant-b-old.example.com:443");
        _clientFactory.Received(1).Evict("https://tenant-a-old.example.com:443");
        _clientFactory.Received(1).Evict("https://tenant-b-old.example.com:443");
    }

    [Test]
    public async Task ResolveAsync_WhenCachedEntryExpiresAndEndpointChanges_EvictsPreviousEndpointClient()
    {
        var endpoint = "https://tenant-old.example.com:443";
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.CustomEndpoint,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => endpoint);
        var resolver = CreateResolver();

        var first = await resolver.ResolveAsync();
        endpoint = "https://tenant-new.example.com:443";
        _cache.Remove($"CerbosConfig:{TenantId}");

        var refreshed = await resolver.ResolveAsync();

        await Assert.That(first?.Endpoint).IsEqualTo("https://tenant-old.example.com:443");
        await Assert.That(refreshed?.Endpoint).IsEqualTo("https://tenant-new.example.com:443");
        _clientFactory.Received(1).Evict("https://tenant-old.example.com:443");
    }

    [Test]
    public async Task ResolveAsync_WithCustomModeAndBlankEndpoint_PreservesByoFailureModeInsteadOfInstanceFallback()
    {
        string username = $"user-{Guid.NewGuid():N}";
        string password = Guid.NewGuid().ToString("N");
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.CustomEndpoint,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(string.Empty);
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns("https://tenant-admin.example.com:8443");
        _secretResolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Cerbos.CustomAdminUsername,
                TenantId,
                Arg.Any<CancellationToken>())
            .Returns(Resolved(SecretDefinitionRegistry.Keys.Cerbos.CustomAdminUsername, username));
        _secretResolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Cerbos.CustomAdminPassword,
                TenantId,
                Arg.Any<CancellationToken>())
            .Returns(Resolved(SecretDefinitionRegistry.Keys.Cerbos.CustomAdminPassword, password));
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync();

        await Assert.That(result).IsNotNull();
        await Assert.That(result?.Mode).IsEqualTo(CerbosMode.CustomEndpoint);
        await Assert.That(result?.Endpoint).IsEqualTo(string.Empty);
        await Assert.That(result?.AdminEndpoint).IsEqualTo("https://tenant-admin.example.com:8443");
        await Assert.That(result?.AdminUsername).IsEqualTo(username);
        await Assert.That(result?.AdminPassword).IsEqualTo(password);
        await Assert.That(result?.IsInstanceDefault).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_WithBareCustomEndpoint_NormalizesGrpcAndAdminEndpoints()
    {
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.CustomEndpoint,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns("tenant-cerbos.example.com:443");
        _settingsResolver.ResolveAsync<string>(
                GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns("tenant-cerbos-admin.example.com:3592");
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync();

        await Assert.That(result?.Endpoint).IsEqualTo("https://tenant-cerbos.example.com:443");
        await Assert.That(result?.AdminEndpoint).IsEqualTo("https://tenant-cerbos-admin.example.com:3592");
    }

    [Test]
    public async Task ResolveAsync_WhenUsingInstancePdp_NormalizesBareConfiguredEndpoint()
    {
        _settingsResolver.ResolveAsync<bool>(
                GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled,
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
        var resolver = CreateResolver(instanceGrpcEndpoint: "cerbosgrpc.openislamu.org:443");

        var result = await resolver.ResolveAsync();

        await Assert.That(result?.Endpoint).IsEqualTo("https://cerbosgrpc.openislamu.org:443");
        await Assert.That(result?.IsInstanceDefault).IsTrue();
    }

    private CerbosConfigResolver CreateResolver(Guid? tenantId = null, string? instanceGrpcEndpoint = null)
    {
        var tenantContext = tenantId.HasValue
            ? Substitute.For<ITenantContext>()
            : _tenantContext;

        if (tenantId.HasValue)
            tenantContext.TenantId.Returns(tenantId.Value);

        return new CerbosConfigResolver(
            _settingsResolver,
            tenantContext,
            _cache,
            _cacheRegistry,
            _clientFactory,
            Options.Create(new CerbosSettings { GrpcEndpoint = instanceGrpcEndpoint ?? "http://localhost:3593" }),
            _secretResolver,
            _logger);
    }

    private static SecretResolutionResult Resolved(string key, string value) =>
        SecretResolutionResult.Resolved(new ResolvedSecret(
            key,
            value,
            SecretSourceType.EnvironmentVariable,
            SecretScope.Tenant,
            TenantId,
            DateTimeOffset.UtcNow));
}
