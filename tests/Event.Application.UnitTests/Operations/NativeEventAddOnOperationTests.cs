using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventAddOns.Requests.Commands;
using Explore.Application.Features.EventAddOns.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventAddOnOperationTests
{
    private static readonly Type[] Requests =
    [
        // Commands (7)
        typeof(CreateEventAddOnCatalogDraftCommand),
        typeof(AddEventAddOnCatalogItemCommand),
        typeof(PublishEventAddOnCatalogCommand),
        typeof(RetireEventAddOnCatalogCommand),
        typeof(ReserveRegistrationOrderAddOnsCommand),
        typeof(FulfillRegistrationOrderAddOnCommand),
        typeof(RefundRegistrationOrderAddOnCommand),

        // Queries (2)
        typeof(GetEventAddOnCatalogQuery),
        typeof(GetRegistrationOrderAddOnsQuery)
    ];

    [Test]
    public async Task Discovery_RegistersAllNineEventAddOnScopedPortsWithoutLegacyShapes()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) || type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1).Because(request.Name);
            await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        }
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(9);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped && port.ImplementationFactory is not null)).IsTrue();
    }
}
