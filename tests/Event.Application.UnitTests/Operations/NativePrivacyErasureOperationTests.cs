using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.Features.PrivacyErasure.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativePrivacyErasureOperationTests
{
    private static readonly Type[] Requests =
    [
        typeof(GetPrivacyErasureStatusQuery)
    ];

    [Test]
    [Arguments(typeof(GetPrivacyErasureStatusQuery), typeof(IQuery<PrivacyErasureStatusDto?>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }

    [Test]
    public async Task Requests_HaveOneNativeShapeAndNoLegacyDispatchEscapeHatch()
    {
        await Assert.That(Requests.Length).IsEqualTo(1);

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1);
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
        }
    }

    [Test]
    public async Task ApplicationComposition_RegistersEveryPrivacyErasureOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(GetPrivacyErasureStatusQuery).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
