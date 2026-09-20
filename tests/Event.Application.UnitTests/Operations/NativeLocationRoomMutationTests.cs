using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.LocationRooms.Handlers.Commands;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeLocationRoomMutationTests
{
    [Test]
    public async Task ActiveSchedule_BlocksReparentingButAllowsSameLocationUpdates()
    {
        var tenantId = Guid.CreateVersion7();
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Current venue",
            Country = "BE",
            City = "Brussels",
            TenantId = tenantId
        };
        var target = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Target venue",
            Country = "BE",
            City = "Brussels",
            TenantId = tenantId
        };
        var room = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            LocationId = location.Id,
            Location = location,
            TenantId = tenantId,
            Tenant = null!,
            Name = "Room",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var locations = Substitute.For<ILocationRepository>();
        locations.Exists(Arg.Any<Guid>()).Returns(call => call.Arg<Guid>() == location.Id || call.Arg<Guid>() == target.Id);
        locations.GetById(location.Id).Returns(location);
        locations.GetById(target.Id).Returns(target);
        var rooms = Substitute.For<ILocationRoomRepository>();
        rooms.GetById(room.Id).Returns(room);
        var scheduled = true;
        rooms.HasScheduleReferencesAsync(room.Id, Arg.Any<CancellationToken>()).Returns(_ => scheduled);
        rooms.MoveToLocationAsync(room, target, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                room.LocationId = target.Id;
                room.Location = target;
                room.Name = call.Arg<string>() ?? throw new InvalidOperationException("Expected the final room name.");
                return Task.CompletedTask;
            });
        rooms.Update(room).Returns(_ =>
        {
            room.ConcurrencyStamp = Guid.CreateVersion7();
            return Task.CompletedTask;
        });
        var services = OperationCompositionTests.Services(
            typeof(UpdateLocationRoomCommand), typeof(UpdateLocationRoomCommandHandler));
        services.AddSingleton(locations);
        services.AddSingleton(rooms);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<BaseCommandResponse<Guid>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<Func<CancellationToken, Task<BaseCommandResponse<Guid>>>>()
                ?? throw new InvalidOperationException("Expected a transaction operation."))(call.Arg<CancellationToken>()));
        services.AddSingleton(unitOfWork);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var update = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<UpdateLocationRoomCommand, BaseCommandResponse<Guid>>>();
        var move = new UpdateLocationRoomCommand
        {
            LocationRoomId = room.Id,
            ExpectedConcurrencyStamp = room.ConcurrencyStamp,
            UpdateLocationRoomDto = new()
            {
                Location = new() { LocationId = target.Id },
                Name = new() { Value = "Moved room" }
            }
        };

        var denied = await update.ExecuteAsync(move, default);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(room.LocationId).IsEqualTo(location.Id);
        await Assert.That(room.Name).IsEqualTo("Room");
        await Assert.That(room.ConcurrencyStamp).IsEqualTo(move.ExpectedConcurrencyStamp);
        var renamed = await update.ExecuteAsync(move with
        {
            UpdateLocationRoomDto = new()
            {
                Location = new() { LocationId = location.Id },
                Name = new() { Value = "Renamed room" }
            }
        }, default);
        await Assert.That(renamed.IsSuccess).IsTrue();
        await Assert.That(room.LocationId).IsEqualTo(location.Id);
        await Assert.That(room.Name).IsEqualTo("Renamed room");

        scheduled = false;
        var moved = await update.ExecuteAsync(move with { ExpectedConcurrencyStamp = room.ConcurrencyStamp }, default);
        await Assert.That(moved.IsSuccess).IsTrue();
        await Assert.That(room.LocationId).IsEqualTo(target.Id);
        await Assert.That(room.Name).IsEqualTo("Moved room");
    }
}
