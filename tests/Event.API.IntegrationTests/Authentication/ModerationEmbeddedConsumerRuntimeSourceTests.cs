using Explore.API.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed class ModerationEmbeddedConsumerRuntimeSourceTests
{
    [Test]
    public async Task CompositionCoversExactlyEmbeddedReadConsumersWithoutDuplicateSources()
    {
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var services = new ServiceCollection().AddSingleton(inventory);
        ModerationEmbeddedConsumerRuntimeSource.AddSources(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sources = scope.ServiceProvider.GetServices<IModerationConsumerRuntimeSource>().ToArray();
        var required = inventory.RequiredConsumers(ModerationCapability.ReadResource, ModerationNativeMode.Embedded);

        await Assert.That(sources.Length).IsEqualTo(23);
        await Assert.That(sources.Select(source => (source.ConsumerId, source.Mode)).Distinct().Count())
            .IsEqualTo(sources.Length);
        await Assert.That(sources.All(source => source.Mode == ModerationNativeMode.Embedded)).IsTrue();
        await Assert.That(sources.Select(source => source.ConsumerId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(required)).IsTrue();
        foreach (var source in sources)
            await Assert.That(source.ReadCurrent()).IsNull();
    }

    [Test]
    public async Task CalendarObservationRequiresBothConcreteOwnersAndBindsCurrentArtifacts()
    {
        var services = CalendarServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var source = scope.ServiceProvider.GetServices<IModerationConsumerRuntimeSource>()
            .Single(source => source.ConsumerId == "calendar.export");
        var observation = source.ReadCurrent();

        await Assert.That(observation).IsNotNull();
        await Assert.That(observation!.ConsumerId).IsEqualTo("calendar.export");
        await Assert.That(observation.Mode).IsEqualTo(ModerationNativeMode.Embedded);
        await Assert.That(observation.InventoryFingerprint)
            .IsEqualTo(provider.GetRequiredService<ModerationCapabilityRegistry>().Fingerprint);
        await Assert.That(observation.RuntimeGeneration.Length).IsEqualTo(64);
        await Assert.That(observation.ProductArtifactDigest!.Length).IsEqualTo(64);
        await Assert.That(observation.RuntimeGeneration).IsEqualTo(ModerationMeasurementProductManifest.RuntimeGeneration(
            observation.ConsumerId, observation.Mode, observation.InventoryFingerprint, observation.ProductArtifactDigest));
        await Assert.That(source.ReadCurrent()).IsEqualTo(observation);
    }

    [Test]
    public async Task RemovingOneCalendarOwnerLeavesConsumerUnobserved()
    {
        var services = CalendarServices();
        services.Remove(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GetAttendeeEventCalendarExportRequestHandler)));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var source = scope.ServiceProvider.GetServices<IModerationConsumerRuntimeSource>()
            .Single(source => source.ConsumerId == "calendar.export");

        await Assert.That(source.ReadCurrent()).IsNull();
    }

    [Test]
    public async Task MismatchedConcreteOwnerCannotProduceRuntimeIdentity()
    {
        var services = CalendarServices();
        var provider = new OwnerIdentityProvider();
        var source = services.Where(descriptor => descriptor.ServiceType == typeof(IModerationConsumerRuntimeSource))
            .Select(descriptor => (IModerationConsumerRuntimeSource)descriptor.ImplementationFactory!(provider))
            .Single(source => source.ConsumerId == "calendar.export");

        await Assert.That(source.ReadCurrent()).IsNull();
    }

    private static IServiceCollection CalendarServices()
    {
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var services = new ServiceCollection().AddSingleton(inventory);
        // Observation reads types only; these native owners never execute or touch a repository.
        services.AddSingleton(new GetEventCalendarExportRequestHandler(null!, null!, null!, null!));
        services.AddSingleton(new GetAttendeeEventCalendarExportRequestHandler(null!, null!, null!));
        return ModerationEmbeddedConsumerRuntimeSource.AddSources(services);
    }

    private sealed class OwnerIdentityProvider : IServiceProvider
    {
        private readonly ModerationCapabilityRegistry inventory =
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());

        public object? GetService(Type serviceType) =>
            serviceType == typeof(ModerationCapabilityRegistry) ? inventory
            : serviceType == typeof(GetEventCalendarExportRequestHandler) ? new object() : null;
    }
}
