using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventType;
using Explore.Application.Features.EventTypes.Handlers.Queries;
using Explore.Application.Features.EventTypes.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventTypeOperationTests
{
    [Test]
    public async Task EventTypeCatalogue_RegistersItsScopedNativeQuery()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(GetEventTypeListRequest), typeof(GetEventTypeListRequestHandler)]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericArguments())
            .IsEquivalentTo(new[] { typeof(GetEventTypeListRequest), typeof(List<EventTypeListDto>) });
    }
}
