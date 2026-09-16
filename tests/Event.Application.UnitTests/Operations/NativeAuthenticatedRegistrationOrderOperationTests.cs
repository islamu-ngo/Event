using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAuthenticatedRegistrationOrderOperationTests
{
    private static readonly Type[] Requests =
    [
        // Commands (9)
        typeof(StartAuthenticatedRegistrationOrderCommand),
        typeof(ClaimGuestRegistrationOrderCommand),
        typeof(ContinueAuthenticatedRegistrationOrderCommand),
        typeof(FinalizeAuthenticatedRegistrationOrderCommand),
        typeof(CancelAuthenticatedRegistrationOrderCommand),
        typeof(LaunchAuthenticatedNativeRegistrationAttemptCommand),
        typeof(LaunchAuthenticatedRegistrationProviderAttemptCommand),
        typeof(SubmitAuthenticatedNativeRegistrationAttemptCommand),
        typeof(SkipAuthenticatedNativeRegistrationRequirementCommand),

        // Queries (2)
        typeof(GetCurrentRegistrationOrderQuery),
        typeof(GetAuthenticatedNativeRegistrationRequirementProgressQuery)
    ];

    [Test]
    public async Task Discovery_RegistersAllElevenAuthenticatedRegistrationOrderScopedPortsWithoutLegacyShapes()
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
        await Assert.That(ports.Length).IsEqualTo(11);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped && port.ImplementationFactory is not null)).IsTrue();
    }
}
