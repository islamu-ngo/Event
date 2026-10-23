using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Registration;

namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventDetailContract
{
    // Deliberately no EventSession predicate: detail eligibility is not discovery eligibility.
    private static string Eligible(SourceSql sql)
    {
        string E(string property) => sql.Column<Event>("e", property);
        string A(string property) => sql.Column<Actor>("a", property);
        return $"""
            {sql.Live<Event>("e")}
            AND {E("EventStatusId")} = {(int)EventStatusEnum.Published}
            AND {E("VisibilityTypeId")} = {(int)VisibilityTypeEnum.Public}
            AND EXISTS (SELECT 1 FROM {sql.Table<Actor>()} a
                WHERE {A("Id")} = {E("ActorId")} AND {sql.Live<Actor>("a")} AND NOT {A("IsSuspended")}
                AND CASE WHEN {E("AtprotoRecordId")} IS NULL OR EXISTS (
                    SELECT 1 FROM {sql.Table<AtprotoOutboundRecordOwnership>()} o
                    WHERE {sql.Live<AtprotoOutboundRecordOwnership>("o")}
                        AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "AtprotoRecordId")} = {E("AtprotoRecordId")}
                        AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "SourceEntityType")} = 'Event'
                        AND {sql.Column<AtprotoOutboundRecordOwnership>("o", "SourceEntityId")} = {E("Id")})
                THEN
                    ({A("ActorTypeId")} = {(int)ActorTypeEnum.User} AND {A("UserId")} IS NOT NULL
                        AND EXISTS (SELECT 1 FROM {sql.Table<TenantUser>()} m WHERE {sql.Live<TenantUser>("m")}
                            AND {sql.Column<TenantUser>("m", "UserId")} = {A("UserId")}
                            AND {sql.Column<TenantUser>("m", "ActorId")} = {E("ActorId")}
                            AND {sql.Column<TenantUser>("m", "StatusId")} = {(int)TenantUserStatusEnum.Active}))
                    OR ({A("ActorTypeId")} = {(int)ActorTypeEnum.Organization} AND {A("OrganizationId")} IS NOT NULL
                        AND EXISTS (SELECT 1 FROM {sql.Table<OrganizationTenant>()} m WHERE {sql.Live<OrganizationTenant>("m")}
                            AND {sql.Column<OrganizationTenant>("m", "OrganizationId")} = {A("OrganizationId")}
                            AND {sql.Column<OrganizationTenant>("m", "ApprovalStatusId")} = {(int)ApprovalStatusEnum.Approved}
                            AND {sql.Column<OrganizationTenant>("m", "IsVisible")} AND NOT {sql.Column<OrganizationTenant>("m", "IsSuspended")}))
                    OR ({A("ActorTypeId")} = {(int)ActorTypeEnum.Group} AND {A("GroupId")} IS NOT NULL
                        AND EXISTS (SELECT 1 FROM {sql.Table<GroupTenant>()} m WHERE {sql.Live<GroupTenant>("m")}
                            AND {sql.Column<GroupTenant>("m", "GroupId")} = {A("GroupId")}
                            AND {sql.Column<GroupTenant>("m", "ApprovalStatusId")} = {(int)ApprovalStatusEnum.Approved}
                            AND {sql.Column<GroupTenant>("m", "IsVisible")} AND NOT {sql.Column<GroupTenant>("m", "IsSuspended")}))
                ELSE EXISTS (SELECT 1 FROM {sql.Table<AtprotoRecord>()} r
                    WHERE {sql.Column<AtprotoRecord>("r", "Id")} = {E("AtprotoRecordId")}
                        AND {sql.Column<AtprotoRecord>("r", "TombstonedAt")} IS NULL
                        AND EXISTS (SELECT 1 FROM {sql.Table<AtprotoRecordTenantPresentation>()} p
                            WHERE {sql.Live<AtprotoRecordTenantPresentation>("p")}
                                AND {sql.Column<AtprotoRecordTenantPresentation>("p", "AtprotoRecordId")} = {E("AtprotoRecordId")}
                                AND {sql.Column<AtprotoRecordTenantPresentation>("p", "IsVisible")}
                                AND {sql.Column<AtprotoRecordTenantPresentation>("p", "SourceVersion")} = {sql.Column<AtprotoRecord>("r", "SourceVersion")})
                        AND EXISTS (SELECT 1 FROM {sql.Table<AtprotoIdentity>()} i WHERE {sql.Live<AtprotoIdentity>("i")}
                            AND {sql.Column<AtprotoIdentity>("i", "ActorId")} = {E("ActorId")}
                            AND {sql.Column<AtprotoIdentity>("i", "Did")} = {sql.Column<AtprotoRecord>("r", "Did")}
                            AND {sql.Column<AtprotoIdentity>("i", "IsActive")}
                            AND NOT {sql.Column<AtprotoIdentity>("i", "IsSuspended")}))
                END)
            """;
    }

    private static string Projection(SourceSql sql)
    {
        string E(string property) => sql.Column<Event>("e", property);
        string lookup = string.Join(",", new[]
        {
            Lookup<EventType>(sql, "EventType", E("EventTypeId")),
            Lookup<EventProvenanceType>(sql, "EventProvenanceType", E("EventProvenanceTypeId")),
            Lookup<AudienceGender>(sql, "AudienceGender", E("AudienceGenderId")),
            Lookup<AudienceAge>(sql, "AudienceAge", E("AudienceAgeId"), "MinAge", "MaxAge"),
            Lookup<EventStatus>(sql, "EventStatus", E("EventStatusId")),
            Lookup<VisibilityType>(sql, "VisibilityType", E("VisibilityTypeId")),
            Lookup<EventFormat>(sql, "EventFormat", E("EventFormatId")),
            Lookup<Madhab>(sql, "Madhab", E("MadhabId")),
            Lookup<EventRegistrationPolicy>(sql, "RegistrationPolicy", E("RegistrationPolicyId"))
        });
        string participation = $"""
            (SELECT {sql.Projection<EventParticipationConfiguration>("pc", "Id", "TenantId", "ConcurrencyStamp",
                "ParticipationHandlingModeId", "AdvanceRegistrationObligationId", "IdentityAccessModeId", "GuestRecoveryPolicy")}
                || pg_catalog.jsonb_build_object(
                    {Lookup<ParticipationHandlingMode>(sql, "ParticipationHandlingMode", sql.Column<EventParticipationConfiguration>("pc", "ParticipationHandlingModeId"))},
                    {Lookup<AdvanceRegistrationObligation>(sql, "AdvanceRegistrationObligation", sql.Column<EventParticipationConfiguration>("pc", "AdvanceRegistrationObligationId"))},
                    {Lookup<IdentityAccessMode>(sql, "IdentityAccessMode", sql.Column<EventParticipationConfiguration>("pc", "IdentityAccessModeId"))})
            FROM {sql.Table<EventParticipationConfiguration>()} pc
            WHERE {sql.Live<EventParticipationConfiguration>("pc")} AND {sql.Column<EventParticipationConfiguration>("pc", "Id")} = {E("Id")})
            """;
        string islamic = $"""
            (SELECT {sql.Projection<EventIslamicAspect>("ia", "Id", "MadhabId", "ReferencePrayer", "PrayerTimeOffset",
                "GenderMode", "IncludesQuranRecitation", "PrimaryLanguageId")}
                || pg_catalog.jsonb_build_object(
                    {Lookup<Madhab>(sql, "Madhab", sql.Column<EventIslamicAspect>("ia", "MadhabId"))},
                    {Lookup<Language>(sql, "PrimaryLanguage", sql.Column<EventIslamicAspect>("ia", "PrimaryLanguageId"))})
                FROM {sql.Table<EventIslamicAspect>()} ia WHERE {sql.Column<EventIslamicAspect>("ia", "Id")} = {E("Id")})
            """;
        string tech = $"""
            (SELECT {sql.Projection<EventTechAspect>("ta", "Id", "GithubRepoUrl", "HackathonTrack", "SkillLevel",
                "TechStackTags", "RequiresLaptop", "IsCodingCompetition", "MaxTeamSize", "PrizePool", "PrizeCurrencyCode")}
                FROM {sql.Table<EventTechAspect>()} ta WHERE {sql.Column<EventTechAspect>("ta", "Id")} = {E("Id")})
            """;
        string source = $"""
            (SELECT {sql.Projection<AtprotoRecord>("record", "Id", "Uri", "Cid")}
                FROM {sql.Table<AtprotoRecord>()} record
                WHERE {sql.Column<AtprotoRecord>("record", "Id")} = {E("AtprotoRecordId")})
            """;
        string actor = ActorProjection(sql, E("ActorId"), true);
        string organizer = ActorProjection(sql, E("OrganizerActorId"), false);
        return $"""
            {sql.Projection<Event>("e", "Id", "TenantId", "ConcurrencyStamp", "Title", "Subtitle", "Description", "Content",
                "Slug", "PublicCode", "EventTypeId", "AudienceGenderId", "AudienceAgeId", "ActorId",
                "EventProvenanceTypeId", "SubmittedByUserId", "OrganizerActorId", "SourcePublisherName",
                "FeaturedImageId", "TotalViews", "MadhabId", "VisibilityTypeId", "EventStatusId", "SessionCount",
                "FirstSessionDate", "LastSessionDate", "Timezone", "EventTimeZoneId", "EventFormatId", "RegistrationPolicyId",
                "AtprotoRecordId", "BackgroundColor", "BackgroundEffect", "BackgroundImageId")}
            || pg_catalog.jsonb_build_object({lookup}, 'ParticipationConfiguration',{participation},
                'Actor',{actor}, 'OrganizerActor',{organizer}, 'AtprotoRecord',{source},
                'IslamicAspect',{islamic}, 'TechAspect',{tech}, 'PublicActions',{Actions(sql)},
                'TicketCatalogVersions',{Tickets(sql)}, 'Presentation',{Presentation(sql)},
                'QuestionnaireAttachment',{QuestionnaireAttachment(sql)},
                'Sessions',{ProgramSessions(sql)}, 'SessionGroups',{ProgramGroups(sql)},
                'AgendaItems',{ProgramAgenda(sql)},
                'LatestModeration',(SELECT {sql.Projection<EventModerationRecord>("mr",
                    "ActionKind", "IsIrreversible", "ResultingStatusId")}
                    FROM {sql.Table<EventModerationRecord>()} mr
                    WHERE {sql.Live<EventModerationRecord>("mr")}
                        AND {sql.Column<EventModerationRecord>("mr", "EventId")} = {E("Id")}
                    ORDER BY {sql.Column<EventModerationRecord>("mr", "CreatedAt")} DESC,
                        {sql.Column<EventModerationRecord>("mr", "Id")} DESC LIMIT 1))
            """;
    }

    private static string Lookup<T>(SourceSql sql, string name, string id, params string[] extra) => $"""
        {Text(name)}, (SELECT {sql.Projection<T>("label", ["Id", "FullName", "MasterCode", .. extra])}
            FROM {sql.Table<T>()} label WHERE {sql.Live<T>("label")} AND {sql.Column<T>("label", "Id")} = {id})
        """;

    private static string ActorProjection(SourceSql sql, string id, bool profile)
    {
        string fields = sql.Projection<Actor>("actor", "Id", "ActorTypeId", "UserId", "OrganizationId", "GroupId");
        string pii = $"""
            (SELECT pg_catalog.jsonb_build_object('ActorId',{sql.Column<ActorPii>("pii", "ActorId")},
                    'DisplayName',{sql.Column<ActorPii>("pii", "DisplayName")},
                    'ExternalProfilePictureUri',CASE WHEN {sql.Column<ActorPii>("pii", "ProfilePictureStorageObjectId")} IS NULL
                        THEN {sql.Column<ActorPii>("pii", "ExternalProfilePictureUri")} ELSE NULL END)
                FROM {sql.Table<ActorPii>()} pii
                WHERE {sql.Column<ActorPii>("pii", "ActorId")} = {sql.Column<Actor>("actor", "Id")})
            """;
        return $"""
            (SELECT {fields} {(profile ? $"""
                || pg_catalog.jsonb_build_object(
                    {Lookup<ActorType>(sql, "ActorType", sql.Column<Actor>("actor", "ActorTypeId"))},'Pii',{pii})
                """ : "")}
                FROM {sql.Table<Actor>()} actor
                WHERE {sql.Live<Actor>("actor")} AND {sql.Column<Actor>("actor", "Id")} = {id})
            """;
    }

    private static string Actions(SourceSql sql)
    {
        string A(string property) => sql.Column<EventPublicAction>("action", property);
        string mode = sql.Column<EventParticipationConfiguration>("pc", "ParticipationHandlingModeId");
        string allowed = string.Join(" OR ", Enum.GetValues<ParticipationHandlingModeEnum>().Select(handling =>
            $"({mode} = {(int)handling} AND {A("EventPublicActionKindId")} IN ({string.Join(",",
                Enum.GetValues<EventPublicActionKindEnum>().Where(kind => EventAuthorityRules.IsPublicActionAllowed((int)handling, (int)kind))
                    .Select(kind => (int)kind))}))"));
        return $"""
            COALESCE((SELECT pg_catalog.jsonb_agg(value ORDER BY ordinal,id) FROM (
                SELECT {sql.Projection<EventPublicAction>("action", "Id", "TenantId", "EventId", "ConcurrencyStamp",
                    "EventPublicActionKindId", "HealthStateId", "Url", "DestinationDomain", "Label", "SortOrder", "IsPrimary")}
                    || pg_catalog.jsonb_build_object(
                        {Lookup<EventPublicActionKind>(sql, "EventPublicActionKind", A("EventPublicActionKindId"))},
                        {Lookup<EventPublicActionHealthState>(sql, "HealthState", A("HealthStateId"))}) AS value,
                    {A("SortOrder")} AS ordinal, {A("Id")} AS id
                FROM {sql.Table<EventPublicAction>()} action
                WHERE {sql.Live<EventPublicAction>("action")} AND {A("EventId")} = {sql.Column<Event>("e", "Id")}
                    AND {A("HealthStateId")} = {(int)EventPublicActionHealthStateEnum.Active}
                    AND EXISTS (SELECT 1 FROM {sql.Table<EventParticipationConfiguration>()} pc
                        WHERE {sql.Live<EventParticipationConfiguration>("pc")}
                            AND {sql.Column<EventParticipationConfiguration>("pc", "Id")} = {A("EventId")}
                            AND ({allowed}))
                ) public_actions),'[]'::jsonb)
            """;
    }

    private static string Tickets(SourceSql sql) => $"""
        COALESCE((SELECT pg_catalog.jsonb_agg(
            {sql.Projection<EventTicketCatalogVersion>("catalog", "Id", "TenantId", "EventId", "CurrencyCode", "TicketCatalogStatusId")}
            || pg_catalog.jsonb_build_object('TicketTypes',COALESCE((
                SELECT pg_catalog.jsonb_agg({sql.Projection<EventTicketType>("ticket", "Id", "TenantId", "CatalogId",
                    "TicketPricingModeId", "FixedPriceMinor", "MinimumPriceMinor")})
                FROM {sql.Table<EventTicketType>()} ticket WHERE {sql.Live<EventTicketType>("ticket")}
                    AND {sql.Column<EventTicketType>("ticket", "CatalogId")} = {sql.Column<EventTicketCatalogVersion>("catalog", "Id")}
                ),'[]'::jsonb)))
            FROM {sql.Table<EventTicketCatalogVersion>()} catalog
            WHERE {sql.Live<EventTicketCatalogVersion>("catalog")}
                AND {sql.Column<EventTicketCatalogVersion>("catalog", "EventId")} = {sql.Column<Event>("e", "Id")}
                AND {sql.Column<EventTicketCatalogVersion>("catalog", "TicketCatalogStatusId")} = {(int)TicketCatalogStatusEnum.Published}
                AND EXISTS (SELECT 1 FROM {sql.Table<EventParticipationConfiguration>()} pc
                    WHERE {sql.Live<EventParticipationConfiguration>("pc")}
                        AND {sql.Column<EventParticipationConfiguration>("pc", "Id")} = {sql.Column<Event>("e", "Id")}
                        AND {sql.Column<EventParticipationConfiguration>("pc", "ParticipationHandlingModeId")} = {(int)ParticipationHandlingModeEnum.PlatformManaged})
            ),'[]'::jsonb)
        """;

    private static string Presentation(SourceSql sql) => $"""
        (SELECT pg_catalog.jsonb_build_object(
            'FeaturedImageUri',{ImageUri(sql, sql.Column<Event>("e", "FeaturedImageId"), false)},
            'ActorDisplayName',{sql.Column<ActorPii>("pii", "DisplayName")},
            'ActorProfilePictureUri',CASE WHEN {sql.Column<ActorPii>("pii", "ExternalProfilePictureUri")} IS NULL THEN
                {ImageUri(sql, sql.Column<ActorPii>("pii", "ProfilePictureStorageObjectId"), true)} ELSE NULL END,
            'ActorTypeId',{sql.Column<Actor>("actor", "ActorTypeId")},
            'ActorTypeFullName',(SELECT {sql.Column<ActorType>("kind", "FullName")} FROM {sql.Table<ActorType>()} kind
                WHERE {sql.Column<ActorType>("kind", "Id")} = {sql.Column<Actor>("actor", "ActorTypeId")}),
            'ActorUserId',{sql.Column<Actor>("actor", "UserId")},
            'ActorOrganizationId',{sql.Column<Actor>("actor", "OrganizationId")},
            'ActorGroupId',{sql.Column<Actor>("actor", "GroupId")},
            'HasValidOptionalQuestionnaire',{OptionalQuestionnaire(sql)},'TicketSummary',NULL,
            'Tags',COALESCE((SELECT pg_catalog.jsonb_agg({sql.Projection<Tag>("tag", "Id", "MasterCode", "FullName")})
                FROM {sql.Table<EventTags>()} et JOIN {sql.Table<Tag>()} tag
                    ON {sql.Column<Tag>("tag", "Id")} = {sql.Column<EventTags>("et", "TagId")}
                WHERE {sql.Live<EventTags>("et")} AND {sql.Live<Tag>("tag")}
                    AND {sql.Column<EventTags>("et", "EventId")} = {sql.Column<Event>("e", "Id")}), '[]'::jsonb),
            'Categories',COALESCE((SELECT pg_catalog.jsonb_agg({sql.Projection<Category>("category",
                "Id", "ConcurrencyStamp", "MasterCode", "FullName", "ParentId")})
                FROM {sql.Table<EventCategories>()} ec JOIN {sql.Table<Category>()} category
                    ON {sql.Column<Category>("category", "Id")} = {sql.Column<EventCategories>("ec", "CategoryId")}
                WHERE {sql.Live<EventCategories>("ec")} AND {sql.Live<Category>("category")}
                    AND {sql.Column<EventCategories>("ec", "EventId")} = {sql.Column<Event>("e", "Id")}), '[]'::jsonb))
        FROM {sql.Table<Actor>()} actor LEFT JOIN {sql.Table<ActorPii>()} pii
            ON {sql.Column<ActorPii>("pii", "ActorId")} = {sql.Column<Actor>("actor", "Id")}
        WHERE {sql.Live<Actor>("actor")} AND {sql.Column<Actor>("actor", "Id")} = {sql.Column<Event>("e", "ActorId")})
        """;

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

    private static string OptionalQuestionnaire(SourceSql sql) =>
        $"EXISTS (SELECT 1 {QuestionnaireRows(sql)})";

    private static string QuestionnaireAttachment(SourceSql sql) => $"""
        (SELECT {sql.Projection<ParticipationRequirementAttachment>("qa", "Id", "TenantId", "EventId",
            "ParticipationConfigurationId", "RegistrationWorkflowId", "RegistrationRequirementId",
            "RegistrationFormId", "RegistrationFormVersionId", "IsStandaloneQuestionnaire", "ConcurrencyStamp")}
            || pg_catalog.jsonb_build_object(
                'RegistrationRequirement',{sql.Projection<RegistrationRequirement>("qr",
                    "Id", "TenantId", "EventId", "RegistrationWorkflowId", "CompletionEffectId")},
                'RegistrationFormVersion',{sql.Projection<RegistrationFormVersion>("qf",
                    "Id", "TenantId", "EventId", "RegistrationFormId", "Version", "StatusId", "LanguageTag",
                    "SchemaHash", "DataSchemaArtifact", "UiSchemaArtifact", "LogicSchemaArtifact", "MappingArtifact")})
            {QuestionnaireRows(sql)})
        """;

    private static string QuestionnaireRows(SourceSql sql)
    {
        string A(string property) => sql.Column<ParticipationRequirementAttachment>("qa", property);
        string R(string property) => sql.Column<RegistrationRequirement>("qr", property);
        string F(string property) => sql.Column<RegistrationFormVersion>("qf", property);
        return $"""
            FROM {sql.Table<ParticipationRequirementAttachment>()} qa
                JOIN {sql.Table<RegistrationRequirement>()} qr ON {R("Id")} = {A("RegistrationRequirementId")}
                JOIN {sql.Table<RegistrationFormVersion>()} qf ON {F("Id")} = {A("RegistrationFormVersionId")}
                WHERE {sql.Live<ParticipationRequirementAttachment>("qa")} AND {sql.Live<RegistrationRequirement>("qr")}
                    AND {sql.Live<RegistrationFormVersion>("qf")}
                    AND {A("ParticipationConfigurationId")} = {sql.Column<Event>("e", "Id")}
                    AND {A("IsStandaloneQuestionnaire")}
                    AND EXISTS (SELECT 1 FROM {sql.Table<EventParticipationConfiguration>()} qc
                        WHERE {sql.Live<EventParticipationConfiguration>("qc")}
                            AND {sql.Column<EventParticipationConfiguration>("qc", "Id")} = {sql.Column<Event>("e", "Id")}
                            AND {sql.Column<EventParticipationConfiguration>("qc", "ParticipationHandlingModeId")} = {(int)ParticipationHandlingModeEnum.WalkIn})
                    AND {R("CompletionEffectId")} = {(int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect}
                    AND {F("StatusId")} = {(int)RegistrationFormStatusEnum.Published}
                    AND {string.Join(" AND ", new[] { "SchemaHash", "DataSchemaArtifact", "UiSchemaArtifact", "LogicSchemaArtifact", "MappingArtifact" }.Select(field => NotBlank(F(field))))}
            """;
    }
}
