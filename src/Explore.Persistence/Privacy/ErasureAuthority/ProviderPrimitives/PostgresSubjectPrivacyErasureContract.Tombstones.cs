using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public static partial class PostgresSubjectPrivacyErasureContract
{
    private static string TombstoneSql(SqlMap m)
    {
        string audit = m.Table<EventLocationDisclosureAudit>();
        m.Write<EventLocationDisclosureAudit>("INSERT");
        string[] auditFields = ["Id", "TenantId", "EventLocationId", "ActorUserId", "PreviousFields", "NewFields",
            "PreviousAudienceId", "NewAudienceId", "PreviousRevealFullDetailsFromUtc", "NewRevealFullDetailsFromUtc",
            "PreviousPolicyVersion", "NewPolicyVersion", "Reason", "OccurredAtUtc"];
        string previousFields = string.Join(" + ", new[]
        {
            ("ShowVenueName", EventLocationDisclosureFields.VenueName), ("ShowCity", EventLocationDisclosureFields.City),
            ("ShowCountry", EventLocationDisclosureFields.Country), ("ShowRoomName", EventLocationDisclosureFields.RoomName),
            ("ShowStreetAddress", EventLocationDisclosureFields.StreetAddress), ("ShowPostcode", EventLocationDisclosureFields.Postcode),
            ("ShowCoordinates", EventLocationDisclosureFields.Coordinates)
        }.Select(field => $"CASE WHEN r.{m.Column<EventLocation>(field.Item1)} THEN {(int)field.Item2} ELSE 0 END"));
        return $"""
            INSERT INTO {audit} ({string.Join(", ", auditFields.Select(m.Column<EventLocationDisclosureAudit>))})
            SELECT uuidv7(), r.{m.Column<EventLocation>("TenantId")}, r.{m.Column<EventLocation>("Id")}, p_subject,
                {previousFields}, 0, r.{m.Column<EventLocation>("FullDetailsAudienceId")}, {(int)LocationDisclosureAudienceEnum.Never},
                r.{m.Column<EventLocation>("RevealFullDetailsFromUtc")}, NULL,
                r.{m.Column<EventLocation>("PolicyVersion")}, r.{m.Column<EventLocation>("PolicyVersion")} + 1,
                {m.Literal<EventLocationDisclosureAudit>("Reason", EventLocationDisclosureAuditReasonEnum.PrivacyErasureRemediation)}, p_now
            FROM {m.Table<EventLocation>()} r WHERE r.{m.Column<EventLocation>("Id")} = ANY(v_event_locations);
            {m.Update<EventLocation>($"r.{m.Column<EventLocation>("Id")} = ANY(v_event_locations)",
                ("ShowVenueName", "false"), ("ShowCity", "false"), ("ShowCountry", "false"),
                ("ShowRoomName", "false"), ("ShowStreetAddress", "false"), ("ShowPostcode", "false"), ("ShowCoordinates", "false"),
                ("FullDetailsAudienceId", ((int)LocationDisclosureAudienceEnum.Never).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("RevealFullDetailsFromUtc", "NULL"), ("NeedsPrivacyReview", "true"),
                ("PolicyVersion", $"r.{m.Column<EventLocation>("PolicyVersion")} + 1"),
                ("LastPolicyActorUserId", "p_subject"), ("LastPolicyChangedAtUtc", "p_now"),
                ("UpdatedAt", "p_now"), ("UpdatedBy", "p_subject"), ("ConcurrencyStamp", "uuidv7()"))}
            {m.Delete<LocationPii>($"r.{m.Column<LocationPii>("LocationId")} = ANY(v_homes)")}
            {m.Update<LocationRoom>($"r.{m.Column<LocationRoom>("LocationId")} = ANY(v_homes)",
                ("Name", $"'privacy-erased-' || replace(r.{m.Column<LocationRoom>("Id")}::text, '-', '')"),
                ("Slug", "NULL"), ("Description", "NULL"), ("IsDeleted", "true"),
                ("DeletedAt", "p_now"), ("DeletedBy", "p_subject"), ("UpdatedAt", "p_now"), ("UpdatedBy", "p_subject"),
                ("ConcurrencyStamp", "uuidv7()"))}
            {m.Update<Location>($"r.{m.Column<Location>("Id")} = ANY(v_homes)",
                ("OwnerUserId", "NULL"), ("AddressOrganizationId", "NULL"),
                ("AddressVisibilityId", ((int)LocationAddressVisibilityEnum.Quarantined).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("LocationPrivacyStateId", ((int)LocationPrivacyStateEnum.Erased).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("PiiErasedAtUtc", "p_now"),
                ("PiiErasureReason", $"CASE p_reason WHEN {(int)PrivacyErasureReasonCode.AccountDeletion} THEN {m.Literal<Location>("PiiErasureReason", LocationPrivacyErasureReasonEnum.AccountDeletion)} WHEN {(int)PrivacyErasureReasonCode.SubjectErasureRequest} THEN {m.Literal<Location>("PiiErasureReason", LocationPrivacyErasureReasonEnum.OwnerErasureRequest)} ELSE {m.Literal<Location>("PiiErasureReason", LocationPrivacyErasureReasonEnum.PrivacyIncidentRemediation)} END"),
                ("FullName", m.Literal<Location>("FullName", Location.ErasedPrivateVenueLabel)), ("City", "''"),
                ("DisplaySortKey", m.Literal<Location>("DisplaySortKey", LocationTextNormalization.Normalize(Location.ErasedPrivateVenueLabel))),
                ("DisplaySortKeyVersion", LocationTextNormalization.CurrentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("UpdatedAt", "p_now"), ("UpdatedBy", "p_subject"), ("ConcurrencyStamp", "uuidv7()"))}
            IF EXISTS (SELECT 1 FROM {m.Table<Actor>()} r WHERE r.{m.Column<Actor>("Id")} = ANY(v_actors)
                AND (r.{m.Column<Actor>("OrganizationId")} IS NOT NULL OR r.{m.Column<Actor>("GroupId")} IS NOT NULL
                    OR r.{m.Column<Actor>("ExternalActorSubjectId")} IS NOT NULL OR r.{m.Column<Actor>("ServicePrincipalId")} IS NOT NULL)) THEN
                RAISE EXCEPTION 'subject_erasure_actor_ownership_invalid' USING ERRCODE = '55000';
            END IF;
            {m.Update<AtprotoIdentity>($"r.{m.Column<AtprotoIdentity>("ActorId")} = ANY(v_actors)",
                ("Did", $"'did:deleted:' || replace(r.{m.Column<AtprotoIdentity>("Id")}::text, '-', '')"),
                ("Handle", "NULL"), ("PdsHost", "''"), ("SigningKey", "NULL"), ("IsActive", "false"), ("IsDeleted", "true"),
                ("CreatedBy", "NULL"), ("UpdatedBy", "NULL"), ("DeletedBy", "NULL"), ("DeletedAt", "p_now"),
                ("UpdatedAt", "p_now"), ("ConcurrencyStamp", "uuidv7()"))}
            {m.Update<ActorPii>($"r.{m.Column<ActorPii>("ActorId")} = ANY(v_actors)",
                ("DisplayName", "'Deleted user'"), ("ExternalProfilePictureUri", "NULL"), ("ProfilePictureStorageObjectId", "NULL"))}
            {m.Update<Actor>($"r.{m.Column<Actor>("Id")} = ANY(v_actors)",
                ("UserId", "NULL"), ("CreatedBy", "NULL"), ("IsDeleted", "true"), ("DeletedAt", "p_now"),
                ("DeletedBy", "NULL"), ("UpdatedAt", "p_now"), ("UpdatedBy", "NULL"), ("ConcurrencyStamp", "uuidv7()"))}
            {m.Delete<UserPii>($"r.{m.Column<UserPii>("UserId")} = p_subject")}
            {m.Delete<UserAuthenticationToken>($"r.{m.Column<UserAuthenticationToken>("UserId")} = p_subject")}
            {m.Update<User>($"r.{m.Column<User>("Id")} = p_subject AND NOT r.{m.Column<User>("IsDeleted")}",
                ("IsDeleted", "true"), ("DeletedAt", "p_now"), ("DeletedBy", "p_subject"),
                ("UpdatedAt", "p_now"), ("ConcurrencyStamp", "uuidv7()"))}
            """;
    }
}
