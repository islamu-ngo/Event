using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventRoleAssignmentOperationTests
{
    private static readonly Type[] Requests =
    [
        // Commands (5)
        typeof(AssignEventRoleByEmailCommand),
        typeof(AssignEventRoleCommand),
        typeof(RevokeEventRoleAssignmentCommand),
        typeof(TransferEventOwnershipCommand),
        typeof(UpdateEventRoleAssignmentWindowCommand),

        // Queries (3)
        typeof(GetAssignableEventRolePresetsRequest),
        typeof(GetCurrentUserEventPermissionsRequest),
        typeof(GetEventTeamListRequest)
    ];

    [Test]
    public async Task Discovery_RegistersAllEightEventRoleAssignmentScopedPortsWithoutLegacyShapes()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) || type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1).Because(request.Name);
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse().Because(request.Name);
        }

        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();

        await Assert.That(ports.Length).IsEqualTo(8);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped && port.ImplementationFactory is not null)).IsTrue();
    }
}
