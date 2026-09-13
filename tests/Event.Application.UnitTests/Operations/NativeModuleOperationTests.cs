using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Modules.Handlers.Commands;
using Explore.Application.Features.Modules.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeModuleOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersBothTenantCapabilityWrites()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(EnableTenantModuleCommand), typeof(EnableTenantModuleCommandHandler),
            typeof(DisableTenantModuleCommand), typeof(DisableTenantModuleCommandHandler)
        ]);
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Select(port => port.ServiceType.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(EnableTenantModuleCommand), typeof(DisableTenantModuleCommand) });
        await Assert.That(ports.All(port =>
            port.ServiceType.GetGenericArguments()[1] == typeof(BaseCommandResponse<Guid>))).IsTrue();
    }
}
