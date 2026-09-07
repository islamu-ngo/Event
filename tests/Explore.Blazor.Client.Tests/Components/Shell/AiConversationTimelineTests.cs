using Explore.Blazor.Client.Components.Shell.AiAssistant;
using Explore.Blazor.Client.Tests;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class AiConversationTimelineTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task Render_WhenActionBelongsToMessage_PlacesActionImmediatelyAfterMessage()
    {
        var assistantMessageId = Guid.CreateVersion7();
        var action = new ProposedActions2
        {
            Id = Guid.CreateVersion7(),
            MessageId = assistantMessageId,
            Kind = "CreateEventDraft",
            Status = "Proposed",
            PayloadJson = "{\"title\":\"Community Iftar\"}"
        };
        GeneratedHalLinkTestHelper.SetLinks(
            action,
            ("confirm-action", "/confirm", "POST"),
            ("reject-action", "/reject", "POST"));
        var conversation = new HalResourceOfAiConversationDto
        {
            Messages =
            [
                new Messages2 { Id = Guid.CreateVersion7(), Role = "User", Sequence = 1, Content = "Plan an event" },
                new Messages2 { Id = assistantMessageId, Role = "Assistant", Sequence = 2, Content = "Review this draft" }
            ],
            ProposedActions = [action]
        };

        var cut = _ctx.RenderMudComponent<AiConversationTimeline>(parameters => parameters
            .Add(component => component.Conversation, conversation));

        var markup = cut.Markup;
        var userIndex = markup.IndexOf("Plan an event", StringComparison.Ordinal);
        var assistantIndex = markup.IndexOf("Review this draft", StringComparison.Ordinal);
        var actionIndex = markup.IndexOf("Community Iftar", StringComparison.Ordinal);

        await Assert.That(userIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(assistantIndex).IsGreaterThan(userIndex);
        await Assert.That(actionIndex).IsGreaterThan(assistantIndex);
    }
}
