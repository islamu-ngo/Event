using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Mappings;
using Explore.Domain;

namespace Event.Application.UnitTests.Profiles;

public sealed class NotificationMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid NotificationId = Guid.Parse("01910000-0000-7000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("01910000-0000-7000-8000-000000000002");
    private static readonly Guid TenantId = Guid.Parse("01910000-0000-7000-8000-000000000003");
    private static readonly Guid SourceId = Guid.Parse("01910000-0000-7000-8000-000000000004");
    private static readonly Guid RecipientId = Guid.Parse("01910000-0000-7000-8000-000000000005");

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Projection_PreservesOnlyThePublishedScalarContract(bool detail)
    {
        await AssertContract(CreateNotification(), Expected(detail), detail);
    }

    [Test]
    [Arguments(true, "type")]
    [Arguments(false, "type")]
    [Arguments(true, "entityType")]
    [Arguments(false, "entityType")]
    [Arguments(true, "scope")]
    [Arguments(false, "scope")]
    [Arguments(true, "reason")]
    [Arguments(false, "reason")]
    [Arguments(true, "source")]
    [Arguments(false, "source")]
    [Arguments(true, "sourcePii")]
    [Arguments(false, "sourcePii")]
    [Arguments(true, "recipient")]
    [Arguments(false, "recipient")]
    [Arguments(true, "recipientPii")]
    [Arguments(false, "recipientPii")]
    public async Task MissingNavigation_ProducesNullDisplayMetadata(bool detail, string missing)
    {
        var source = CreateNotification();
        var expected = Expected(detail);
        string field;
        switch (missing)
        {
            case "type":
                source.NotificationType = null!;
                field = "notificationTypeName";
                break;
            case "entityType":
                source.NotificationEntityType = null;
                field = "notificationEntityTypeName";
                break;
            case "scope":
                source.NotificationScope = null!;
                field = "notificationScopeName";
                break;
            case "reason":
                source.NotificationReason = null;
                field = "notificationReasonName";
                break;
            case "source":
                source.SourceActor = null;
                field = "sourceActorName";
                break;
            case "sourcePii":
                source.SourceActor!.Pii = null!;
                field = "sourceActorName";
                break;
            case "recipient":
                source.RecipientContextActor = null;
                field = "recipientContextActorName";
                break;
            default:
                source.RecipientContextActor!.Pii = null!;
                field = "recipientContextActorName";
                break;
        }

        expected[field] = null;
        await AssertContract(source, expected, detail);
    }

    [Test]
    [Arguments(true, null)]
    [Arguments(false, null)]
    [Arguments(true, "")]
    [Arguments(false, "")]
    public async Task NullableMetadata_PreservesNullAndEmptyWithoutInventingLifecycleDates(bool detail, string? value)
    {
        var source = CreateNotification();
        source.NotificationType.FullName = value!;
        source.NotificationEntityType!.FullName = value!;
        source.NotificationScope.FullName = value!;
        source.NotificationReason!.FullName = value!;
        source.SourceActor!.Pii!.DisplayName = value!;
        source.RecipientContextActor!.Pii!.DisplayName = value!;
        source.Body = value;
        source.EntityId = value;
        source.ReadAt = null;
        source.ArchivedAt = null;
        source.SnoozedUntil = null;
        var expected = Expected(detail);
        foreach (var field in new[] { "notificationTypeName", "notificationEntityTypeName", "notificationScopeName", "notificationReasonName", "sourceActorName", "recipientContextActorName", "body", "entityId" })
        {
            expected[field] = JsonValue.Create(value);
        }

        expected["readAt"] = null;
        expected["archivedAt"] = null;
        expected["snoozedUntil"] = null;
        await AssertContract(source, expected, detail);
    }

    private static async Task AssertContract(Notification source, JsonObject expected, bool detail)
    {
        var actual = detail
            ? JsonSerializer.SerializeToNode(NotificationMapper.ToDetail(source), JsonOptions)
            : JsonSerializer.SerializeToNode(NotificationMapper.ToListItem(source), JsonOptions);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue()
            .Because($"Expected {expected}; actual {actual}");
    }

    private static JsonObject Expected(bool detail)
    {
        var expected = JsonNode.Parse("""
            {
              "id": "01910000-0000-7000-8000-000000000001",
              "notificationTypeId": 11,
              "notificationTypeName": "Event update",
              "title": "Published title",
              "body": "Published body",
              "isRead": true,
              "readAt": "2026-09-02T10:00:00Z",
              "notificationEntityTypeId": 12,
              "notificationEntityTypeName": "Event",
              "entityId": "public-event",
              "notificationScopeId": 13,
              "notificationScopeName": "Group",
              "sourceActorId": "01910000-0000-7000-8000-000000000004",
              "sourceActorName": "Source display",
              "recipientContextActorId": "01910000-0000-7000-8000-000000000005",
              "recipientContextActorName": "Recipient display",
              "notificationReasonId": 14,
              "notificationReasonName": "Following",
              "isArchived": true,
              "archivedAt": "2026-09-03T10:00:00Z",
              "snoozedUntil": "2026-09-04T10:00:00Z",
              "createdAt": "2026-09-01T10:00:00Z"
            }
            """)!.AsObject();
        if (detail)
        {
            expected["userId"] = UserId.ToString();
            expected["tenantId"] = TenantId.ToString();
        }

        return expected;
    }

    private static Notification CreateNotification()
    {
        var user = new User { Id = UserId, Pii = new UserPii { Email = "private@example.test", FirstName = "Private", LastName = "Recipient" } };
        var sourceActor = new Actor
        {
            Id = SourceId,
            ActorType = null!,
            User = user,
            Pii = new ActorPii { DisplayName = "Source display", ProfilePictureUri = "https://private.example.test/source" }
        };
        var recipientActor = new Actor
        {
            Id = RecipientId,
            ActorType = null!,
            User = user,
            Pii = new ActorPii { DisplayName = "Recipient display", ProfilePictureUri = "https://private.example.test/recipient" }
        };
        sourceActor.Pii.Actor = sourceActor;
        recipientActor.Pii.Actor = recipientActor;
        user.Actor = sourceActor;
        return new Notification
        {
            Id = NotificationId,
            UserId = UserId,
            User = user,
            TenantId = TenantId,
            Tenant = null!,
            NotificationIntentId = SourceId,
            DeduplicationKey = "private-deduplication-key",
            NotificationTypeId = 11,
            NotificationType = new NotificationType { Id = 11, FullName = "Event update", MasterCode = "PRIVATE_TYPE" },
            Title = "Published title",
            Body = "Published body",
            IsRead = true,
            ReadAt = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc),
            NotificationEntityTypeId = 12,
            NotificationEntityType = new NotificationEntityType { Id = 12, FullName = "Event", MasterCode = "PRIVATE_ENTITY" },
            EntityId = "public-event",
            NotificationScopeId = 13,
            NotificationScope = new NotificationScopeType { Id = 13, FullName = "Group", MasterCode = "PRIVATE_SCOPE" },
            SourceActorId = SourceId,
            SourceActor = sourceActor,
            RecipientContextActorId = RecipientId,
            RecipientContextActor = recipientActor,
            NotificationReasonId = 14,
            NotificationReason = new NotificationReason { Id = 14, FullName = "Following", MasterCode = "PRIVATE_REASON" },
            IsArchived = true,
            ArchivedAt = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc),
            SnoozedUntil = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            CreatedBy = SourceId,
            UpdatedAt = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc),
            UpdatedBy = RecipientId,
            IsDeleted = true,
            DeletedAt = new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc),
            DeletedBy = RecipientId
        };
    }
}
