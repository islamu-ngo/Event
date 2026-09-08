using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Microsoft.Extensions.Logging;
using MudBlazor;
using LocationRoomManagerComponent = Explore.Blazor.Client.Pages.Events.Components.LocationRoomManager;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class LocationRoomManagerTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private static readonly Guid TestLocationId = Guid.NewGuid();

    public LocationRoomManagerTests()
    {
        _ctx = new BlazorTestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private static List<LocationRoomListDto> CreateTestRooms() =>
    [
        new() { Id = Guid.NewGuid(), LocationId = TestLocationId, Name = "Main Hall", Capacity = 200, SortOrder = 1 },
        new() { Id = Guid.NewGuid(), LocationId = TestLocationId, Name = "Room B", Capacity = 0, SortOrder = 2 }
    ];

    private IRenderedComponent<LocationRoomManagerComponent> Render(
        List<LocationRoomListDto>? rooms = null, bool canManage = true, Guid? locationId = null)
    {
        var locId = locationId ?? TestLocationId;
        var roomService = Substitute.For<ILocationRoomService>();
        roomService.GetRoomsByLocationAsync(locId)
            .Returns(Task.FromResult<ICollection<LocationRoomListDto>>(rooms ?? CreateTestRooms()));

        _ctx.Services.AddScoped(_ => roomService);
        _ctx.Services.AddScoped(_ => Substitute.For<IDialogService>());
        _ctx.Services.AddScoped(_ => Substitute.For<ISnackbar>());
        _ctx.Services.AddScoped(_ => Substitute.For<ILogger<LocationRoomManagerComponent>>());

        return _ctx.RenderMudComponent<LocationRoomManagerComponent>(p => p
            .Add(x => x.LocationId, locId)
            .Add(x => x.CanManage, canManage));
    }

    [Test]
    public async Task RendersRoomNames_WhenRoomsExist()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("Main Hall");
        await Assert.That(cut.Markup).Contains("Room B");
    }

    [Test]
    public async Task RendersCapacity_WhenRoomHasCapacity()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("(200)");
    }

    [Test]
    public async Task ShowsSelectLocationMessage_WhenLocationIdEmpty()
    {
        var cut = Render(locationId: Guid.Empty);

        await Assert.That(cut.Markup).Contains("Select a location first");
    }

    [Test]
    public async Task RendersNoRoomsMessage_WhenRoomsEmpty()
    {
        var cut = Render(rooms: []);

        await Assert.That(cut.Markup).Contains("No rooms configured");
    }

    [Test]
    public async Task RendersAddRoomButton_WhenCanManageAndLocationSet()
    {
        var cut = Render(canManage: true);

        await Assert.That(cut.Markup).Contains("Add Room");
    }

    [Test]
    public async Task DisablesAddRoomButton_WhenCanManageFalse()
    {
        var cut = Render(canManage: false);

        var addButtons = cut.FindAll("button:has(.mud-icon-root)");
        var disabledButtons = cut.FindAll("button[disabled]");
        await Assert.That(disabledButtons.Count).IsGreaterThan(0);
    }
}
