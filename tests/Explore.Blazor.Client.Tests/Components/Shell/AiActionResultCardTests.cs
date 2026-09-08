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

        var cut = _ctx.RenderMudComponent<AiActionResultCard>(parameters => parameters
            .Add(component => component.Action, action));

        var link = cut.Find("[data-testid='ai-result-event-link']");
        await Assert.That(link.GetAttribute("href")).IsEqualTo($"/events/{resultId}");
        await Assert.That(cut.Markup).Contains("Community Iftar");
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
            .Add(component => component.Action, action));

        await Assert.That(cut.Markup).Contains("Online (Digital)");
    }
}
