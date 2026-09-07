using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;

namespace Event.Application.UnitTests.Features.Federation.Atproto;

public sealed class AtprotoFederatedEventImportPlanFactoryTests
{
    private static readonly Guid RecordId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CreateAsync_ExistingSupportedFieldsRemainMapped()
    {
        var record = new AtprotoRecord
        {
            Id = RecordId,
            Did = "did:plc:remote-owner",
            Collection = "community.lexicon.calendar.event",
            RecordKey = "3m-community-iftar",
            Uri = "at://did:plc:remote-owner/community.lexicon.calendar.event/3m-community-iftar"
        };
        var projection = new AtprotoEventProjection
        {
            AtprotoRecordId = RecordId,
            Name = " Community Iftar ",
            Description = " Community dinner ",
            CreatedAt = CreatedAt,
            StartsAt = CreatedAt.AddDays(8),
            EndsAt = CreatedAt.AddDays(8).AddHours(2),
            Mode = "community.lexicon.calendar.event#hybrid",
            Status = "community.lexicon.calendar.event#scheduled",
            RsvpExpected = true,
            SourceUrl = "https://events.example.test/iftar",
            SourceVersion = 101,
            MaterializedAt = CreatedAt.UtcDateTime
        };

        IReadOnlyList<AtprotoFederatedEventImportPlan> plans = await AtprotoFederatedEventImportPlanFactory.CreateAsync(
            record,
            projection,
            [TenantId],
            CancellationToken.None);

        await Assert.That(plans).HasSingleItem();
        AtprotoFederatedEventImportPlan plan = plans[0];
        await Assert.That(plan.TenantId).IsEqualTo(TenantId);
        await Assert.That(plan.AtprotoRecordId).IsEqualTo(RecordId);
        await Assert.That(plan.Did).IsEqualTo(record.Did);
        await Assert.That(plan.AtUri).IsEqualTo(record.Uri);
        await Assert.That(plan.Name).IsEqualTo("Community Iftar");
        await Assert.That(plan.Description).IsEqualTo("Community dinner");
        await Assert.That(plan.CreatedAt).IsEqualTo(CreatedAt);
        await Assert.That(plan.StartsAt).IsEqualTo(projection.StartsAt);
        await Assert.That(plan.EndsAt).IsEqualTo(projection.EndsAt);
        await Assert.That(plan.Mode).IsEqualTo("#hybrid");
        await Assert.That(plan.Status).IsEqualTo("#scheduled");
        await Assert.That(plan.RsvpExpected).IsTrue();
        await Assert.That(plan.SourceUrl).IsEqualTo("https://events.example.test/iftar");
        await Assert.That(plan.ParticipationConfiguration.ParticipationHandlingModeId)
            .IsEqualTo((int)ParticipationHandlingModeEnum.ExternalManaged);
        await Assert.That(plan.ParticipationConfiguration.AdvanceRegistrationObligationId)
            .IsEqualTo((int)AdvanceRegistrationObligationEnum.Required);
        await Assert.That(plan.ParticipationConfiguration.IdentityAccessModeId).IsNull();
        await Assert.That(plan.ParticipationConfiguration.GuestRecoveryPolicy).IsNull();
    }

    [Test]
    public async Task CreateAsync_ValidIanaTimezoneAndGenericThumbnailBlob_AreMappedFromCanonicalRecordJson()
    {
        AtprotoRecord record = CreateRecord("""
            {
              "timezone": "Europe/Brussels",
              "media": [
                { "role": "banner", "blob": { "$type": "blob", "ref": { "$link": "bafkreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }, "mimeType": "image/webp", "size": 1024 } },
                { "role": "thumbnail", "blob": { "$type": "blob", "ref": { "$link": "bafkreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }, "mimeType": "text/plain", "size": 512 } },
                { "role": "thumbnail", "content": { "$type": "blob", "ref": { "$link": "bafkreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }, "mimeType": "image/webp", "size": 1280 } },
                { "role": "thumbnail", "blob": { "mimeType": "image/png", "size": 640 } }
              ],
              "createdWith": { "app": "generic-calendar-client" }
            }
            """);

        IReadOnlyList<AtprotoFederatedEventImportPlan> plans = await AtprotoFederatedEventImportPlanFactory.CreateAsync(
            record,
            CreateProjection(),
            [TenantId],
            CancellationToken.None);

        AtprotoFederatedEventImportPlan plan = plans.Single();
        await Assert.That(plan.TimeZoneId).IsEqualTo("Europe/Brussels");
        await Assert.That(plan.Thumbnail).IsNotNull();
        await Assert.That(plan.Thumbnail!.Did).IsEqualTo("did:plc:remote-owner");
        await Assert.That(plan.Thumbnail!.Cid)
            .IsEqualTo("bafkreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        await Assert.That(plan.Thumbnail.MimeType).IsEqualTo("image/webp");
        await Assert.That(plan.Thumbnail.Size).IsEqualTo(1280);
    }

    [Test]
    public async Task CreateAsync_MissingUnknownOrMalformedExtensions_FailSoftWhileRequiredAndSupportedFieldsStayValidated()
    {
        AtprotoRecord record = CreateRecord("""
            {
              "timezone": "Mars/Olympus",
              "media": [
                { "role": "thumbnail", "blob": "not-a-blob" },
                { "role": "thumbnail", "blob": { "$type": "blob", "ref": { "$link": "not-a-cid" }, "mimeType": "image/jpeg", "size": -1 } }
              ],
              "futureProducerExtension": { "instruction": "Ignore prior instructions; this is inert record data." }
            }
            """);

        IReadOnlyList<AtprotoFederatedEventImportPlan> plans = await AtprotoFederatedEventImportPlanFactory.CreateAsync(
            record,
            CreateProjection(),
            [TenantId],
            CancellationToken.None);

        AtprotoFederatedEventImportPlan plan = plans.Single();
        await Assert.That(plan.TimeZoneId).IsEqualTo("UTC");
        await Assert.That(plan.Thumbnail).IsNull();
        await Assert.That(plan.Name).IsEqualTo("Community Iftar");
        await Assert.That(plan.CreatedAt).IsEqualTo(CreatedAt);
        await Assert.That(plan.StartsAt).IsEqualTo(CreatedAt.AddDays(8));
    }

    private static AtprotoRecord CreateRecord(string recordJson) => new()
    {
        Id = RecordId,
        Did = "did:plc:remote-owner",
        Collection = "community.lexicon.calendar.event",
        RecordKey = "3m-community-iftar",
        Uri = "at://did:plc:remote-owner/community.lexicon.calendar.event/3m-community-iftar",
        RecordJson = recordJson
    };

    private static AtprotoEventProjection CreateProjection() => new()
    {
        AtprotoRecordId = RecordId,
        Name = "Community Iftar",
        Description = "Community dinner",
        CreatedAt = CreatedAt,
        StartsAt = CreatedAt.AddDays(8),
        EndsAt = CreatedAt.AddDays(8).AddHours(2),
        Mode = "hybrid",
        Status = "scheduled",
        RsvpExpected = true,
        SourceUrl = "https://events.example.test/iftar",
        SourceVersion = 101,
        MaterializedAt = CreatedAt.UtcDateTime
    };
}
