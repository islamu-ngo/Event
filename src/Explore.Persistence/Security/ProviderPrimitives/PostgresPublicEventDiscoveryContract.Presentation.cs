using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventDiscoveryContract
{
    private static string LocalProjection(SourceSql sql)
    {
        string E(string property) => sql.Column<Event>("e", property);
        string lookup = string.Join(",", new[]
        {
            Lookup<EventType>(sql, "EventType", E("EventTypeId"), "Id", "FullName"),
            Lookup<EventProvenanceType>(sql, "EventProvenanceType", E("EventProvenanceTypeId"), "Id", "MasterCode"),
            Lookup<AudienceGender>(sql, "AudienceGender", E("AudienceGenderId"), "Id", "FullName"),
            Lookup<AudienceAge>(sql, "AudienceAge", E("AudienceAgeId"), "Id", "FullName", "MinAge", "MaxAge"),
            Lookup<EventStatus>(sql, "EventStatus", E("EventStatusId"), "Id", "FullName"),
            Lookup<VisibilityType>(sql, "VisibilityType", E("VisibilityTypeId"), "Id", "FullName"),
            Lookup<EventFormat>(sql, "EventFormat", E("EventFormatId"), "Id", "FullName"),
            Lookup<Madhab>(sql, "Madhab", E("MadhabId"), "Id", "FullName"),
            Lookup<EventRegistrationPolicy>(sql, "RegistrationPolicy", E("RegistrationPolicyId"), "Id", "FullName")
        });
        string participation = $"""
            (SELECT {sql.Projection<EventParticipationConfiguration>("pc", "Id", "TenantId", "ConcurrencyStamp",
                "ParticipationHandlingModeId", "AdvanceRegistrationObligationId", "IdentityAccessModeId", "GuestRecoveryPolicy")}
                || pg_catalog.jsonb_build_object(
                    {Lookup<ParticipationHandlingMode>(sql, "ParticipationHandlingMode", sql.Column<EventParticipationConfiguration>("pc", "ParticipationHandlingModeId"), "Id", "MasterCode", "FullName")},
                    {Lookup<AdvanceRegistrationObligation>(sql, "AdvanceRegistrationObligation", sql.Column<EventParticipationConfiguration>("pc", "AdvanceRegistrationObligationId"), "Id", "MasterCode", "FullName")},
                    {Lookup<IdentityAccessMode>(sql, "IdentityAccessMode", sql.Column<EventParticipationConfiguration>("pc", "IdentityAccessModeId"), "Id", "MasterCode", "FullName")})
            FROM {sql.Table<EventParticipationConfiguration>()} pc
            WHERE {sql.Live<EventParticipationConfiguration>("pc")} AND {sql.Column<EventParticipationConfiguration>("pc", "Id")} = {E("Id")})
            """;
        string location = $"""
            (SELECT {sql.Projection<EventLocation>("el", "Id", "TenantId", "EventId", "CreatedAt", "RevealFullDetailsFromUtc")}
                FROM {sql.Table<EventLocation>()} el WHERE {sql.Live<EventLocation>("el")}
                    AND {sql.Column<EventLocation>("el", "Id")} = {sql.Column<EventSession>("s", "EventLocationId")})
            """;
        return $"""
            {sql.Projection<Event>("e", "Id", "TenantId", "Title", "DiscoveryTitleSortKey", "DiscoverySourceSortKey", "Subtitle", "Description", "Slug", "PublicCode",
                "EventTypeId", "AudienceGenderId", "AudienceAgeId", "ActorId", "EventProvenanceTypeId", "FeaturedImageId",
                "TotalViews", "MadhabId", "VisibilityTypeId", "EventStatusId", "FirstSessionDate", "LastSessionDate",
                "FirstSessionStartUtc", "LastSessionEndUtc", "Timezone", "EventFormatId", "RegistrationPolicyId",
                "AtprotoRecordId", "CreatedAt")}
            || pg_catalog.jsonb_build_object({lookup}, 'ParticipationConfiguration', {participation},
                'Session', {sql.Projection<EventSession>("s", "Id", "TenantId", "EventId", "EventSessionStatusId", "Title", "StartTime", "EndTime",
                    "EndTimeType", "LocalStartDate", "LocalEndDate", "LocalStartTime", "LocalEndTime", "EventLocationId", "RoomId")}
                    || pg_catalog.jsonb_build_object('EventLocation',{location}),
                'MatchingCount', chosen.matches, 'Presentation', {PresentationSql(sql)})
            """;
    }

    private static string Lookup<T>(SourceSql sql, string name, string id, params string[] fields) => $"""
        {Text(name)}, (SELECT {sql.Projection<T>("label", fields)} FROM {sql.Table<T>()} label
            WHERE {sql.Live<T>("label")} AND {sql.Column<T>("label", "Id")} = {id})
        """;

    private static string PresentationSql(SourceSql sql)
    {
        string A(string property) => sql.Column<Actor>("pa", property);
        string P(string property) => sql.Column<ActorPii>("pii", property);
        string actorPicture = $"""
            CASE WHEN {P("ProfilePictureStorageObjectId")} IS NOT NULL AND {P("ExternalProfilePictureUri")} IS NULL THEN
                {ImageUri(sql, P("ProfilePictureStorageObjectId"), profile: true)}
            ELSE NULL END
            """;
        return $"""
            (SELECT pg_catalog.jsonb_build_object(
                'FeaturedImageUri',{ImageUri(sql, sql.Column<Event>("e", "FeaturedImageId"), profile: false)},
                'ActorDisplayName',{P("DisplayName")}, 'ActorProfilePictureUri',{actorPicture},
                'ActorExternalProfilePictureUri',CASE WHEN {P("ProfilePictureStorageObjectId")} IS NULL
                    THEN {P("ExternalProfilePictureUri")} ELSE NULL END,
                'ActorTypeId',{A("ActorTypeId")},
                'ActorTypeFullName',(SELECT {sql.Column<ActorType>("at", "FullName")} FROM {sql.Table<ActorType>()} at
                    WHERE {sql.Column<ActorType>("at", "Id")} = {A("ActorTypeId")}),
                'ActorUserId',{A("UserId")}, 'ActorOrganizationId',{A("OrganizationId")}, 'ActorGroupId',{A("GroupId")},
                'HasValidOptionalQuestionnaire',{OptionalQuestionnaire(sql)},
                'TicketSummary',{TicketSummary(sql)})
            FROM {sql.Table<Actor>()} pa LEFT JOIN {sql.Table<ActorPii>()} pii ON {P("ActorId")} = {A("Id")}
            WHERE {sql.Live<Actor>("pa")} AND {A("Id")} = {sql.Column<Event>("e", "ActorId")})
            """;
    }

    // String.IsNullOrWhiteSpace's Unicode whitespace set; do not return the underlying artifact or key.
    private const string Whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";
    private static string NotBlank(string value) => $"length(btrim({value},{Text(Whitespace)})) > 0";

    private static string ImageUri(SourceSql sql, string id, bool profile)
    {
        string S(string property) => sql.Column<StorageObject>("img", property);
        string mime = $"translate(btrim({S("ContentType")},{Text(Whitespace)}),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')";
        string extension = $"translate(ltrim(btrim({S("Extension")},{Text(Whitespace)}),'.'),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')";
        return $"""
            (SELECT '/api/storageobject/' || {S("Id")}::text || '/public' FROM {sql.Table<StorageObject>()} img
                WHERE {sql.Live<StorageObject>("img")} AND {S("Id")} = {id}
                    AND {S("Id")} <> '00000000-0000-0000-0000-000000000000'::uuid
                    AND {S("StorageProviderBindingId")} IS NOT NULL
                    AND {S("StorageProviderBindingId")} <> '00000000-0000-0000-0000-000000000000'::uuid
                    AND {S("Provider")} IN ({Text(StorageProviders.Local)},{Text(StorageProviders.S3Compatible)})
                    AND {NotBlank(S("ObjectKey"))}
                    AND {S("LifecycleState")} = {Text(StorageObjectLifecycleStates.Active)}
                    AND {S("Visibility")} = {Text(StorageObjectVisibilities.PublicImage)}
                    AND {S("Purpose")} IN ({Text(StorageObjectPurposes.LegacyImage)},{Text(StorageObjectPurposes.ProfileImage)},{Text(StorageObjectPurposes.EventImage)})
                    AND {S("OwningResourceKind")} IS DISTINCT FROM {Text(StorageOwningResourceKinds.EventResource)}
                    {(profile ? $"AND {S("OwningResourceKind")} IS NULL AND {S("OwningResourceId")} IS NULL" : "")}
                    AND CASE {mime} WHEN 'image/jpeg' THEN {extension} IN ('jpg','jpeg','jpe')
                        WHEN 'image/png' THEN {extension} = 'png' WHEN 'image/gif' THEN {extension} = 'gif'
                        WHEN 'image/webp' THEN {extension} = 'webp' WHEN 'image/avif' THEN {extension} = 'avif'
                        ELSE false END)
            """;
    }

    private static string OptionalQuestionnaire(SourceSql sql)
    {
        string A(string property) => sql.Column<ParticipationRequirementAttachment>("qa", property);
        string R(string property) => sql.Column<RegistrationRequirement>("qr", property);
        string F(string property) => sql.Column<RegistrationFormVersion>("qf", property);
        return $"""
            EXISTS (SELECT 1 FROM {sql.Table<EventParticipationConfiguration>()} qc
                JOIN {sql.Table<ParticipationRequirementAttachment>()} qa ON {A("ParticipationConfigurationId")} = {sql.Column<EventParticipationConfiguration>("qc", "Id")}
                JOIN {sql.Table<RegistrationRequirement>()} qr ON {R("Id")} = {A("RegistrationRequirementId")}
                JOIN {sql.Table<RegistrationFormVersion>()} qf ON {F("Id")} = {A("RegistrationFormVersionId")}
                WHERE {sql.Live<EventParticipationConfiguration>("qc")} AND {sql.Live<ParticipationRequirementAttachment>("qa")}
                    AND {sql.Live<RegistrationRequirement>("qr")} AND {sql.Live<RegistrationFormVersion>("qf")}
                    AND {sql.Column<EventParticipationConfiguration>("qc", "Id")} = {sql.Column<Event>("e", "Id")}
                    AND {A("IsStandaloneQuestionnaire")}
                    AND {R("CompletionEffectId")} = {(int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect}
                    AND {F("StatusId")} = {(int)RegistrationFormStatusEnum.Published}
                    AND {string.Join(" AND ", new[] { "SchemaHash", "DataSchemaArtifact", "UiSchemaArtifact", "LogicSchemaArtifact", "MappingArtifact" }.Select(field => NotBlank(F(field))))})
            """;
    }

    private static string TicketSummary(SourceSql sql)
    {
        string C(string property) => sql.Column<EventTicketCatalogVersion>("tc", property);
        string T(string property) => sql.Column<EventTicketType>("tt", property);
        string mode = T("TicketPricingModeId");
        string amount = $"""
            CASE {mode}
                WHEN {(int)TicketPricingModeEnum.Free} THEN 0
                WHEN {(int)TicketPricingModeEnum.Fixed} THEN {T("FixedPriceMinor")}
                WHEN {(int)TicketPricingModeEnum.Donation} THEN COALESCE({T("MinimumPriceMinor")},0)
                WHEN {(int)TicketPricingModeEnum.PayWhatYouCan} THEN COALESCE({T("MinimumPriceMinor")},0)
                WHEN {(int)TicketPricingModeEnum.SlidingScale} THEN {T("MinimumPriceMinor")} END
            """;
        return $"""
            (SELECT CASE WHEN price.invalid THEN pg_catalog.jsonb_build_object('Invalid',true)
                ELSE pg_catalog.jsonb_build_object('CurrencyCode',{C("CurrencyCode")},'FromAmountMinor',price.minimum,
                    'SummaryCode',CASE WHEN price.kinds = 1 THEN CASE price.mode
                        WHEN {(int)TicketPricingModeEnum.Free} THEN 'FREE'
                        WHEN {(int)TicketPricingModeEnum.Fixed} THEN 'FIXED'
                        WHEN {(int)TicketPricingModeEnum.Donation} THEN 'DONATION'
                        WHEN {(int)TicketPricingModeEnum.PayWhatYouCan} THEN 'PAY_WHAT_YOU_CAN'
                        WHEN {(int)TicketPricingModeEnum.SlidingScale} THEN 'SLIDING_SCALE' END
                    WHEN price.free THEN 'MIXED_WITH_FREE' ELSE 'MIXED' END) END
            FROM {sql.Table<EventTicketCatalogVersion>()} tc
            CROSS JOIN LATERAL (
                SELECT min({amount}) AS minimum, min({mode}) AS mode, count(DISTINCT {mode}) AS kinds,
                    bool_or({mode} = {(int)TicketPricingModeEnum.Free}) AS free,
                    bool_or(({amount}) IS NULL) AS invalid, count(*) AS count
                FROM {sql.Table<EventTicketType>()} tt WHERE {sql.Live<EventTicketType>("tt")}
                    AND {T("CatalogId")} = {C("Id")}) price
            WHERE {sql.Live<EventTicketCatalogVersion>("tc")} AND {C("EventId")} = {sql.Column<Event>("e", "Id")}
                AND {C("TicketCatalogStatusId")} = {(int)TicketCatalogStatusEnum.Published} AND price.count > 0
                AND EXISTS (SELECT 1 FROM {sql.Table<EventParticipationConfiguration>()} pc
                    WHERE {sql.Live<EventParticipationConfiguration>("pc")} AND {sql.Column<EventParticipationConfiguration>("pc", "Id")} = {sql.Column<Event>("e", "Id")}
                        AND {sql.Column<EventParticipationConfiguration>("pc", "ParticipationHandlingModeId")} = {(int)ParticipationHandlingModeEnum.PlatformManaged}))
            """;
    }
}
