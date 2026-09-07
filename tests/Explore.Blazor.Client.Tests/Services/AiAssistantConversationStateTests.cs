using Explore.Blazor.Client.Services.Ai;
using Explore.Blazor.Client.Tests;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AiAssistantConversationStateTests
{
    [Test]
    public async Task CanConfirmAndCanReject_ReadOnlyHalLinks()
    {
        var action = new ProposedActions2();
        GeneratedHalLinkTestHelper.SetLinks(
            action,
            ("confirm-action", "/confirm", "POST"),
            ("reject-action", "/reject", "POST"));

        await Assert.That(AiAssistantConversationState.CanConfirm(action)).IsTrue();
        await Assert.That(AiAssistantConversationState.CanReject(action)).IsTrue();

        action._links!.Remove("confirm-action");

        await Assert.That(AiAssistantConversationState.CanConfirm(action)).IsFalse();
        await Assert.That(AiAssistantConversationState.CanReject(action)).IsTrue();
    }

    [Test]
    public async Task HasEventLink_ReadsReferenceHalLink()
    {
        var reference = HalLinkTestFactory.WithLinks(new HalResourceOfAiReferenceSearchResultDto
        {
            Kind = "Event",
            ReferenceId = Guid.CreateVersion7(),
            DisplayName = "Community Iftar"
        }, new HalLinkTestLink("event", "/api/events/1", "GET"));

        await Assert.That(AiAssistantConversationState.HasEventLink(reference)).IsTrue();

        reference._links.Clear();

        await Assert.That(AiAssistantConversationState.HasEventLink(reference)).IsFalse();
    }

    [Test]
    public async Task StateMutators_UpdateValuesAndNotifySubscribers()
    {
        var state = new AiAssistantConversationState();
        var changes = 0;
        state.OnChange += () => changes++;

        var conversation = new HalResourceOfAiConversationDto { Id = Guid.CreateVersion7(), Title = "Planning" };
        var references = new List<HalResourceOfAiReferenceSearchResultDto>
        {
            new() { Kind = "Event", ReferenceId = Guid.CreateVersion7(), DisplayName = "Event" }
        };

        state.SetLoading(true);
        state.SetError("  retry later  ");
        state.SelectConversation(conversation);
        state.SetReferenceResults(references);
        state.SetSelectedReferences(references);

        await Assert.That(state.IsLoading).IsTrue();
        await Assert.That(state.ErrorMessage).IsEqualTo("retry later");
        await Assert.That(state.SelectedConversation).IsSameReferenceAs(conversation);
        await Assert.That(state.ReferenceResults.Count).IsEqualTo(1);
        await Assert.That(state.SelectedReferences.Count).IsEqualTo(1);
        await Assert.That(changes).IsGreaterThanOrEqualTo(5);
    }
}
