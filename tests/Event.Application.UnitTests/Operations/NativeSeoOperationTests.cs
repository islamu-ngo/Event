using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Seo;
using Explore.Application.Features.Seo.Handlers.Queries;
using Explore.Application.Features.Seo.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeSeoOperationTests
{
    [Test]
    public async Task SitemapDiscovery_RegistersExclusiveScopedNativeQuery()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(GetSitemapEventsQuery), typeof(GetSitemapEventsQueryHandler)]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericArguments()[0]).IsEqualTo(typeof(GetSitemapEventsQuery));
        await Assert.That(ports[0].ServiceType.GetGenericArguments()[1]).IsEqualTo(typeof(IReadOnlyList<SitemapEventEntryDto>));
        await Assert.That(typeof(GetSitemapEventsQuery).GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
    }
}
