using EventListCustomizationDrawerComponent = Explore.Blazor.Client.Pages.Events.Components.EventListCustomizationDrawer;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class EventListCustomizationDrawerTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public EventListCustomizationDrawerTests()
    {
        _ctx = new BlazorTestContext();
    }

    public void Dispose() => _ctx.Dispose();

    private static ICollection<EffectiveSettingDto> CreateDefaultSettings() =>
    [
        new() { Key = "event_list.browse_mode", Value = "pagination" },
        new() { Key = "event_list.page_size", Value = "20" },
        new() { Key = "event_list.default_layout", Value = "DetailedList" },
        new() { Key = "event_list.card.show_date", Value = "true" },
        new() { Key = "event_list.card.show_location", Value = "true" },
        new() { Key = "event_list.card.show_organizer", Value = "true" },
        new() { Key = "event_list.card.show_description", Value = "true" },
        new() { Key = "event_list.card.show_price", Value = "true" },
        new() { Key = "event_list.card.show_status", Value = "true" },
        new() { Key = "event_list.card.show_tags", Value = "true" },
        new() { Key = "event_list.card.show_categories", Value = "true" },
        new() { Key = "event_list.card.show_capacity", Value = "true" }
    ];

    [Test]
    public async Task Drawer_RendersHeader()
    {
        var cut = _ctx.RenderMudComponent<EventListCustomizationDrawerComponent>(p => p
            .Add(x => x.Settings, CreateDefaultSettings()));

        await Assert.That(cut.Markup).Contains("Customize View");
    }

    [Test]
    public async Task Drawer_RendersCardFieldLabels()
    {
        var cut = _ctx.RenderMudComponent<EventListCustomizationDrawerComponent>(p => p
            .Add(x => x.Settings, CreateDefaultSettings()));

        await Assert.That(cut.Markup).Contains("Date");
        await Assert.That(cut.Markup).Contains("Location");
        await Assert.That(cut.Markup).Contains("Organizer");
        await Assert.That(cut.Markup).Contains("Description");
        await Assert.That(cut.Markup).Contains("Price");
        await Assert.That(cut.Markup).Contains("Status");
    }

    [Test]
    public async Task Drawer_RendersBrowseModeSection()
    {
        var cut = _ctx.RenderMudComponent<EventListCustomizationDrawerComponent>(p => p
            .Add(x => x.Settings, CreateDefaultSettings()));

        await Assert.That(cut.Markup).Contains("Browse Mode");
        await Assert.That(cut.Markup).Contains("Pages");
        await Assert.That(cut.Markup).Contains("Scroll");
    }

    [Test]
    public async Task Drawer_RendersLayoutSection()
    {
        var cut = _ctx.RenderMudComponent<EventListCustomizationDrawerComponent>(p => p
            .Add(x => x.Settings, CreateDefaultSettings()));

        await Assert.That(cut.Markup).Contains("Default Layout");
    }

    [Test]
    public async Task Drawer_ShowsSavingIndicator_WhenIsSaving()
    {
        var cut = _ctx.RenderMudComponent<EventListCustomizationDrawerComponent>(p => p
            .Add(x => x.Settings, CreateDefaultSettings())
            .Add(x => x.IsSaving, true));

        await Assert.That(cut.Markup).Contains("Saving");
    }

    [Test]
    public async Task Drawer_DoesNotShowSavingIndicator_WhenNotSaving()
    {
        var cut = _ctx.RenderMudComponent<EventListCustomizationDrawerComponent>(p => p
            .Add(x => x.Settings, CreateDefaultSettings())
            .Add(x => x.IsSaving, false));

        await Assert.That(cut.Markup).DoesNotContain("Saving");
    }

    [Test]
    public async Task Drawer_RendersResetButton()
    {
        var cut = _ctx.RenderMudComponent<EventListCustomizationDrawerComponent>(p => p
            .Add(x => x.Settings, CreateDefaultSettings()));

        await Assert.That(cut.Markup).Contains("Reset to Defaults");
    }

    [Test]
    public async Task Drawer_RendersWithNullSettings_WithoutError()
    {
        var cut = _ctx.RenderMudComponent<EventListCustomizationDrawerComponent>(p => p
            .Add(x => x.Settings, null));

        // Should render without throwing, showing default state
        await Assert.That(cut.Markup).Contains("Customize View");
        await Assert.That(cut.Markup).Contains("Browse Mode");
    }
}
