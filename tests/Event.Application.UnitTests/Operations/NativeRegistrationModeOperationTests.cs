using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.RegistrationModes.Handlers.Queries;
using Explore.Application.Features.RegistrationModes.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeRegistrationModeOperationTests
{
    [Test]
    public async Task RegistrationModeCapability_RegistersTwoScopedQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetRegistrationModeDetailsRequest), typeof(GetRegistrationModeDetailsRequestHandler),
            typeof(GetRegistrationModeListRequest), typeof(GetRegistrationModeListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Select(port => port.ServiceType.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetRegistrationModeDetailsRequest), typeof(GetRegistrationModeListRequest) });
    }

}
