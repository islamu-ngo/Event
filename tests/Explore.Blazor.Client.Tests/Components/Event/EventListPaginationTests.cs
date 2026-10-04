using Explore.Blazor.Client.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using EventListPaginationComponent = Explore.Blazor.Client.Pages.Events.Components.EventListPagination;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class EventListPaginationTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Continuation_RendersNativeActionOnlyWhileServerOffersMore()
    {
        var requested = false;
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.LoadedCount, 20)
            .Add(x => x.SnapshotCount, 43)
            .Add(x => x.HasMore, true)
            .Add(x => x.NextRequested, () => requested = true));

        await cut.Find("button").ClickAsync(new MouseEventArgs());
        await Assert.That(requested).IsTrue();
        await Assert.That(cut.FindAll("[role='navigation']").Count).IsEqualTo(0);

        cut.Render(p => p.Add(x => x.HasMore, false));
        await Assert.That(cut.FindAll("button").Count).IsEqualTo(0);
    }

    [Test]
    public async Task Continuation_DisablesNextDuringRequest()
    {
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.HasMore, true)
            .Add(x => x.IsLoading, true));
        await Assert.That(cut.Find("button").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find(".event-list-pagination").GetAttribute("aria-busy")).IsEqualTo("true");
    }

    [Test]
    public async Task Continuation_BatchSizeChangeIsAnExplicitAction()
    {
        var chosenSize = 0;
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.PageSizeChanged, (int size) => chosenSize = size));
        await cut.InvokeAsync(() => cut.FindComponent<MudBlazor.MudSelect<int>>().Instance.ValueChanged.InvokeAsync(50));
        await Assert.That(chosenSize).IsEqualTo(50);
    }

    [Test]
    public async Task Continuation_LanguageChangeUpdatesBatchControlWithoutChangingMembership()
    {
        var translation = Substitute.For<ITranslationService>();
        var language = "en";
        translation.T("ui.discovery.batch_size", Arg.Any<string?>()).Returns(_ => language);
        _ctx.Services.AddSingleton(translation);
        var cut = _ctx.RenderMudComponent<EventListPaginationComponent>(p => p
            .Add(x => x.LoadedCount, 20)
            .Add(x => x.SnapshotCount, 43));
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) =>
        {
            if (cut.FindComponent<MudBlazor.MudSelect<int>>().Instance.Label == "ar")
                rendered.TrySetResult();
        };

        language = "ar";
        await cut.InvokeAsync(() => translation.OnLanguageChanged += Raise.Event<Action<string>>(language));
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.FindComponent<MudBlazor.MudSelect<int>>().Instance.Label).IsEqualTo(language);
        await Assert.That(cut.Instance.LoadedCount).IsEqualTo(20);
        await Assert.That(cut.Instance.SnapshotCount).IsEqualTo(43);
    }
}
