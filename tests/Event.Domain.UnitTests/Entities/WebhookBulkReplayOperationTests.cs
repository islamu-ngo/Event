using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class WebhookBulkReplayOperationTests
{
    private static readonly DateTime QueuedAt =
        new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Operation_FreezesPreviewAndCompletesWithMonotonicVersion()
    {
        var operation = CreateOperation(maxItems: 3);

        await Assert.That(operation.Status).IsEqualTo(WebhookBulkReplayStatus.Queued);
        await Assert.That(operation.ReasonCode).IsEqualTo("operator.incident-recovery");
        await Assert.That(operation.EstimatedEligibleCount).IsEqualTo(5);
        await Assert.That(operation.EstimatedSelectedCount).IsEqualTo(3);
        await Assert.That(operation.EstimatedExcludedCount).IsEqualTo(36);
        await Assert.That(operation.ConcurrencyVersion).IsEqualTo(1);

        operation.Start(QueuedAt.AddSeconds(1));
        operation.Complete(2, QueuedAt.AddSeconds(2));

        await Assert.That(operation.Status).IsEqualTo(WebhookBulkReplayStatus.Completed);
        await Assert.That(operation.ScheduledCount).IsEqualTo(2);
        await Assert.That(operation.ConcurrencyVersion).IsEqualTo(3);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            operation.Cancel("operator.too-late", QueuedAt.AddSeconds(3))));
    }

    [Test]
    public async Task Operation_CancelsOnlyWhileQueuedAndNormalizesReason()
    {
        var operation = CreateOperation();

        operation.Cancel(" Operator.Change_Of_Plan ", QueuedAt.AddSeconds(1));

        await Assert.That(operation.Status).IsEqualTo(WebhookBulkReplayStatus.Cancelled);
        await Assert.That(operation.CancellationReasonCode).IsEqualTo("operator.change_of_plan");
        await Assert.That(operation.StartedAt).IsNull();
        await Assert.That(operation.ConcurrencyVersion).IsEqualTo(2);
    }

    [Test]
    public async Task Create_RejectsInvalidHashAndHardLimitOverflow()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() => CreateOperation(requestHash: "not-a-hash")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.Run(() =>
            CreateOperation(maxItems: WebhookBulkReplayOperation.HardMaximumItems + 1)));
    }

    private static WebhookBulkReplayOperation CreateOperation(
        int maxItems = 100,
        string? requestHash = null) =>
        WebhookBulkReplayOperation.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            requestHash ?? $"sha256:{new string('a', 64)}",
            QueuedAt.AddDays(-7),
            QueuedAt,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "event.published",
            maxItems,
            " Operator.Incident-Recovery ",
            new WebhookBulkReplayPreviewSnapshot(5, 1, 2, 3, 4, 5, 6, 7, 8),
            QueuedAt);
}
