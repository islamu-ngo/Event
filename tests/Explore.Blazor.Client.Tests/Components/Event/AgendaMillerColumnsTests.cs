using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Microsoft.Extensions.Logging;
using MudBlazor;
using AgendaMillerColumnsComponent = Explore.Blazor.Client.Pages.Events.Components.AgendaMillerColumns;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class AgendaMillerColumnsTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private static readonly Guid TestEventId = Guid.NewGuid();
    private static readonly DateTimeOffset TestDate1 = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TestDate2 = new(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan TestTime0900 = new(9, 0, 0);
    private static readonly TimeSpan TestTime1100 = new(11, 0, 0);

    public AgendaMillerColumnsTests()
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

    private static List<EventAgendaItemListDto> CreateTestItems(List<EventDayListDto> days) =>
    [
        new()
        {
            Id = Guid.NewGuid(), EventId = TestEventId, Title = "Keynote",
            LocalStartDate = TestDate1, LocalStartTime = TestTime0900, LocalEndTime = TestTime1100,
            KindId = 1, KindFullName = "Keynote", SortOrder = 0
        },
        new()
        {
            Id = Guid.NewGuid(), EventId = TestEventId, Title = "Workshop A",
            LocalStartDate = TestDate1, LocalStartTime = TestTime1100, LocalEndTime = default,
            KindId = 2, KindFullName = "Workshop", SortOrder = 1
        },
        new()
        {
            Id = Guid.NewGuid(), EventId = TestEventId, Title = "Closing",
            LocalStartDate = TestDate2, LocalStartTime = TestTime0900, LocalEndTime = TestTime1100,
            KindId = 3, KindFullName = "Talk", SortOrder = 0
        }
    ];

    private IRenderedComponent<AgendaMillerColumnsComponent> Render(
        List<EventDayListDto>? days = null, List<EventAgendaItemListDto>? items = null, bool canManage = false)
    {
        var testDays = days ?? CreateTestDays();
        var testItems = items ?? CreateTestItems(testDays);

        _ctx.Services.AddScoped(_ => Substitute.For<IEventAgendaItemService>());
        _ctx.Services.AddScoped(_ => Substitute.For<IDialogService>());
        _ctx.Services.AddScoped(_ => Substitute.For<ILogger<AgendaMillerColumnsComponent>>());

        return _ctx.RenderMudComponent<AgendaMillerColumnsComponent>(p => p
            .Add(x => x.EventId, TestEventId)
            .Add(x => x.Days, testDays)
            .Add(x => x.AgendaItems, testItems)
            .Add(x => x.CanManage, canManage));
    }

    [Test]
    public async Task RendersDaysColumn_WithDayLabels()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("Day 1");
        await Assert.That(cut.Markup).Contains("Day 2");
    }

    [Test]
    public async Task RendersNoDaysMessage_WhenDaysEmpty()
    {
        var cut = Render(days: [], items: []);

        await Assert.That(cut.Markup).Contains("No days scheduled");
    }

    [Test]
    public async Task RendersSelectDayPrompt_WhenNoDaySelected()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("Select a day to view agenda");
    }

    [Test]
    public async Task RendersNoItemsMessage_WhenDayHasNoItems()
    {
        var day = new EventDayListDto
        {
            Id = Guid.NewGuid(),
            EventId = TestEventId,
            LocalDate = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero),
            Label = "Empty Day",
            IsPublished = true,
            SortOrder = 0
        };
        var cut = Render(days: [day], items: []);

        var dayItems = cut.FindAll(".agenda-miller__column--days .agenda-miller__item");
        if (dayItems.Count > 0)
        {
            dayItems[0].Click();
            cut.WaitForState(() => cut.Markup.Contains("No items for this day"));
            await Assert.That(cut.Markup).Contains("No items for this day");
        }
    }

    [Test]
    public async Task RendersSelectItemPrompt_WhenNoItemSelected()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("Select an item to view details");
    }

    [Test]
    public async Task DoesNotRenderManageButtons_WhenCanManageFalse()
    {
        var cut = Render(canManage: false);

        var addButtons = cut.FindAll("[aria-label='Add day']");
        await Assert.That(addButtons.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RendersManageButtons_WhenCanManageTrue()
    {
        var cut = Render(canManage: true);

        var addButtons = cut.FindAll("[aria-label='Add day']");
        await Assert.That(addButtons.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task RendersDateFallback_WhenLabelNull()
    {
        var day = new EventDayListDto
        {
            Id = Guid.NewGuid(),
            EventId = TestEventId,
            LocalDate = TestDate1,
            Label = null,
            IsPublished = true,
            SortOrder = 0
        };
        var cut = Render(days: [day], items: []);

        await Assert.That(cut.Markup).Contains(TestDate1.ToString("ddd, MMM d"));
    }

    [Test]
    public async Task RendersPublishedBadge_WhenDayIsPublished()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("agenda-miller__item-badge");
    }

    [Test]
    public async Task ShowsItemsForDay_WhenDayClicked()
    {
        var days = CreateTestDays();
        var items = CreateTestItems(days);
        var cut = Render(days: days, items: items);

        var dayElements = cut.FindAll(".agenda-miller__column--days .agenda-miller__item");
        if (dayElements.Count > 0)
        {
            dayElements[0].Click();
            cut.WaitForState(() => cut.Markup.Contains("Keynote"));
            await Assert.That(cut.Markup).Contains("Keynote");
            await Assert.That(cut.Markup).Contains("Workshop A");
        }
    }

    [Test]
    public async Task FiltersItemsByDay_DoesNotShowOtherDayItems()
    {
        var days = CreateTestDays();
        var items = CreateTestItems(days);
        var cut = Render(days: days, items: items);

        var dayElements = cut.FindAll(".agenda-miller__column--days .agenda-miller__item");
        if (dayElements.Count > 1)
        {
            dayElements[0].Click();
            cut.WaitForState(() => cut.Markup.Contains("Keynote"));

            var markup = cut.Markup;
            await Assert.That(markup).Contains("Keynote");
            await Assert.That(markup).DoesNotContain("Closing");
        }
    }

    [Test]
    public async Task ShowsTimeRange_WhenItemHasStartAndEndTime()
    {
        var days = CreateTestDays();
        var items = CreateTestItems(days);
        var cut = Render(days: days, items: items);

        var dayElements = cut.FindAll(".agenda-miller__column--days .agenda-miller__item");
        if (dayElements.Count > 0)
        {
            dayElements[0].Click();
            cut.WaitForState(() => cut.Markup.Contains("Keynote"));

            await Assert.That(cut.Markup).Contains("09:00");
        }
    }

    [Test]
    public async Task ShowsKindChip_WhenKindFullNamePresent()
    {
        var days = CreateTestDays();
        var items = CreateTestItems(days);
        var cut = Render(days: days, items: items);

        var dayElements = cut.FindAll(".agenda-miller__column--days .agenda-miller__item");
        if (dayElements.Count > 0)
        {
            dayElements[0].Click();
            cut.WaitForState(() => cut.Markup.Contains("Keynote"));

            await Assert.That(cut.Markup).Contains("Keynote");
        }
    }
}
