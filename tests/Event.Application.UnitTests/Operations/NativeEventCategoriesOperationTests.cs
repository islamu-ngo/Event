using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventCategories.Requests.Commands;
using Explore.Application.Features.EventCategories.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventCategoriesOperationTests
{
    private static readonly Type[] Requests =
    [
        typeof(CreateEventCategoriesCommand), typeof(UpdateEventCategoriesCommand),
        typeof(DeleteEventCategoriesCommand), typeof(GetCategoriesByEventRequest),
        typeof(GetEventsByCategoryRequest), typeof(GetEventCategoriesDetailsRequest),
        typeof(GetEventCategoriesListRequest)
    ];

    [Test]
    public async Task Requests_HaveOneNativeShapeAndNoLegacyDispatchEscapeHatch()
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
    public async Task ApplicationComposition_RegistersEveryCategoryOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(CreateEventCategoriesCommand).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(7);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
