using EventListPaginationComponent = Explore.Blazor.Client.Pages.Events.Components.EventListPagination;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class EventListPaginationTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public EventListPaginationTests()
    {
        _ctx = new BlazorTestContext();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Pagination_RendersPageSummary()
    {
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.CurrentPage, 1)
            .Add(x => x.TotalPages, 5)
            .Add(x => x.PageSize, 20)
            .Add(x => x.TotalCount, 100));

        // "Showing 1–20 of 100 events"
        await Assert.That(cut.Markup).Contains("Showing");
        await Assert.That(cut.Markup).Contains("100");
        await Assert.That(cut.Markup).Contains("events");
    }

    [Test]
    public async Task Pagination_RendersPerPageLabel()
    {
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.CurrentPage, 1)
            .Add(x => x.TotalPages, 5)
            .Add(x => x.PageSize, 20)
            .Add(x => x.TotalCount, 100));

        await Assert.That(cut.Markup).Contains("Per page:");
    }

    [Test]
    public async Task Pagination_RendersMudPaginationComponent()
    {
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.CurrentPage, 1)
            .Add(x => x.TotalPages, 5)
            .Add(x => x.PageSize, 20)
            .Add(x => x.TotalCount, 100));

        await Assert.That(cut.Markup).Contains("mud-pagination");
    }

    [Test]
    public async Task Pagination_ShowsCorrectRangeForPage2()
    {
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.CurrentPage, 2)
            .Add(x => x.TotalPages, 5)
            .Add(x => x.PageSize, 20)
            .Add(x => x.TotalCount, 100));

        // Page 2: items 21–40
        await Assert.That(cut.Markup).Contains("21");
        await Assert.That(cut.Markup).Contains("40");
    }

    [Test]
    public async Task Pagination_HasNavigationRole()
    {
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.CurrentPage, 1)
            .Add(x => x.TotalPages, 5)
            .Add(x => x.PageSize, 20)
            .Add(x => x.TotalCount, 100));

        await Assert.That(cut.Markup).Contains("role=\"navigation\"");
    }

    [Test]
    public async Task Pagination_HidesSummary_WhenTotalCountIsZero()
    {
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.CurrentPage, 1)
            .Add(x => x.TotalPages, 0)
            .Add(x => x.PageSize, 20)
            .Add(x => x.TotalCount, 0));

        await Assert.That(cut.Markup).DoesNotContain("Showing");
    }
}
