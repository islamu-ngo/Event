using Explore.Blazor.Client.Components.Shell.AiAssistant;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class AiActionResultCardTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Render_WhenCreateEventDraftSucceeds_ShowsRichEventCard()
    {
        var resultId = Guid.CreateVersion7();
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "CreateEventDraft",
            Status = "Executed",
            ResultResourceId = resultId,
            PayloadJson = """{"title":"Weekly Islamic Study Session","description":"Join us for a weekly session to deepen your understanding of the Quran and Sunnah.","eventFormatId":1,"visibilityTypeId":1,"timezone":"Europe/Berlin"}"""
        };

        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Mode, AiPresentationMode.Live)
            .Add(component => component.Presentation, LiveResult(resultId))
            .Add(component => component.OnOpenResult,
                EventCallback.Factory.Create<AiActionResultPresentation>(this, _ => { }))
            .Add(component => component.Action, action));

        await Assert.That(cut.FindAll("[data-testid='ai-result-event-card']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ai-result-event-link']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ai-result-event-details']").Count).IsEqualTo(1);

        await Assert.That(cut.Markup).Contains("Event Draft Created");
        await Assert.That(cut.Markup).Contains("Weekly Islamic Study Session");
        await Assert.That(cut.Markup).Contains("Join us for a weekly session");
        await Assert.That(cut.Markup).Contains("In-Person (Local)");
        await Assert.That(cut.Markup).Contains("Public");
        await Assert.That(cut.Markup).Contains("Europe/Berlin");
    }

    [Test]
    public async Task Render_WhenCreateEventDraftSucceeds_EventLinkPointsToDetailPage()
    {
        var resultId = Guid.CreateVersion7();
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "CreateEventDraft",
            Status = "Executed",
            ResultResourceId = resultId,
            PayloadJson = """{"title":"Community Iftar"}"""
        };

        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        AiActionResultPresentation? opened = null;
        var presentation = LiveResult(resultId);
        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Mode, AiPresentationMode.Live)
            .Add(component => component.Presentation, presentation)
            .Add(component => component.Action, action)
            .Add(component => component.OnOpenResult,
                EventCallback.Factory.Create<AiActionResultPresentation>(this, result =>
                {
                    opened = result;
                    navigation.NavigateTo(result.Destination!);
                })));

        var link = cut.Find("[data-testid='ai-result-event-link']");
        await Assert.That(link.GetAttribute("href")).IsEqualTo($"/events/{resultId}");
        await Assert.That(cut.Markup).Contains("Community Iftar");
        await link.ClickAsync(new MouseEventArgs());
        await Assert.That(opened).IsSameReferenceAs(presentation);
        await Assert.That(navigation.Uri).EndsWith($"/events/{resultId}");
    }

    [Test]
    public async Task Render_WhenCreateEventDraftHasNoPayload_ShowsUntitledFallback()
    {
        var resultId = Guid.CreateVersion7();
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "CreateEventDraft",
            Status = "Executed",
            ResultResourceId = resultId
        };

        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Mode, AiPresentationMode.Live)
            .Add(component => component.Presentation, LiveResult(resultId))
            .Add(component => component.OnOpenResult,
                EventCallback.Factory.Create<AiActionResultPresentation>(this, _ => { }))
            .Add(component => component.Action, action));

        await Assert.That(cut.FindAll("[data-testid='ai-result-event-card']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("Untitled Event");
    }

    [Test]
    public async Task Render_WhenNonCreateEventDraftAction_ShowsRawResultId()
    {
        var resultId = Guid.CreateVersion7();
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "SomeOtherAction",
            Status = "Executed",
            ResultResourceId = resultId
        };

        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Mode, AiPresentationMode.Live)
            .Add(component => component.Action, action));

        await Assert.That(cut.FindAll("[data-testid='ai-result-event-card']")).IsEmpty();
        await Assert.That(cut.Markup).Contains($"Created result {resultId}");
    }

    [Test]
    public async Task Render_WhenActionFails_ShowsFailureMessage()
    {
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "CreateEventDraft",
            Status = "Failed",
            FailureMessage = "Insufficient permissions."
        };

        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Mode, AiPresentationMode.Live)
            .Add(component => component.Action, action));

        await Assert.That(cut.FindAll("[data-testid='ai-result-event-card']")).IsEmpty();
        await Assert.That(cut.Markup).Contains("Insufficient permissions.");
    }

    [Test]
    public async Task Render_WhenOnlineFormat_ShowsOnlineDigital()
    {
        var resultId = Guid.CreateVersion7();
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "CreateEventDraft",
            Status = "Executed",
            ResultResourceId = resultId,
            PayloadJson = """{"title":"Webinar","eventFormatId":2}"""
        };

        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Mode, AiPresentationMode.Live)
            .Add(component => component.Presentation, LiveResult(resultId))
            .Add(component => component.Action, action));

        await Assert.That(cut.Markup).Contains("Online (Digital)");
    }

    [Test]
    [Arguments("/events/019a0000-0000-7000-8000-000000000001")]
    [Arguments("/ai?new=true")]
    [Arguments("//untrusted.invalid/learning")]
    [Arguments("https://untrusted.invalid/learning")]
    [Arguments("javascript:alert(1)")]
    [Arguments("/learning/demo/not-a-private-id")]
    public async Task SimulationResult_UnsafeDestinationCannotEmitHrefOrInvokeHost(string destination)
    {
        var opened = new List<AiActionResultPresentation>();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/learning");
        var originalUri = navigation.Uri;
        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Mode, AiPresentationMode.Simulation)
            .Add(component => component.Presentation,
                new AiActionResultPresentation("Example", "Synthetic result", destination, "Open"))
            .Add(component => component.OnOpenResult,
                EventCallback.Factory.Create<AiActionResultPresentation>(this, opened.Add)));

        await Assert.That(cut.FindAll("[data-testid='ai-action-result']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ai-action-result'] a[href]")).IsEmpty();
        await Assert.That(opened).IsEmpty();
        await Assert.That(navigation.Uri).IsEqualTo(originalUri);
    }

    [Test]
    public async Task UnspecifiedResult_WithExplicitDestinationAndCallbackStillCannotNavigate()
    {
        var opened = new List<AiActionResultPresentation>();
        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Presentation,
                new AiActionResultPresentation("Example", "Synthetic result", "/learning", "Open"))
            .Add(component => component.OnOpenResult,
                EventCallback.Factory.Create<AiActionResultPresentation>(this, opened.Add)));

        await Assert.That(cut.FindAll("[data-testid='ai-action-result']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("a[href]")).IsEmpty();
        await Assert.That(opened).IsEmpty();
    }

    private static AiActionResultPresentation LiveResult(Guid resultId) =>
        new("Event Draft Created", "The draft is ready to open.",
            $"/events/{resultId}", "Open event");
}
