namespace Event.Api.IntegrationTests.Features;

using Explore.API.Hosting;
using Explore.API.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

public sealed class EventReportingIntakeCacheInvalidationTests
{
    [Test]
    public async Task ReportingOutputCacheInvalidator_EvictsOnlyCanonicalEventResponseTags()
    {
        IOutputCacheStore store = Substitute.For<IOutputCacheStore>();
        var invalidator = new EventReportingOutputCacheInvalidator(store);

        await invalidator.InvalidateAsync(CancellationToken.None);

        await store.Received(2).EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.Received(1).EvictByTagAsync("detail-data", Arg.Any<CancellationToken>());
        await store.Received(1).EvictByTagAsync("list-data", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApiHostComposition_RegistersOneScopedReportingOutputCacheInvalidator()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });

        builder.AddApiHostServices(static () => false);

        ServiceDescriptor[] registrations = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(IEventReportingOutputCacheInvalidator))
            .ToArray();
        await Assert.That(registrations.Length).IsEqualTo(1);
        await Assert.That(registrations[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(registrations[0].ImplementationType).IsEqualTo(typeof(EventReportingOutputCacheInvalidator));
    }

    [Test]
    public async Task IntakeTenantSettingNotification_InvalidatesSettingCacheBeforeReportingOutputCache()
    {
        Guid tenantId = Guid.CreateVersion7();
        IHierarchicalSettingsResolver resolver = Substitute.For<IHierarchicalSettingsResolver>();
        IEventReportingOutputCacheInvalidator invalidator = Substitute.For<IEventReportingOutputCacheInvalidator>();
        var handler = new SettingCacheInvalidationHandler(resolver, [], [invalidator]);

        await handler.Handle(Notification(GovernanceSettingKeys.EventReporting.IntakeEnabled, tenantId), CancellationToken.None);

        Received.InOrder(() =>
        {
            resolver.InvalidateCache(SettingScope.Instance);
            resolver.InvalidateCache(SettingScope.Tenant, tenantId);
            invalidator.InvalidateAsync(Arg.Any<CancellationToken>());
        });
        await invalidator.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task IntakeInstanceSettingNotification_InvalidatesSettingCacheBeforeReportingOutputCache()
    {
        IHierarchicalSettingsResolver resolver = Substitute.For<IHierarchicalSettingsResolver>();
        IEventReportingOutputCacheInvalidator invalidator = Substitute.For<IEventReportingOutputCacheInvalidator>();
        var handler = new SettingCacheInvalidationHandler(resolver, [], [invalidator]);

        await handler.Handle(Notification(GovernanceSettingKeys.EventReporting.IntakeEnabled, tenantId: null), CancellationToken.None);

        resolver.Received(1).InvalidateCache(SettingScope.Instance);
        resolver.DidNotReceive().InvalidateCache(SettingScope.Tenant, Arg.Any<Guid>());
        await invalidator.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnrelatedSettingNotification_DoesNotInvalidateReportingOutputCache()
    {
        IHierarchicalSettingsResolver resolver = Substitute.For<IHierarchicalSettingsResolver>();
        IEventReportingOutputCacheInvalidator invalidator = Substitute.For<IEventReportingOutputCacheInvalidator>();
        var handler = new SettingCacheInvalidationHandler(resolver, [], [invalidator]);

        await handler.Handle(Notification(GovernanceSettingKeys.Events.RequireApproval, Guid.CreateVersion7()), CancellationToken.None);

        await invalidator.DidNotReceiveWithAnyArgs().InvalidateAsync(default);
    }

    private static SettingChangedNotification Notification(string key, Guid? tenantId) => new(
        key,
        "true",
        "false",
        SettingSource.TenantOverride,
        tenantId,
        Guid.CreateVersion7(),
        DateTime.UtcNow);
}
