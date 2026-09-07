using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Event.Domain.UnitTests;

[Category("EventLocationPrivacy")]
public sealed class EventLocationPrivacyAuditTests
{
    [Test]
    public async Task DisclosureAuditCapturesOnlyPolicyDeltaFacts()
    {
        var occurredAt = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
        var revealAt = occurredAt.AddDays(2);
        var audit = EventLocationDisclosureAudit.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            EventLocationDisclosureFields.Country,
            EventLocationDisclosureFields.Country | EventLocationDisclosureFields.City,
            LocationDisclosureAudienceEnum.Never,
            LocationDisclosureAudienceEnum.ConfirmedParticipant,
            null,
            revealAt,
            3,
            4,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            occurredAt);

        await Assert.That(audit.Id.Version).IsEqualTo(7);
        await Assert.That(audit.PreviousFields).IsEqualTo(EventLocationDisclosureFields.Country);
        await Assert.That(audit.NewFields)
            .IsEqualTo(EventLocationDisclosureFields.Country | EventLocationDisclosureFields.City);
        await Assert.That(audit.PreviousAudienceId)
            .IsEqualTo((int)LocationDisclosureAudienceEnum.Never);
        await Assert.That(audit.NewAudienceId)
            .IsEqualTo((int)LocationDisclosureAudienceEnum.ConfirmedParticipant);
        await Assert.That(audit.PreviousRevealFullDetailsFromUtc).IsNull();
        await Assert.That(audit.NewRevealFullDetailsFromUtc).IsEqualTo(revealAt);
        await Assert.That(audit.PreviousPolicyVersion).IsEqualTo(3);
        await Assert.That(audit.NewPolicyVersion).IsEqualTo(4);
        await Assert.That(audit.Reason).IsEqualTo(EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange);
        await Assert.That(audit.OccurredAtUtc).IsEqualTo(occurredAt);
        await Assert.That(() => ((ITenantEntity)audit).TenantId = Guid.CreateVersion7())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DisclosureAuditRejectsNoOpInvalidFlagsAndVersionGaps()
    {
        var tenantId = Guid.CreateVersion7();
        var eventLocationId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var occurredAt = DomainTestClock.UtcNow;

        await Assert.That(() => EventLocationDisclosureAudit.Create(
                tenantId,
                eventLocationId,
                actorId,
                EventLocationDisclosureFields.Country,
                EventLocationDisclosureFields.Country,
                LocationDisclosureAudienceEnum.Never,
                LocationDisclosureAudienceEnum.Never,
                null,
                null,
                1,
                2,
                EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                occurredAt))
            .Throws<ArgumentException>();

        await Assert.That(() => EventLocationDisclosureAudit.Create(
                tenantId,
                eventLocationId,
                actorId,
                EventLocationDisclosureFields.Country,
                (EventLocationDisclosureFields)(1 << 20),
                LocationDisclosureAudienceEnum.Never,
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
                null,
                null,
                1,
                3,
                EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                occurredAt))
            .Throws<ArgumentOutOfRangeException>();

        await Assert.That(() => EventLocationDisclosureAudit.Create(
                tenantId,
                eventLocationId,
                actorId,
                EventLocationDisclosureFields.Country,
                EventLocationDisclosureFields.Country | EventLocationDisclosureFields.City,
                LocationDisclosureAudienceEnum.Never,
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
                null,
                null,
                1,
                3,
                EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                occurredAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ExactReadAuditUsesClosedPurposeAndGuidIdentifiers()
    {
        var audit = EventLocationExactReadAudit.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            EventLocationExactReadPurposeEnum.SupportCaseReview,
            false,
            DomainTestClock.UtcNow,
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        await Assert.That(audit.Id.Version).IsEqualTo(7);
        await Assert.That(audit.WasAuthorized).IsFalse();
        await Assert.That(audit.Purpose).IsEqualTo(EventLocationExactReadPurposeEnum.SupportCaseReview);
    }

    [Test]
    public async Task AuthorityIntentUsesUuidV7AndTypedUserSubject()
    {
        var requestedAt = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
        var intent = CreateIntent(1, requestedAt);

        await Assert.That(intent.IntentId.Version).IsEqualTo(7);
        await Assert.That(intent.AuthoritySequence).IsEqualTo(1);
        await Assert.That(intent.SubjectKind).IsEqualTo(PrivacyErasureSubjectKind.User);
        await Assert.That(intent.PolicyVersion).IsEqualTo(1);

        await Assert.That(() => PrivacyErasureIntent.Record(
                Guid.NewGuid(),
                1,
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1,
                requestedAt,
                requestedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AuthorityIntentRejectsVersion7ShapeOutsideRfc4122Variant()
    {
        var nonRfcVariantId = Guid.Parse("018e4e5c-7f00-7000-0000-000000000001");
        var requestedAt = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(nonRfcVariantId.Version).IsEqualTo(7);
        await Assert.That(nonRfcVariantId.Variant).IsEqualTo(0);
        await Assert.That(() => PrivacyErasureIntent.Record(
                nonRfcVariantId,
                1,
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1,
                requestedAt,
                requestedAt))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ReplayCheckpointRequiresContiguousMonotonicAuthoritySequence()
    {
        var recordedAt = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
        var firstIntent = CreateIntent(1, recordedAt);
        var secondIntent = CreateIntent(2, recordedAt.AddSeconds(1));
        var gapIntent = CreateIntent(4, recordedAt.AddSeconds(2));
        var first = PrivacyErasureReplayCheckpoint.Start(firstIntent, recordedAt.AddMinutes(1));
        var second = PrivacyErasureReplayCheckpoint.Advance(first, secondIntent, recordedAt.AddMinutes(2));

        await Assert.That(first.Id.Version).IsEqualTo(7);
        await Assert.That(first.AuthoritySequence).IsEqualTo(1);
        await Assert.That(second.AuthoritySequence).IsEqualTo(2);
        await Assert.That(second.PreviousCheckpointId).IsEqualTo(first.Id);
        await Assert.That(second.Matches(secondIntent)).IsTrue();

        await Assert.That(() => PrivacyErasureReplayCheckpoint.Advance(
                second,
                secondIntent,
                recordedAt.AddMinutes(3)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => PrivacyErasureReplayCheckpoint.Advance(
                second,
                gapIntent,
                recordedAt.AddMinutes(3)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PrivacyEvidenceTypesExposeNoPublicMutationOrDeleteSurface()
    {
        Type[] evidenceTypes =
        [
            typeof(EventLocationDisclosureAudit),
            typeof(EventLocationExactReadAudit),
            typeof(PrivacyErasureIntent),
            typeof(PrivacyErasureReplayCheckpoint)
        ];
        string[] forbiddenNames =
        [
            "address", "coordinate", "latitude", "longitude", "postcode", "name",
            "accessinstruction", "doorcode", "erasedvalue"
        ];

        foreach (Type evidenceType in evidenceTypes)
        {
            await Assert.That(evidenceType.GetProperties().Any(property => property.SetMethod?.IsPublic == true))
                .IsFalse();
            await Assert.That(evidenceType.GetMethods().Any(method =>
                    method.Name.StartsWith("Update", StringComparison.Ordinal)
                    || method.Name.StartsWith("Delete", StringComparison.Ordinal)))
                .IsFalse();
            await Assert.That(evidenceType.GetProperties().Any(property => forbiddenNames.Any(name =>
                    property.Name.Contains(name, StringComparison.OrdinalIgnoreCase))))
                .IsFalse();
            await Assert.That(evidenceType.GetProperties().Any(property => property.PropertyType == typeof(string)))
                .IsFalse();
        }
    }

    [Test]
    public async Task EncodedPhysicalValuesCannotEnterClosedAuditVocabulariesOrIdentifiers()
    {
        await Assert.That(Enum.TryParse<EventLocationDisclosureAuditReasonEnum>(
                "home_1_Main_Street",
                true,
                out _))
            .IsFalse();
        await Assert.That(Enum.TryParse<EventLocationExactReadPurposeEnum>(
                "door_code_1234",
                true,
                out _))
            .IsFalse();
        await Assert.That(Enum.TryParse<LocationPrivacyErasureReasonEnum>(
                "postcode_1000",
                true,
                out _))
            .IsFalse();
        await Assert.That(Guid.TryParse("home_1_Main_Street", out _)).IsFalse();
        await Assert.That(Guid.TryParse("door_code_1234", out _)).IsFalse();

        await Assert.That(() => EventLocationDisclosureAudit.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                EventLocationDisclosureFields.Country,
                EventLocationDisclosureFields.Country | EventLocationDisclosureFields.City,
                LocationDisclosureAudienceEnum.Never,
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
                null,
                null,
                1,
                2,
                (EventLocationDisclosureAuditReasonEnum)999,
                DomainTestClock.UtcNow))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => EventLocationExactReadAudit.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                (EventLocationExactReadPurposeEnum)999,
                false,
                DomainTestClock.UtcNow,
                Guid.CreateVersion7(),
                null))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PrivacyErasureIntent.Record(
                Guid.CreateVersion7(),
                1,
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                (PrivacyErasureReasonCode)999,
                1,
                DomainTestClock.UtcNow,
                DomainTestClock.UtcNow))
            .Throws<ArgumentOutOfRangeException>();

        Type[] factoryTypes =
        [
            typeof(EventLocationDisclosureAudit),
            typeof(EventLocationExactReadAudit),
            typeof(PrivacyErasureIntent)
        ];
        await Assert.That(factoryTypes.SelectMany(type => type.GetMethods())
                .Where(method => method.Name is "Create" or "Record")
                .SelectMany(method => method.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(string)))
            .IsFalse();
        await Assert.That(typeof(Location).GetMethod(nameof(Location.EraseOwnedPii))!
                .GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(string)))
            .IsFalse();
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "Venue",
            Country = "BE",
            City = "Brussels",
            Tenant = null!
        };
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        await Assert.That(() => location.EraseOwnedPii(
                DomainTestClock.UtcNow,
                (LocationPrivacyErasureReasonEnum)999))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static PrivacyErasureIntent CreateIntent(
        long sequence,
        DateTime requestedAt) =>
        PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            sequence,
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            requestedAt,
            requestedAt.AddMilliseconds(1));
}
