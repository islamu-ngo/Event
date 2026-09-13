using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Features.LocationRooms.Handlers.Commands;
using Explore.Application.Features.LocationRooms.Handlers.Queries;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeLocationRoomOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersThreeCommandsAndTwoQueries()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(CreateLocationRoomCommand), typeof(CreateLocationRoomCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateLocationRoomCommand), typeof(UpdateLocationRoomCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeleteLocationRoomCommand), typeof(DeleteLocationRoomCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(GetLocationRoomDetailRequest), typeof(GetLocationRoomDetailRequestHandler),
                typeof(IQueryHandler<,>), typeof(LocationRoomDto)),
            (typeof(GetLocationRoomsByLocationRequest), typeof(GetLocationRoomsByLocationRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<LocationRoomListDto>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(
            operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(5);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        foreach (var operation in operations)
        {
            var port = ports.Single(descriptor =>
                descriptor.ServiceType.GetGenericArguments()[0] == operation.Request).ServiceType;
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(operation.Port);
            await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(operation.Result);
        }
    }
}
