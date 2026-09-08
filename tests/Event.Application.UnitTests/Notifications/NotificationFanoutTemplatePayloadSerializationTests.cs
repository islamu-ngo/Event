using System.Text.Json;
using Explore.Application.Notifications;

namespace Event.Application.UnitTests.Notifications;

public sealed class NotificationFanoutTemplatePayloadSerializationTests
{
    [Test]
    public async Task Serialize_CanonicalizesCollectionSequencesForStableReplayJson()
    {
        Guid firstId = Guid.Parse("01990000-0000-7000-8000-000000000001");
        Guid secondId = Guid.Parse("01990000-0000-7000-8000-000000000002");
        var first = new NotificationFanoutSessionDisplayTimeV1(
            firstId,
            "First",
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            null);
        var second = first with
        {
            SessionId = secondId,
            SessionTitle = "Second",
            StartsAt = first.StartsAt.AddHours(1)
        };
        var forward = CreateSnapshot([first, second]);
        var reverse = CreateSnapshot([second, first]);
        var forwardChanges = new NotificationFanoutChangeSetV1([
            NotificationFanoutChangeField.StartTime,
            NotificationFanoutChangeField.Cancelled]);
        var reverseChanges = new NotificationFanoutChangeSetV1([
            NotificationFanoutChangeField.Cancelled,
            NotificationFanoutChangeField.StartTime]);

        string snapshotJson = NotificationFanoutTemplateJson.Serialize(reverse);
        string changeJson = NotificationFanoutTemplateJson.Serialize(reverseChanges);
        NotificationFanoutSnapshotV1 replay = JsonSerializer.Deserialize(
            snapshotJson,
            NotificationFanoutTemplateJsonContext.Default.NotificationFanoutSnapshotV1)!;
        NotificationFanoutChangeSetV1 replayChanges = JsonSerializer.Deserialize(
            changeJson,
            NotificationFanoutTemplateJsonContext.Default.NotificationFanoutChangeSetV1)!;

        await Assert.That(snapshotJson).IsEqualTo(NotificationFanoutTemplateJson.Serialize(forward));
        await Assert.That(changeJson).IsEqualTo(NotificationFanoutTemplateJson.Serialize(forwardChanges));
        await Assert.That(replay.SessionDisplayTimes![0].SessionId).IsEqualTo(firstId);
        await Assert.That(replay.SessionDisplayTimes[1].SessionId).IsEqualTo(secondId);
        await Assert.That(replayChanges.Fields[0]).IsEqualTo(NotificationFanoutChangeField.Cancelled);
        await Assert.That(replayChanges.Fields[1]).IsEqualTo(NotificationFanoutChangeField.StartTime);
    }

    [Test]
    public async Task SnapshotSerialization_DoesNotChangeWhenCallerMutatesConstructorArray()
    {
        Guid sessionId = Guid.Parse("01990000-0000-7000-8000-000000000003");
        var original = new NotificationFanoutSessionDisplayTimeV1(
            sessionId,
            "Immutable session",
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
            null);
        NotificationFanoutSessionDisplayTimeV1[] callerOwned = [original];
        NotificationFanoutSnapshotV1 snapshot = CreateSnapshot(callerOwned);
        string beforeMutation = NotificationFanoutTemplateJson.Serialize(snapshot);

        callerOwned[0] = original with { SessionTitle = "MUTATED-CALLER-CANARY" };
        string afterMutation = NotificationFanoutTemplateJson.Serialize(snapshot);

        await Assert.That(afterMutation).IsEqualTo(beforeMutation);
        await Assert.That(afterMutation).DoesNotContain("MUTATED-CALLER-CANARY");
    }

    private static NotificationFanoutSnapshotV1 CreateSnapshot(
        NotificationFanoutSessionDisplayTimeV1[] sessionDisplayTimes) => new(
        "Immutable event",
        SessionTitle: null,
        StartsAt: null,
        EndsAt: null,
        "UTC",
        Location: null,
        sessionDisplayTimes);
}
