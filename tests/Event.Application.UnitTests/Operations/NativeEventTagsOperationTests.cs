using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventTags.Requests.Commands;
using Explore.Application.Features.EventTags.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventTagsOperationTests
{
    private static readonly Type[] Requests =
    [
        typeof(CreateEventTagsCommand), typeof(UpdateEventTagsCommand), typeof(DeleteEventTagsCommand),
        typeof(GetTagsByEventRequest), typeof(GetEventsByTagRequest),
        typeof(GetEventTagsDetailsRequest), typeof(GetEventTagsListRequest)
    ];

    [Test]
    public async Task Requests_UseExactlyOneNativeShapeWithoutLegacyDispatch()
    {
        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1).Because(request.Name);
            await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        }
    }

    [Test]
    public async Task ProductionDiscovery_RegistersSevenProtectedScopedPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(7);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
