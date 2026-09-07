using System.Text.Json;
using Explore.Application.Services;
using Explore.Domain;

namespace Event.Application.UnitTests.Services;

[Category("EventLocationPrivacy")]
public sealed class LocationPrivacyOutboxMessageFactoryTests
{
    private static readonly DateTime Now =
        new(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CreateLocationErased_ContainsOnlyOpaqueIdentityAndVersionFacts()
    {
        Location home = CreateHome();
        PrivacyErasureIntent intent = CreateIntent();

        Guid messageId = Guid.CreateVersion7();
        OutboxMessage message = LocationPrivacyOutboxMessageFactory.CreateLocationErased(
            messageId,
            intent,
            home,
            Now);

        await Assert.That(message.Id).IsEqualTo(messageId);
        await Assert.That(message.EventType)
            .IsEqualTo(LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType);
        await Assert.That(message.Id.Version).IsEqualTo(7);
        await Assert.That(message.Payload).Contains(intent.IntentId.ToString());
        await Assert.That(message.Payload).Contains(home.Id.ToString());
        await Assert.That(ReadInt32(message.Payload!, "SchemaVersion")).IsEqualTo(1);
        await Assert.That(ReadInt64(message.Payload!, "AuthoritySequence")).IsEqualTo(intent.AuthoritySequence);
        await Assert.That(PropertyNames(message.Payload!)).IsEquivalentTo([
            "SchemaVersion", "IntentId", "AuthoritySequence", "LocationId", "LocationVersion"]);
        await Assert.That(message.Payload).DoesNotContain("PRIVATE-HOME-NAME-CANARY");
        await Assert.That(message.Payload).DoesNotContain("ADDRESS-CANARY");
        await Assert.That(message.Payload).DoesNotContain("ROOM-CANARY");
    }

    [Test]
    public async Task CreateCorrectionRequested_ContainsOnlyOpaqueIdsAndPolicyVersion()
    {
        Location home = CreateHome();
        PrivacyErasureIntent intent = CreateIntent();
        EventLocation eventLocation = EventLocation.CreatePhysical(
            home.TenantId,
            Guid.CreateVersion7(),
            home.Id,
            intent.SubjectId,
            Now);

        Guid messageId = Guid.CreateVersion7();
        OutboxMessage message = LocationPrivacyOutboxMessageFactory.CreateCorrectionRequested(
            messageId,
            intent,
            eventLocation,
            Now);

        await Assert.That(message.Id).IsEqualTo(messageId);
        await Assert.That(message.EventType)
            .IsEqualTo(LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType);
        await Assert.That(message.Payload).Contains(eventLocation.Id.ToString());
        await Assert.That(message.Payload).Contains("\"PolicyVersion\":1");
        await Assert.That(ReadInt32(message.Payload!, "SchemaVersion")).IsEqualTo(1);
        await Assert.That(ReadInt64(message.Payload!, "AuthoritySequence")).IsEqualTo(intent.AuthoritySequence);
        await Assert.That(PropertyNames(message.Payload!)).IsEquivalentTo([
            "SchemaVersion", "IntentId", "AuthoritySequence", "TenantId", "EventId",
            "EventLocationId", "LocationId", "PolicyVersion"]);
        await Assert.That(message.Payload).DoesNotContain("PRIVATE-HOME-NAME-CANARY");
        await Assert.That(message.Payload).DoesNotContain("ADDRESS-CANARY");
        await Assert.That(message.Payload).DoesNotContain("ROOM-CANARY");
    }

    [Test]
    public async Task CreateProjectionCorrection_PreservesVersionedOpaqueReplayFacts()
    {
        Location home = CreateHome();
        EventLocation eventLocation = EventLocation.CreatePhysical(
            home.TenantId,
            Guid.CreateVersion7(),
            home.Id,
            Guid.CreateVersion7(),
            Now);
        Guid messageId = Guid.CreateVersion7();

        OutboxMessage message = LocationPrivacyOutboxMessageFactory.CreateProjectionCorrection(
            messageId,
            eventLocation,
            Now);

        await Assert.That(message.Id).IsEqualTo(messageId);
        await Assert.That(message.EventType)
            .IsEqualTo(LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType);
        await Assert.That(ReadInt32(message.Payload!, "SchemaVersion")).IsEqualTo(1);
        await Assert.That(PropertyNames(message.Payload!)).IsEquivalentTo([
            "SchemaVersion", "TenantId", "EventId", "EventLocationId", "PolicyVersion"]);
        await Assert.That(message.Payload).DoesNotContain("PRIVATE-HOME-NAME-CANARY");
        await Assert.That(message.Payload).DoesNotContain("ADDRESS-CANARY");
        await Assert.That(message.Payload).DoesNotContain("ROOM-CANARY");
    }

    private static string[] PropertyNames(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    }

    private static int ReadInt32(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(propertyName).GetInt32();
    }

    private static long ReadInt64(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(propertyName).GetInt64();
    }

    private static PrivacyErasureIntent CreateIntent() =>
        PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            1,
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            Now,
            Now);

    private static Location CreateHome()
    {
        var home = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Tenant = null!,
            FullName = "PRIVATE-HOME-NAME-CANARY",
            Country = "BE",
            City = "Brussels",
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        home.ClassifyAsPrivateHome(Guid.CreateVersion7());
        home.SetManualAddress("ADDRESS-CANARY", "1000");
        home.Rooms =
        [
            new LocationRoom
            {
                Id = Guid.CreateVersion7(),
                TenantId = home.TenantId,
                Tenant = null!,
                LocationId = home.Id,
                Location = home,
                Name = "ROOM-CANARY",
                ConcurrencyStamp = Guid.CreateVersion7(),
            }
        ];
        return home;
    }
}
