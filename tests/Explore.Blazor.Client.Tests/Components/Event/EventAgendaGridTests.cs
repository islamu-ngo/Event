using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Microsoft.Extensions.Logging;
using MudBlazor;
using EventAgendaGridComponent = Explore.Blazor.Client.Pages.Events.Components.EventAgendaGrid;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class EventAgendaGridTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private static readonly Guid TestEventId = Guid.NewGuid();
    private static readonly DateTimeOffset TestDate1 = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TestDate2 = new(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TestTime0900 = new(9, 0, 0);
    private static readonly TimeSpan TestTime1100 = new(11, 0, 0);

    public EventAgendaGridTests()
    {
        _ctx = new BlazorTestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private static List<EventDayListDto> CreateTestDays() =>
    [
        new() { Id = Guid.NewGuid(), EventId = TestEventId, LocalDate = TestDate1, Label = "Day 1", IsPublished = true, SortOrder = 0 },
        new() { Id = Guid.NewGuid(), EventId = TestEventId, LocalDate = TestDate2, Label = "Day 2", IsPublished = true, SortOrder = 1 }
    ];

    private static List<EventAgendaItemListDto> CreateTestItems() =>
    [
        new()
        {
            Id = Guid.NewGuid(), EventId = TestEventId, Title = "Opening Keynote",
            LocalStartDate = TestDate1, LocalStartTime = TestTime0900, LocalEndTime = TestTime1100,
            StartTime = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 6, 15, 11, 0, 0, TimeSpan.Zero),
            KindId = 1, KindFullName = "Keynote", SortOrder = 0
        },
        new()
        {
            Id = Guid.NewGuid(), EventId = TestEventId, Title = "Workshop",
            LocalStartDate = TestDate2, LocalStartTime = TestTime0900, LocalEndTime = TestTime1100,
            StartTime = new DateTimeOffset(2026, 6, 16, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 6, 16, 11, 0, 0, TimeSpan.Zero),
            KindId = 2, KindFullName = "Workshop", SortOrder = 1
        }
    ];

    private static List<LocationRoomListDto> CreateTestRooms() =>
    [
        new() { Id = Guid.NewGuid(), LocationId = Guid.NewGuid(), Name = "Main Hall", Capacity = 200, SortOrder = 0 },
        new() { Id = Guid.NewGuid(), LocationId = Guid.NewGuid(), Name = "Room B", Capacity = 50, SortOrder = 1 }
    ];

    private IRenderedComponent<EventAgendaGridComponent> Render(
        List<EventDayListDto>? days = null, List<EventAgendaItemListDto>? items = null,
        List<LocationRoomListDto>? rooms = null, bool canManage = true, IDialogService? dialogs = null)
    {
        var testDays = days ?? CreateTestDays();
        var testItems = items ?? CreateTestItems();
        var testRooms = rooms ?? [];

        var agendaService = Substitute.For<IEventAgendaItemService>();
        agendaService.GetManagedAgendaItemsByEventAsync(TestEventId)
            .Returns(Task.FromResult<ICollection<EventAgendaItemListDto>>(testItems));

        _ctx.Services.AddScoped(_ => agendaService);
        _ctx.Services.AddScoped(_ => dialogs ?? Substitute.For<IDialogService>());
        _ctx.Services.AddScoped(_ => Substitute.For<ISnackbar>());
        _ctx.Services.AddScoped(_ => Substitute.For<ILogger<EventAgendaGridComponent>>());

        return _ctx.RenderMudComponent<EventAgendaGridComponent>(p => p
            .Add(x => x.EventId, TestEventId)
            .Add(x => x.Days, testDays)
            .Add(x => x.Rooms, testRooms)
            .Add(x => x.CanManage, canManage));
    }

    [Test]
    public async Task GridEditClickWaitsForDialogOpening()
    {
        var dialogs = Substitute.For<IDialogService>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource<IDialogReference>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialogs.ShowAsync<Explore.Blazor.Client.Pages.Events.Components.EventAgendaItemEditorDialog>(
                Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>())
            .Returns(_ =>
            {
                entered.TrySetResult();
                return opened.Task;
            });
        var cut = Render(rooms: CreateTestRooms(), dialogs: dialogs);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Task click = cut.Find(".event-agenda-grid__item").TriggerEventAsync(
            "onclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await entered.Task.WaitAsync(timeout.Token);
        bool completedBeforeOpening = click.IsCompleted;
        var reference = Substitute.For<IDialogReference>();
        reference.Result.Returns(Task.FromResult<DialogResult?>(DialogResult.Cancel()));
        opened.SetResult(reference);
        await click.WaitAsync(timeout.Token);

        await Assert.That(completedBeforeOpening).IsFalse();
    }

    [Test]
    public async Task RendersEmptyMessage_WhenNoItems()
    {
        var cut = Render(items: []);

        await Assert.That(cut.Markup).Contains("No agenda items");
    }

    [Test]
    public async Task RendersAddItemButton_WhenCanManageTrue()
    {
        var cut = Render(canManage: true);

        await Assert.That(cut.Markup).Contains("Add Item");
    }

    [Test]
    public async Task ShowsDayChipSelector_WhenMultipleDays()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("All Days");
    }

    [Test]
    public async Task HidesDayChipSelector_WhenSingleDay()
    {
        var singleDay = new List<EventDayListDto>
        {
            new() { Id = Guid.NewGuid(), EventId = TestEventId, LocalDate = TestDate1, Label = "Day 1", IsPublished = true, SortOrder = 0 }
        };
        var cut = Render(days: singleDay);

        await Assert.That(cut.Markup).DoesNotContain("All Days");
    }

    [Test]
    public async Task RendersGridMode_WhenRoomsExist()
    {
        var cut = Render(rooms: CreateTestRooms());

        await Assert.That(cut.Markup).Contains("event-agenda-grid__container");
    }

    [Test]
    public async Task RendersListMode_WhenNoRooms()
    {
        var cut = Render(rooms: []);

        await Assert.That(cut.Markup).Contains("Opening Keynote");
        await Assert.That(cut.Markup).DoesNotContain("event-agenda-grid__container");
    }

    [Test]
    public async Task RendersRoomHeaders_WhenRoomsExist()
    {
        var cut = Render(rooms: CreateTestRooms());

        await Assert.That(cut.Markup).Contains("Main Hall");
        await Assert.That(cut.Markup).Contains("Room B");
    }

    [Test]
    public async Task RendersItemTitle_InListMode()
    {
        var cut = Render(rooms: []);

        await Assert.That(cut.Markup).Contains("Opening Keynote");
        await Assert.That(cut.Markup).Contains("Workshop");
    }

    [Test]
    public async Task RendersKindName_InListMode()
    {
        var cut = Render(rooms: []);

        await Assert.That(cut.Markup).Contains("Keynote");
    }

    [Test]
    public async Task HidesEditDeleteButtons_WhenCanManageFalse()
    {
        var cut = Render(canManage: false, rooms: []);

        var editButtons = cut.FindAll("[aria-label*='Edit']");
        var deleteButtons = cut.FindAll("[aria-label*='Delete']");
        await Assert.That(editButtons.Count + deleteButtons.Count).IsEqualTo(0);
    }
}
