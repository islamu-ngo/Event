using Explore.Application.Analytics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Analytics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class AnalyticsConfigResolverTests
{
    [Test]
    public async Task ResolveAsync_ValidSettings_ReturnsResolvedConfiguration()
    {
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var tenantContext = Substitute.For<ITenantContext>();
        var tenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000009001");
        tenantContext.TenantId.Returns(tenantId);
        var secretResolver = Substitute.For<ISecretResolver>();
        var personalApiKey = Guid.NewGuid().ToString("N");

        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.Provider, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("posthog");
        settingsResolver.ResolveAsync<bool>(GovernanceSettingKeys.Analytics.Enabled, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(true);
        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.ConsentMode, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("identified");
        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.TransportMode, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("relay");
        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.ApiKey, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("pk_test");
        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.EndpointUrl, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("https://analytics.example.com");
        secretResolver.ResolveAsync(SecretDefinitionRegistry.Keys.Analytics.PersonalApiKey, tenantId, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SecretDefinitionRegistry.Keys.Analytics.PersonalApiKey,
                personalApiKey,
                SecretSourceType.EnvironmentVariable,
                SecretScope.Tenant,
                tenantId,
                DateTimeOffset.UtcNow)));

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Substitute.For<ILogger<AnalyticsConfigResolver>>();
        var resolver = new AnalyticsConfigResolver(settingsResolver, tenantContext, cache, secretResolver, logger);

        var config = await resolver.ResolveAsync();

        await Assert.That(config.Provider).IsEqualTo(AnalyticsProviderEnum.Posthog);
        await Assert.That(config.IsEnabled).IsTrue();
        await Assert.That(config.ConsentMode).IsEqualTo(AnalyticsConsentMode.Identified);
        await Assert.That(config.TransportMode).IsEqualTo(AnalyticsTransportMode.Relay);
        await Assert.That(config.ApiKey).IsEqualTo("pk_test");
        await Assert.That(config.EndpointUrl).IsEqualTo("https://analytics.example.com");
        await Assert.That(config.PersonalApiKey).IsEqualTo(personalApiKey);
    }

    [Test]
    public async Task ResolveAsync_InvalidProviderValue_FallsBackToNone()
    {
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.Parse("018e4e5c-7f00-7000-8000-000000009002"));
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);

        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.Provider, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("unknown-provider");
        settingsResolver.ResolveAsync<bool>(GovernanceSettingKeys.Analytics.Enabled, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(true);
        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.ConsentMode, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("unexpected");
        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.TransportMode, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("unexpected");
        settingsResolver.ResolveAsync<string>(GovernanceSettingKeys.Analytics.ApiKey, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("pk_test");

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Substitute.For<ILogger<AnalyticsConfigResolver>>();
        var resolver = new AnalyticsConfigResolver(settingsResolver, tenantContext, cache, secretResolver, logger);

        var config = await resolver.ResolveAsync();

        await Assert.That(config.Provider).IsEqualTo(AnalyticsProviderEnum.None);
        await Assert.That(config.ConsentMode).IsEqualTo(AnalyticsConsentMode.Pseudonymous);
        await Assert.That(config.TransportMode).IsEqualTo(AnalyticsTransportMode.Direct);
    }
}
