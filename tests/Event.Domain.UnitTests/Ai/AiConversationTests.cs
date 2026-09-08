namespace Event.Domain.UnitTests.Ai;

using Explore.Domain.Ai;

public class AiConversationTests
{
    [Test]
    public async Task AddMessage_AssignsIncreasingLongSequences()
    {
        var conversation = CreateConversation();
        var utcNow = new DateTime(2026, 5, 29, 14, 0, 0, DateTimeKind.Utc);
        var userId = Guid.CreateVersion7();

        var first = conversation.AddMessage(AiMessageRole.User, "Create an event draft", userId, utcNow);
        var second = conversation.AddMessage(AiMessageRole.Assistant, "I can help with that.", null, utcNow.AddSeconds(1));

        await Assert.That(first.Sequence).IsEqualTo(1L);
        await Assert.That(second.Sequence).IsEqualTo(2L);
        await Assert.That(conversation.LastMessageSequence).IsEqualTo(2L);
    }

    [Test]
    public async Task QueueRun_WhenActive_MarksConversationRunningAndStoresProvider()
    {
        var conversation = CreateConversation();
        var utcNow = new DateTime(2026, 5, 29, 14, 1, 0, DateTimeKind.Utc);

        var run = conversation.QueueRun("openai-compatible", "gpt-4.1-mini", utcNow);

        await Assert.That(conversation.Status).IsEqualTo(AiConversationStatus.Running);
        await Assert.That(conversation.Provider).IsEqualTo("openai-compatible");
        await Assert.That(conversation.ModelId).IsEqualTo("gpt-4.1-mini");
        await Assert.That(run.Status).IsEqualTo(AiRunStatus.Queued);
        await Assert.That(run.QueuedAt).IsEqualTo(utcNow);
    }

    [Test]
    public async Task CompleteRun_WhenInProgress_ReturnsConversationToActive()
    {
        var conversation = CreateConversation();
        var queuedAt = new DateTime(2026, 5, 29, 14, 2, 0, DateTimeKind.Utc);
        var completedAt = queuedAt.AddSeconds(5);
        var run = conversation.QueueRun("fake", "deterministic", queuedAt);

        run.Start(queuedAt.AddSeconds(1));
        conversation.CompleteRun(run, completedAt);

        await Assert.That(run.Status).IsEqualTo(AiRunStatus.Succeeded);
        await Assert.That(run.CompletedAt).IsEqualTo(completedAt);
        await Assert.That(conversation.Status).IsEqualTo(AiConversationStatus.Active);
    }

    [Test]
    public async Task FailRun_WhenQueued_BlocksConversationWithFailureCode()
    {
        var conversation = CreateConversation();
        var queuedAt = new DateTime(2026, 5, 29, 14, 3, 0, DateTimeKind.Utc);
        var failedAt = queuedAt.AddSeconds(5);
        var run = conversation.QueueRun("fake", "deterministic", queuedAt);

        conversation.FailRun(run, "provider_timeout", "Provider timed out.", failedAt);

        await Assert.That(run.Status).IsEqualTo(AiRunStatus.Failed);
        await Assert.That(conversation.Status).IsEqualTo(AiConversationStatus.Blocked);
        await Assert.That(conversation.BlockedReason).IsEqualTo("provider_timeout");
    }

    [Test]
    public async Task AddReference_StoresTypedReferenceMetadata()
    {
        var conversation = CreateConversation();
        var eventId = Guid.CreateVersion7();

        var reference = conversation.AddReference(
            AiReferenceKind.Event,
            eventId,
            "Community Iftar",
            "Public Ramadan event",
            conversation.UserId,
            DomainTestClock.UtcNow);

        await Assert.That(reference.Kind).IsEqualTo(AiReferenceKind.Event);
        await Assert.That(reference.ReferenceId).IsEqualTo(eventId);
        await Assert.That(reference.DisplayName).IsEqualTo("Community Iftar");
    }

    [Test]
    public async Task ProposeAction_StoresPayloadWithoutExecutingSideEffects()
    {
        var conversation = CreateConversation();
        var message = conversation.AddMessage(AiMessageRole.Assistant, "I drafted an event.", null, DomainTestClock.UtcNow);

        var action = conversation.ProposeAction(
            AiProposedActionKind.CreateEventDraft,
            "{\"title\":\"Community Iftar\"}",
            message.Id,
            null,
            DomainTestClock.UtcNow);

        await Assert.That(action.Status).IsEqualTo(AiProposedActionStatus.Proposed);
        await Assert.That(action.Kind).IsEqualTo(AiProposedActionKind.CreateEventDraft);
        await Assert.That(action.MessageId).IsEqualTo(message.Id);
    }

    [Test]
    public async Task AddMessage_WhenConversationBlocked_ThrowsInvalidOperationException()
    {
        var conversation = CreateConversation();
        conversation.Block("ai_assistant_disabled", DomainTestClock.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            conversation.AddMessage(AiMessageRole.User, "Hello", conversation.UserId, DomainTestClock.UtcNow);
            return Task.CompletedTask;
        });
    }

    private static AiConversation CreateConversation()
    {
        var userId = Guid.CreateVersion7();

        return new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAt = DomainTestClock.UtcNow,
            CreatedBy = userId
        };
    }
}
