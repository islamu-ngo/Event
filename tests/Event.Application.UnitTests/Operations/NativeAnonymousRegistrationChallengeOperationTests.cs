using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.RegistrationOrders.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAnonymousRegistrationChallengeOperationTests
{
    private static readonly Type[] Requests =
    [
        typeof(IssueAnonymousRegistrationChallengeCommand),
        typeof(ConsumeAnonymousRegistrationChallengeCommand)
    ];

    [Test]
    public async Task Discovery_RegistersAllTwoProtectedScopedPortsWithoutLegacyShapes()
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
        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped && port.ImplementationFactory is not null)).IsTrue();
    }
}
