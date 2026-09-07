using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Services.Federation;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoJetstreamReadinessHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_DormantCapabilityWithEmptyDidFilterIsHealthy()
    {
        IAtprotoJetstreamRuntimeStore store = StoreWithEnabledTenants([]);
        var healthCheck = CreateHealthCheck(store, [], Connected());

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["capabilityEnabled"]).IsEqualTo(false);
        await Assert.That(result.Data["allowlistConfigured"]).IsEqualTo(false);
    }

    [Test]
    public async Task CheckHealthAsync_EnabledCapabilityWithEmptyDidFilterIsHealthyForPublicDiscovery()
    {
        IAtprotoJetstreamRuntimeStore store = StoreWithEnabledTenants([Guid.CreateVersion7()]);
        var healthCheck = CreateHealthCheck(store, [], Connected());

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("public collection");
        await Assert.That(result.Data.Keys)
            .IsEquivalentTo(["capabilityEnabled", "allowlistConfigured", "connected", "cursor"]);
        await Assert.That(result.Data.Values).DoesNotContain(value => value is Guid or string);
    }

    [Test]
    public async Task CheckHealthAsync_EnabledCapabilityWithCuratedDidFilterIsHealthy()
    {
        IAtprotoJetstreamRuntimeStore store = StoreWithEnabledTenants([Guid.CreateVersion7()]);
        var healthCheck = CreateHealthCheck(store, ["did:plc:curated-owner"], Connected());

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["capabilityEnabled"]).IsEqualTo(true);
        await Assert.That(result.Data["allowlistConfigured"]).IsEqualTo(true);
        await Assert.That(result.Data["connected"]).IsEqualTo(true);
    }

    [Test]
    public async Task CheckHealthAsync_BriefReconnectStaysHealthy()
    {
        IAtprotoJetstreamRuntimeStore store = StoreWithEnabledTenants([Guid.CreateVersion7()]);
        var liveness = new AtprotoJetstreamLiveness();
        liveness.MarkConnected(DateTime.UtcNow.AddMinutes(-10), 0);
        liveness.MarkDisconnected(DateTime.UtcNow.AddSeconds(-2), 42);
        var healthCheck = CreateHealthCheck(store, [], liveness);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["connected"]).IsEqualTo(false);
        await Assert.That(result.Data["cursor"]).IsEqualTo(42L);
    }

    [Test]
    public async Task CheckHealthAsync_OutageBeyondReconnectBudgetDegrades()
    {
        IAtprotoJetstreamRuntimeStore store = StoreWithEnabledTenants([Guid.CreateVersion7()]);
        var liveness = new AtprotoJetstreamLiveness();
        liveness.MarkConnected(DateTime.UtcNow.AddHours(-2), 0);
        liveness.MarkDisconnected(DateTime.UtcNow.AddHours(-1), 7);
        var healthCheck = CreateHealthCheck(store, [], liveness);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("no open subscription");
    }

    [Test]
    public async Task CheckHealthAsync_QuietStreamWhileConnectedStaysHealthy()
    {
        // Calendar records are rare network-wide, so a healthy consumer can sit idle for hours.
        // Readiness must track connectivity, never time-since-last-event.
        IAtprotoJetstreamRuntimeStore store = StoreWithEnabledTenants([Guid.CreateVersion7()]);
        var liveness = new AtprotoJetstreamLiveness();
        liveness.MarkConnected(DateTime.UtcNow.AddDays(-3), 99);
        var healthCheck = CreateHealthCheck(store, [], liveness);

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    public async Task CheckHealthAsync_StoreFailureIsUnhealthy()
    {
        IAtprotoJetstreamRuntimeStore store = Substitute.For<IAtprotoJetstreamRuntimeStore>();
        store.ResolveEnabledTenantIdsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Guid>>(_ => throw new InvalidOperationException("unavailable"));
        var healthCheck = CreateHealthCheck(store, [], Connected());

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
    }

    private static AtprotoJetstreamLiveness Connected()
    {
        var liveness = new AtprotoJetstreamLiveness();
        liveness.MarkConnected(DateTime.UtcNow, 0);
        return liveness;
    }

    private static AtprotoJetstreamReadinessHealthCheck CreateHealthCheck(
        IAtprotoJetstreamRuntimeStore store,
        string[] allowedDids,
        AtprotoJetstreamLiveness liveness) => new(
            store,
            Options.Create(new AtprotoJetstreamOptions { AllowedDids = allowedDids }),
            liveness,
            TimeProvider.System);

    private static IAtprotoJetstreamRuntimeStore StoreWithEnabledTenants(IReadOnlyList<Guid> tenantIds)
    {
        IAtprotoJetstreamRuntimeStore store = Substitute.For<IAtprotoJetstreamRuntimeStore>();
        store.ResolveEnabledTenantIdsAsync(Arg.Any<CancellationToken>()).Returns(tenantIds);
        return store;
    }
}
