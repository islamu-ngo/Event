using Explore.Blazor.Client.Components.Shell.AiAssistant;
using Explore.Blazor.Client.Tests;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class AiProposedActionCardTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Render_WhenHalLinksExist_ShowsConfirmRejectAndSafePreview()
    {
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "CreateEventDraft",
            Status = "Proposed",
            PayloadJson = "{\"title\":\"Community Iftar\",\"description\":\"Plan the meal.\",\"tenantId\":\"not-rendered\"}"
        };
        GeneratedHalLinkTestHelper.SetLinks(
            action,
            ("confirm-action", "/confirm", "POST"),
            ("reject-action", "/reject", "POST"));
        ProposedActions2? confirmed = null;

        var cut = _ctx.RenderMudComponent<AiProposedActionCard>(parameters => parameters
            .Add(component => component.Action, action)
            .Add(component => component.OnConfirm, EventCallback.Factory.Create<ProposedActions2>(this, value => confirmed = value)));

        await Assert.That(cut.FindAll("[data-testid='ai-rail-confirm-action']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='ai-rail-reject-action']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("Community Iftar");
        await Assert.That(cut.Markup).Contains("Plan the meal.");
        await Assert.That(cut.Markup).DoesNotContain("not-rendered");

        await cut.Find("[data-testid='ai-rail-confirm-action']").ClickAsync(new MouseEventArgs());

        await Assert.That(confirmed).IsSameReferenceAs(action);
    }

    [Test]
    public async Task Render_WhenHalLinksAreAbsent_HidesConfirmReject()
    {
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "CreateEventDraft",
            Status = "Executed",
            ResultResourceId = Guid.CreateVersion7()
        };

        var cut = _ctx.RenderMudComponent<AiProposedActionCard>(parameters => parameters
            .Add(component => component.Action, action));

        await Assert.That(cut.FindAll("[data-testid='ai-rail-confirm-action']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='ai-rail-reject-action']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='ai-action-result']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Render_WhenRejected_ShowsRejectedCardWithoutActionButtons()
    {
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            Kind = "CreateEventDraft",
            Status = "Rejected",
            RejectedAt = TestTime.UtcNow,
            PayloadJson = "{\"title\":\"Community Iftar\",\"description\":\"Plan the meal.\"}"
        };

        var cut = _ctx.RenderMudComponent<AiProposedActionCard>(parameters => parameters
            .Add(component => component.Action, action));

        await Assert.That(cut.FindAll("[data-testid='ai-rail-confirm-action']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='ai-rail-reject-action']")).IsEmpty();
        await Assert.That(cut.Markup).Contains("Rejected");
        await Assert.That(cut.Markup).Contains("Event draft proposal rejected");
        await Assert.That(cut.Markup).Contains("No event draft was created.");
    }
}
