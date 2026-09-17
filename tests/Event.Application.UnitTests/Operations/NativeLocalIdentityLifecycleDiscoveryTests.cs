using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeLocalIdentityLifecycleDiscoveryTests
{
    [Test]
    public async Task AutomaticDiscoveryPublishesOneScopedQueryWithoutLegacyDispatch()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        var ports = services.Where(descriptor => !descriptor.IsKeyedService
            && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
            && descriptor.ServiceType.GenericTypeArguments[0] == typeof(GetLocalIdentityLifecycleCapabilitiesQuery)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GenericTypeArguments[1]).IsEqualTo(typeof(LocalIdentityLifecycleCapabilities));
        await Assert.That(typeof(GetLocalIdentityLifecycleCapabilitiesQuery).GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
    }
}
