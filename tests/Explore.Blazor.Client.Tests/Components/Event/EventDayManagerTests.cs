using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Microsoft.Extensions.Logging;
using MudBlazor;
using EventDayManagerComponent = Explore.Blazor.Client.Pages.Events.Components.EventDayManager;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class EventDayManagerTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private static readonly Guid TestEventId = Guid.NewGuid();
    private static readonly DateTimeOffset TestDate1 = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TestDate2 = new(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);

    public EventDayManagerTests()
    {
        _ctx = new BlazorTestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private static List<EventDayListDto> CreateTestDays() =>
    [
        new() { Id = Guid.NewGuid(), EventId = TestEventId, LocalDate = TestDate1, Label = "Day 1", IsPublished = true, SortOrder = 1 },
        new() { Id = Guid.NewGuid(), EventId = TestEventId, LocalDate = TestDate2, Label = "Day 2", IsPublished = false, SortOrder = 2 }
    ];

    private IRenderedComponent<EventDayManagerComponent> Render(
        List<EventDayListDto>? days = null, bool canManage = true)
    {
        var dayService = Substitute.For<IEventDayService>();
        dayService.GetDaysByEventAsync(TestEventId)
            .Returns(Task.FromResult<ICollection<EventDayListDto>>(days ?? CreateTestDays()));

        _ctx.Services.AddScoped(_ => dayService);
        _ctx.Services.AddScoped(_ => Substitute.For<IDialogService>());
        _ctx.Services.AddScoped(_ => Substitute.For<ISnackbar>());
        _ctx.Services.AddScoped(_ => Substitute.For<ILogger<EventDayManagerComponent>>());

        return _ctx.RenderMudComponent<EventDayManagerComponent>(p => p
            .Add(x => x.EventId, TestEventId)
            .Add(x => x.CanManage, canManage));
    }

    [Test]
    public async Task RendersDayLabels_WhenDaysExist()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("Day 1");
        await Assert.That(cut.Markup).Contains("Day 2");
    }

    [Test]
    public async Task RendersEmptyMessage_WhenNoDays()
    {
        var cut = Render(days: []);

        await Assert.That(cut.Markup).Contains("No days configured");
    }

    [Test]
    public async Task RendersDraftChip_WhenDayNotPublished()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("Draft");
    }

    [Test]
    public async Task RendersAddDayButton_WhenCanManageTrue()
    {
        var cut = Render(canManage: true);

        await Assert.That(cut.Markup).Contains("Add Day");
    }

    [Test]
    public async Task HidesEditDeleteButtons_WhenCanManageFalse()
    {
        var cut = Render(canManage: false);

        var editButtons = cut.FindAll("[aria-label*='Edit']");
        var deleteButtons = cut.FindAll("[aria-label*='Delete']");
        await Assert.That(editButtons.Count + deleteButtons.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RendersDateFormatted_WhenDayHasLocalDate()
    {
        var cut = Render();

        await Assert.That(cut.Markup).Contains("Jun 15");
        await Assert.That(cut.Markup).Contains("2026");
    }

    [Test]
    public async Task RendersNoDateSet_WhenLocalDateIsDefault()
    {
        var days = new List<EventDayListDto>
        {
            new() { Id = Guid.NewGuid(), EventId = TestEventId, LocalDate = default, Label = "No Date", IsPublished = true, SortOrder = 1 }
        };
        var cut = Render(days: days);

        await Assert.That(cut.Markup).Contains("No date set");
    }

    [Test]
    public async Task RendersSortOrderFallback_WhenLabelIsNull()
    {
        var days = new List<EventDayListDto>
        {
            new() { Id = Guid.NewGuid(), EventId = TestEventId, LocalDate = TestDate1, Label = null, IsPublished = true, SortOrder = 3 }
        };
        var cut = Render(days: days);

        await Assert.That(cut.Markup).Contains("Day 3");
    }

    [Test]
    public async Task ShowsEditDeleteButtons_WhenCanManageTrue()
    {
        var cut = Render(canManage: true);

        var editButtons = cut.FindAll("[aria-label*='Edit']");
        await Assert.That(editButtons.Count).IsGreaterThan(0);
    }
}
