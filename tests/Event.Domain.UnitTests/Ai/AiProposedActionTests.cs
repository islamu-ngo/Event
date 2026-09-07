namespace Event.Domain.UnitTests.Ai;

using Explore.Domain.Ai;

public class AiProposedActionTests
{
    [Test]
    public async Task Confirm_WhenProposed_MarksConfirmedWithUserAndTimestamp()
    {
        var action = CreateAction();
        var userId = Guid.CreateVersion7();
        var utcNow = new DateTime(2026, 5, 29, 15, 0, 0, DateTimeKind.Utc);

        action.Confirm(userId, utcNow);

        await Assert.That(action.Status).IsEqualTo(AiProposedActionStatus.Confirmed);
        await Assert.That(action.ConfirmedBy).IsEqualTo(userId);
        await Assert.That(action.ConfirmedAt).IsEqualTo(utcNow);
    }

    [Test]
    public async Task MarkExecuted_WhenNotConfirmed_ThrowsInvalidOperationException()
    {
        var action = CreateAction();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            action.MarkExecuted(Guid.CreateVersion7());
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task MarkExecuted_WhenConfirmed_StoresResultResourceId()
    {
        var action = CreateAction();
        var userId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();

        action.Confirm(userId, DomainTestClock.UtcNow);
        action.MarkExecuted(eventId);

        await Assert.That(action.Status).IsEqualTo(AiProposedActionStatus.Executed);
        await Assert.That(action.ResultResourceId).IsEqualTo(eventId);
    }

    [Test]
    public async Task Reject_WhenProposed_PreventsLaterConfirmation()
    {
        var action = CreateAction();
        var userId = Guid.CreateVersion7();

        action.Reject(userId, DomainTestClock.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            action.Confirm(userId, DomainTestClock.UtcNow);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task MarkFailed_WhenFailureCodeBlank_ThrowsArgumentException()
    {
        var action = CreateAction();

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            action.MarkFailed(" ", null);
            return Task.CompletedTask;
        });
    }

    private static AiProposedAction CreateAction()
    {
        return new AiProposedAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            ConversationId = Guid.CreateVersion7(),
            Kind = AiProposedActionKind.CreateEventDraft,
            PayloadJson = "{\"title\":\"Community Iftar\"}",
            CreatedAt = DomainTestClock.UtcNow
        };
    }
}
