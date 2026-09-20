using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeRegistrationPaymentOperationTests
{
    private static readonly Type[] Requests =
    [
        // Commands (8)
        typeof(StartGuestRegistrationPaymentCommand),
        typeof(RetryGuestRegistrationPaymentCommand),
        typeof(StartAuthenticatedRegistrationPaymentCommand),
        typeof(RetryAuthenticatedRegistrationPaymentCommand),
        typeof(RequestAuthenticatedRegistrationRefundCommand),
        typeof(RespondAuthenticatedRegistrationMaterialChangeCommand),
        typeof(CreateStudioRegistrationRefundCommand),
        typeof(RetryStudioRegistrationRefundCommand),

        // Queries (7)
        typeof(GetGuestRegistrationPaymentQuery),
        typeof(GetAuthenticatedRegistrationPaymentQuery),
        typeof(GetGuestPaidOrderAcceptanceQuery),
        typeof(GetAuthenticatedPaidOrderAcceptanceQuery),
        typeof(GetGuestRegistrationPaymentCheckoutTargetQuery),
        typeof(GetAuthenticatedRegistrationPaymentCheckoutTargetQuery),
        typeof(GetStudioRegistrationPaymentQuery)
    ];

    [Test]
    public async Task Discovery_RegistersAllFifteenRegistrationPaymentScopedPortsWithoutLegacyShapes()
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
        await Assert.That(ports.Length).IsEqualTo(15);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped && port.ImplementationFactory is not null)).IsTrue();
    }
}
