using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.PaidEventPolicies.Requests.Commands;
using Explore.Application.Features.PaidEventPolicies.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativePaidEventPolicyOperationTests
{
    private static readonly Type[] Requests =
    [
        typeof(GetInstancePaidEventPolicyQuery),
        typeof(GetTenantPaidEventPolicyQuery),
        typeof(GetTenantPaidEventPolicyConfigurationQuery),
        typeof(ReviseInstancePaidEventPolicyCommand),
        typeof(ReviseTenantPaidEventPolicyCommand)
    ];

    [Test]
    public async Task Discovery_RegistersAllFiveProtectedScopedPortsWithoutLegacyShapes()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) || type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1).Because(request.Name);
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
        }
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(5);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped && port.ImplementationFactory is not null)).IsTrue();
    }
}
