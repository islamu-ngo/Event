using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Event.Domain.UnitTests.Entities;

public class EventModerationRecordTests
{
    [Test]
    public async Task EventModerationRecord_ImplementsTenantEntityInterface()
    {
        await Assert.That(typeof(EventModerationRecord).GetInterfaces().Contains(typeof(ITenantEntity))).IsTrue();
    }

    [Test]
    public async Task CreateLightModeration_AllowsUnmoderationAndStoresSafeMetadataOnly()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var moderatorUserId = Guid.NewGuid();
        var createdAt = DomainTestClock.UtcNowOffset;

        var record = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(),
            tenantId,
            eventId,
            moderatorUserId,
            "policy_review",
            (int)EventStatusEnum.Published,
            "correlation-123",
            createdAt);

        await Assert.That(record.TenantId).IsEqualTo(tenantId);
        await Assert.That(record.EventId).IsEqualTo(eventId);
        await Assert.That(record.ModeratorUserId).IsEqualTo(moderatorUserId);
        await Assert.That(record.ActionKind).IsEqualTo(EventModerationActionKind.LightModerated);
        await Assert.That(record.PreviousStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(record.ResultingStatusId).IsEqualTo((int)EventStatusEnum.Moderated);
        await Assert.That(record.IsIrreversible).IsFalse();
        await Assert.That(record.AllowsUnmoderation).IsTrue();
        await Assert.That(record.ReasonCode).IsEqualTo("policy_review");
        await Assert.That(record.CorrelationId).IsEqualTo("correlation-123");
        await Assert.That(record.CreatedAt).IsEqualTo(createdAt);
        await Assert.That(UnsafePayloadProperties()).IsEmpty();
    }

    [Test]
    public async Task CreateHeavyRedaction_IsIrreversibleAndCannotBeUnmoderated()
    {
        var record = EventModerationRecord.CreateHeavyRedaction(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "illegal_content",
            (int)EventStatusEnum.Published,
            null,
            DomainTestClock.UtcNowOffset);

        await Assert.That(record.ActionKind).IsEqualTo(EventModerationActionKind.HeavyRedacted);
        await Assert.That(record.ResultingStatusId).IsEqualTo((int)EventStatusEnum.Moderated);
        await Assert.That(record.IsIrreversible).IsTrue();
        await Assert.That(record.AllowsUnmoderation).IsFalse();
    }

    [Test]
    public async Task CreateUnmoderation_RejectsIrreversibleModerationRecord()
    {
        var heavyRecord = EventModerationRecord.CreateHeavyRedaction(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "illegal_content",
            (int)EventStatusEnum.Published,
            null,
            DomainTestClock.UtcNowOffset);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            _ = EventModerationRecord.CreateUnmoderation(
                Guid.CreateVersion7(),
                heavyRecord,
                Guid.NewGuid(),
                "review_complete",
                "correlation-456",
                DomainTestClock.UtcNowOffset);

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task CreateUnmoderation_FromLightModerationReturnsPublishedStatus()
    {
        var lightRecord = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "policy_review",
            (int)EventStatusEnum.Published,
            null,
            DomainTestClock.UtcNowOffset);

        var unmoderationRecord = EventModerationRecord.CreateUnmoderation(
            Guid.CreateVersion7(),
            lightRecord,
            Guid.NewGuid(),
            "review_complete",
            null,
            DomainTestClock.UtcNowOffset);

        await Assert.That(unmoderationRecord.ActionKind).IsEqualTo(EventModerationActionKind.Unmoderated);
        await Assert.That(unmoderationRecord.PreviousStatusId).IsEqualTo((int)EventStatusEnum.Moderated);
        await Assert.That(unmoderationRecord.ResultingStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(unmoderationRecord.IsIrreversible).IsFalse();
        await Assert.That(unmoderationRecord.AllowsUnmoderation).IsFalse();
    }

    [Test]
    public async Task LinkSourceReportDecision_StoresReportDecisionTraceability()
    {
        var record = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "policy_review",
            (int)EventStatusEnum.Published,
            null,
            DomainTestClock.UtcNowOffset);
        var reportId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();

        record.LinkSourceReportDecision(reportId, decisionId);

        await Assert.That(record.SourceReportId).IsEqualTo(reportId);
        await Assert.That(record.SourceReportDecisionId).IsEqualTo(decisionId);
    }

    [Test]
    public async Task CreateLightModeration_WithSourceDecision_AllowsNullModeratorForProviderEnforcement()
    {
        var record = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            moderatorUserId: null,
            "coop_decision",
            (int)EventStatusEnum.Published,
            "coop-correlation",
            DomainTestClock.UtcNowOffset);
        var reportId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();

        record.LinkSourceReportDecision(reportId, decisionId);

        await Assert.That(record.ModeratorUserId).IsNull();
        await Assert.That(record.SourceReportId).IsEqualTo(reportId);
        await Assert.That(record.SourceReportDecisionId).IsEqualTo(decisionId);
    }

    [Test]
    public async Task LinkSourceReportDecision_RejectsEmptyIds()
    {
        var record = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "policy_review",
            (int)EventStatusEnum.Published,
            null,
            DomainTestClock.UtcNowOffset);

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            record.LinkSourceReportDecision(Guid.Empty, Guid.NewGuid());
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            record.LinkSourceReportDecision(Guid.NewGuid(), Guid.Empty);
            return Task.CompletedTask;
        });
    }

    private static IReadOnlyList<string> UnsafePayloadProperties()
    {
        string[] forbiddenFragments =
        [
            "Title",
            "Subtitle",
            "Description",
            "Content",
            "Slug",
            "Url",
            "Uri",
            "Image",
            "ObjectKey",
            "Payload"
        ];

        return typeof(EventModerationRecord)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
