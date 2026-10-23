using Explore.Domain.Ai;

namespace Event.Domain.UnitTests.Ai;

public sealed class AiToolExecutionCustodyTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ReservationCapturesConfirmedSourceIdentity()
    {
        var action = Action();
        var execution = Reserve(action);
        await Assert.That(execution.ConversationId).IsEqualTo(action.ConversationId);
        await Assert.That(execution.PrincipalUserId).IsEqualTo(action.ConfirmedBy!.Value);
        await Assert.That(execution.ActingActorId).IsEqualTo(action.ActingActorId!.Value);
        await Assert.That(execution.ProposedActionId).IsEqualTo(action.Id);
        await Assert.That(execution.ActionMessageId).IsEqualTo(action.MessageId);
        await Assert.That(execution.ActionCreatedAt).IsEqualTo(action.CreatedAt);
        await Assert.That(execution.ActionConfirmedAt).IsEqualTo(action.ConfirmedAt!.Value);
        await Assert.That(execution.PayloadDigest).IsEqualTo(AiToolExecution.DigestPayload(action.PayloadJson));
        await Assert.That(execution.HasDurablePrimaryReservation).IsTrue();
        await Assert.That(execution.PrimaryEventId).IsNull();
    }

    [Test]
    [Arguments("execution")]
    [Arguments("event")]
    [Arguments("principal")]
    [Arguments("action")]
    [Arguments("tenant")]
    [Arguments("conversation")]
    [Arguments("kind")]
    [Arguments("status")]
    [Arguments("confirmer")]
    [Arguments("confirmed-at")]
    [Arguments("actor")]
    [Arguments("image")]
    public async Task ReservationRejectsSubstitutedOrIncompleteEvidence(string invalid)
    {
        var action = Action();
        Guid executionId = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        Guid principal = action.ConfirmedBy!.Value, actor = action.ActingActorId!.Value;
        Guid? image = null;
        switch (invalid)
        {
            case "execution": executionId = Guid.Empty; break;
            case "event": eventId = Guid.Empty; break;
            case "principal": principal = Guid.Empty; break;
            case "action": action.Id = Guid.Empty; break;
            case "tenant": action.TenantId = Guid.Empty; break;
            case "conversation": action.ConversationId = Guid.Empty; break;
            case "kind": action.Kind = (AiProposedActionKind)(-1); break;
            case "status": action.Status = AiProposedActionStatus.Proposed; break;
            case "confirmer": action.ConfirmedBy = Guid.CreateVersion7(); break;
            case "confirmed-at": action.ConfirmedAt = null; break;
            case "actor": actor = Guid.CreateVersion7(); break;
            case "image": image = Guid.CreateVersion7(); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(
            AiToolExecution.ReserveEventDraft(executionId, eventId, action, principal, actor, image, null)));
    }

    [Test]
    [Arguments(0, true)]
    [Arguments(30, true)]
    [Arguments(31, false)]
    [Arguments(-1, false)]
    public async Task NewPrimaryEffectRequiresOriginalActionLifetime(int minutes, bool allowed)
    {
        var execution = Reserve(Action());
        Guid target = execution.ReservedEventId!.Value;
        if (allowed)
        {
            execution.LinkPrimaryEvent(target, Now.AddMinutes(minutes));
            await Assert.That(execution.PrimaryEventId).IsEqualTo(target);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                execution.LinkPrimaryEvent(target, Now.AddMinutes(minutes));
                return Task.CompletedTask;
            });
            await Assert.That(execution.PrimaryEventId).IsNull();
        }
    }

    [Test]
    public async Task LinkedPrimaryEffectCannotSwitchAndCanRecoverAfterExpiry()
    {
        var execution = Reserve(Action());
        execution.LinkPrimaryEvent(execution.ReservedEventId!.Value, Now);
        Guid stamp = execution.ConcurrencyStamp;
        execution.LinkPrimaryEvent(execution.ReservedEventId.Value, Now.AddHours(1));
        await Assert.That(execution.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.LinkPrimaryEvent(Guid.CreateVersion7(), Now);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ImageCustodyRequiresPrimaryAndCannotReplaceUploadOrObject()
    {
        var action = Action();
        Guid message = Guid.CreateVersion7(), upload = Guid.CreateVersion7(), storage = Guid.CreateVersion7();
        var execution = AiToolExecution.ReserveEventDraft(Guid.CreateVersion7(), Guid.CreateVersion7(),
            action, action.ConfirmedBy!.Value, action.ActingActorId!.Value, message, "source-digest");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.BindImageUploadSession(message, "source-digest", upload);
            return Task.CompletedTask;
        });
        execution.LinkPrimaryEvent(execution.ReservedEventId!.Value, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.BindImageUploadSession(message, "substitute", upload);
            return Task.CompletedTask;
        });
        execution.BindImageUploadSession(message, "source-digest", upload);
        execution.BindImageStorageObject(upload, storage);
        Guid stamp = execution.ConcurrencyStamp;
        execution.BindImageUploadSession(message, "source-digest", upload);
        execution.BindImageStorageObject(upload, storage);
        await Assert.That(execution.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.BindImageUploadSession(message, "source-digest", Guid.CreateVersion7());
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.BindImageStorageObject(upload, Guid.CreateVersion7());
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SuccessRequiresPrimaryAndCannotBecomeFailure()
    {
        var execution = Reserve(Action());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.MarkSucceeded(Now);
            return Task.CompletedTask;
        });
        execution.LinkPrimaryEvent(execution.ReservedEventId!.Value, Now);
        execution.MarkSucceeded(Now);
        Guid stamp = execution.ConcurrencyStamp;
        execution.MarkSucceeded(Now.AddHours(1));
        await Assert.That(execution.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.MarkFailed("failed", null, Now);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task FailedReservationCannotLinkOrReopen()
    {
        var execution = Reserve(Action());
        execution.MarkFailed("failed", null, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.LinkPrimaryEvent(execution.ReservedEventId!.Value, Now);
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            execution.MarkSucceeded(Now);
            return Task.CompletedTask;
        });
    }

    [Test]
    [Arguments("principal")]
    [Arguments("tenant")]
    [Arguments("conversation")]
    [Arguments("actor")]
    [Arguments("message")]
    [Arguments("created")]
    [Arguments("payload")]
    public async Task PreviewConsentChangesWhenItsSourceOrOwnerChanges(string changed)
    {
        var action = Action();
        Guid principal = action.ConfirmedBy!.Value;
        string original = action.ConsentVersionFor(principal);
        switch (changed)
        {
            case "principal": principal = Guid.CreateVersion7(); break;
            case "tenant": action.TenantId = Guid.CreateVersion7(); break;
            case "conversation": action.ConversationId = Guid.CreateVersion7(); break;
            case "actor": action.ActingActorId = Guid.CreateVersion7(); break;
            case "message": action.MessageId = Guid.CreateVersion7(); break;
            case "created": action.CreatedAt = action.CreatedAt.AddTicks(1); break;
            case "payload": action.PayloadJson = "{\"changed\":true}"; break;
        }
        await Assert.That(action.ConsentVersionFor(principal) == original).IsFalse();
    }

    [Test]
    public async Task PreviewConsentSurvivesStatusChangesForRecovery()
    {
        var action = Action();
        string original = action.ConsentVersionFor(action.ConfirmedBy!.Value);
        action.Status = AiProposedActionStatus.Executed;
        await Assert.That(action.ConsentVersionFor(action.ConfirmedBy.Value)).IsEqualTo(original);
    }

    private static AiProposedAction Action() => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        ConversationId = Guid.CreateVersion7(),
        ActingActorId = Guid.CreateVersion7(),
        MessageId = Guid.CreateVersion7(),
        Kind = AiProposedActionKind.CreateEventDraft,
        Status = AiProposedActionStatus.Confirmed,
        PayloadJson = "{}",
        ConfirmedBy = Guid.CreateVersion7(),
        ConfirmedAt = Now,
        CreatedAt = Now
    };

    private static AiToolExecution Reserve(AiProposedAction action) =>
        AiToolExecution.ReserveEventDraft(Guid.CreateVersion7(), Guid.CreateVersion7(), action,
            action.ConfirmedBy!.Value, action.ActingActorId!.Value, null, null);
}
