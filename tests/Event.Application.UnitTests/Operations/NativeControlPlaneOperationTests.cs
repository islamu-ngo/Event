using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeControlPlaneOperationTests
{
    [Test]
    public async Task ControlPlane_DiscoveryClosesEveryOperationWithExclusiveScopedPorts()
    {
        Type[] cohort = typeof(GetControlPlaneOverviewQuery).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                "Explore.Application.Features.ControlPlane.", StringComparison.Ordinal) == true)
            .ToArray();
        Type[] requests = cohort.Where(type => type.Namespace?.Contains(".Requests.", StringComparison.Ordinal) == true
            && typeof(Explore.Application.Authorization.ISecureRequest).IsAssignableFrom(type)).ToArray();
        var services = new ServiceCollection();
        services.AddNativeOperations(cohort);
        Type[] ports = services.Where(descriptor => !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) ||
                 type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .ToArray();

        await Assert.That(requests.Length).IsEqualTo(26);
        await Assert.That(ports.Select(port => port.GetGenericArguments()[0])).IsEquivalentTo(requests);
        await Assert.That(ports.Count(port => port.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(14);
        await Assert.That(ports.Count(port => port.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).IsEqualTo(12);
        foreach (Type port in ports)
        {
            Type request = port.GetGenericArguments()[0];
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
            await Assert.That(services.Single(descriptor => descriptor.ServiceType == port).Lifetime)
                .IsEqualTo(ServiceLifetime.Scoped);
        }
    }
}
