using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;

namespace Explore.Persistence.Security;

public static partial class PostgresLocalAdministrationContract
{
    private const string UuidV7Pattern = "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";
    private static string[] UpdateColumns(Type type) => type == typeof(LocalIdentityUser)
        ? ["PasswordHash", "ConcurrencyStamp", "SecurityStamp", "UpdatedAt"]
        : type == typeof(LocalIdentityCredentialOperation) ? ["Stage", "ConcurrencyStamp", "UpdatedAt"]
        : type == typeof(IdentityUserToken<Guid>) ? ["Value"] : [];

    private static string Row(Type type, params (string Property, string Expression)[] values)
    {
        var source = Inventory.Single(item => item.Type == type);
        if (values.Length != source.Properties.Length
            || !source.Properties.Order(StringComparer.Ordinal).SequenceEqual(
                values.Select(item => item.Property).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException("local_administration_compiler_inventory_drift");
        return "pg_catalog.jsonb_build_object(" + string.Join(",", values.Select(value =>
            Text(value.Property) + "," + value.Expression)) + ")";
    }

    private static string Effect(Type type, string before, string after) =>
        $"effects := effects || pg_catalog.jsonb_build_array(pg_catalog.jsonb_build_object('Type',{Text(Label(type))},'Before',{before},'After',{after}));";

    private static string Metadata(string operation, string subject, int state) =>
        $"pg_catalog.jsonb_build_object('Version',{LocalCredentialStateMetadata.CurrentVersion},'State',{state},'OperationId',{operation},'ApplicationUserId',{subject})::text";

    private static string CompileBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string createUser = Row(typeof(LocalIdentityUser),
            ("Id", "p_accepted->'LocalSubjectId'"), ("AccessFailedCount", "0"),
            ("ConcurrencyStamp", "p_accepted->'IdentityConcurrencyStamp'"), ("CreatedAt", "p_accepted->'ChangedAt'"),
            ("Email", "p_request->'Email'"), ("EmailConfirmed", "true"), ("FirstName", "p_request->'FirstName'"),
            ("LastName", "p_request->'LastName'"), ("LockoutEnabled", "true"), ("LockoutEnd", "NULL"),
            ("NormalizedEmail", "p_request->'NormalizedEmail'"), ("NormalizedUserName", "p_request->'NormalizedUserName'"),
            ("PasswordHash", "p_accepted->'PasswordHash'"), ("PhoneNumber", "NULL"), ("PhoneNumberConfirmed", "false"),
            ("SecurityStamp", "p_accepted->'SecurityStamp'"), ("TwoFactorEnabled", "false"), ("UpdatedAt", "NULL"),
            ("UserName", "p_request->'Email'"));
        string operation(bool reset) => Row(typeof(LocalIdentityCredentialOperation),
            ("Id", "p_request->'OperationId'"), ("ConcurrencyStamp", "p_accepted->'OperationStamp'"),
            ("CreatedAt", "p_accepted->'ChangedAt'"),
            ("ExternalLoginId", reset ? "previous->'ExternalLoginId'" : "p_accepted->'ExternalLoginId'"),
            ("InitiatingApplicationUserId", "p_request->'ActorId'"), ("Kind", reset ? "2" : "1"),
            ("LocalSubjectId", reset ? "previous->'LocalSubjectId'" : "p_accepted->'LocalSubjectId'"),
            ("PersonalActorId", reset ? "previous->'PersonalActorId'" : "p_accepted->'PersonalActorId'"),
            ("PreviousOperationConcurrencyStamp", reset ? "previous->'ConcurrencyStamp'" : "NULL"),
            ("PreviousOperationId", reset ? "previous->'Id'" : "NULL"), ("ResetReason", reset ? "p_request->'Reason'" : "NULL"),
            ("Stage", reset ? "2" : "1"), ("UpdatedAt", "NULL"),
            ("VerifiedAt", reset ? "previous->'VerifiedAt'" : "p_accepted->'ChangedAt'"),
            ("VerifiedByApplicationUserId", reset ? "previous->'VerifiedByApplicationUserId'" : "p_request->'ActorId'"));
        string createToken = Row(typeof(IdentityUserToken<Guid>),
            ("UserId", "p_accepted->'LocalSubjectId'"),
            ("LoginProvider", Text(LocalCredentialStateMetadata.TokenLoginProvider)),
            ("Name", Text(LocalCredentialStateMetadata.TokenName)),
            ("Value", Metadata("p_request->'OperationId'", "p_accepted->'LocalSubjectId'", 1)));
        string graphUser = Row(typeof(User), ("Id", "op->'LocalSubjectId'"),
            ("ConcurrencyStamp", "p_accepted->'UserConcurrencyStamp'"), ("CreatedAt", "op->'CreatedAt'"),
            ("CreatedBy", "p_request->'ActorId'"), ("DeletedAt", "NULL"), ("DeletedBy", "NULL"),
            ("EmailVerified", "identity_user->'EmailConfirmed'"), ("IsDeleted", "false"),
            ("LastActiveTenantId", "NULL"), ("UpdatedAt", "NULL"), ("UpdatedBy", "NULL"));
        string userPii = Row(typeof(UserPii), ("UserId", "op->'LocalSubjectId'"), ("Email", "identity_user->'Email'"),
            ("FirstName", "identity_user->'FirstName'"), ("LastName", "identity_user->'LastName'"));
        string actor = Row(typeof(Actor),
            ("Id", "op->'PersonalActorId'"), ("ActorTypeId", ((int)ActorTypeEnum.User).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("BackgroundColor", "NULL"), ("BackgroundEffect", "NULL"), ("BannerColor", "NULL"),
            ("ConcurrencyStamp", "p_accepted->'ActorConcurrencyStamp'"), ("CreatedAt", "op->'CreatedAt'"),
            ("CreatedBy", "p_request->'ActorId'"), ("DeletedAt", "NULL"), ("DeletedBy", "NULL"),
            ("Description", "NULL"), ("ExternalActorSubjectId", "NULL"), ("GroupId", "NULL"), ("IsDeleted", "false"),
            ("IsSuspended", "false"), ("ModerationReasonCode", "NULL"), ("OrganizationId", "NULL"),
            ("ProfilePictureCid", "NULL"), ("ServicePrincipalId", "NULL"), ("SuspendedAt", "NULL"),
            ("SuspendedBy", "NULL"), ("UpdatedAt", "NULL"), ("UpdatedBy", "NULL"), ("UserId", "op->'LocalSubjectId'"));
        string actorPii = Row(typeof(ActorPii), ("ActorId", "op->'PersonalActorId'"),
            ("DisplayName", "btrim((identity_user->>'FirstName') || ' ' || (identity_user->>'LastName'))"),
            ("ExternalProfilePictureUri", "NULL"), ("ProfilePictureStorageObjectId", "NULL"));
        string login = Row(typeof(UserExternalLogin), ("Id", "op->'ExternalLoginId'"),
            ("AuthenticationProviderId", ((int)AuthenticationProviderKind.Local).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("CreatedAt", "op->'CreatedAt'"), ("CreatedBy", "p_request->'ActorId'"),
            ("ProviderDisplayName", "'Local'"), ("ProviderKey", "op->>'LocalSubjectId'"),
            ("UpdatedAt", "NULL"), ("UpdatedBy", "NULL"), ("UserId", "op->'LocalSubjectId'"));
        string mutationGuard(bool create, bool reset) => $"""
            allowed := ARRAY['ChangedAt','OperationStamp','IdentityConcurrencyStamp','SecurityStamp'
                {(create ? ",'LocalSubjectId','PersonalActorId','ExternalLoginId'" : "")}
                {(create || reset ? ",'PasswordHash'" : "")}{(reset ? ",'PreviousOperationStamp'" : "")}]::text[];
            IF p_accepted->>'ChangedAt' IS NULL
                OR (p_accepted->>'ChangedAt')::timestamptz > pg_catalog.clock_timestamp()
                OR (p_accepted->>'ChangedAt') IS DISTINCT FROM
                    pg_catalog.to_char((p_accepted->>'ChangedAt')::timestamptz AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US') || '0Z'
                OR NULLIF(btrim(p_accepted->>'IdentityConcurrencyStamp'),'') IS NULL
                OR NULLIF(btrim(p_accepted->>'SecurityStamp'),'') IS NULL
                OR p_accepted->>'IdentityConcurrencyStamp' = identity_user->>'ConcurrencyStamp'
                OR p_accepted->>'SecurityStamp' = identity_user->>'SecurityStamp'
                {(create || reset ? "OR NULLIF(btrim(p_accepted->>'PasswordHash'),'') IS NULL" : "")}
                OR NOT COALESCE((p_accepted->>'OperationStamp') ~ {Text(UuidV7Pattern)},false)
                {(create ? """
                    OR NOT COALESCE((p_accepted->>'LocalSubjectId') ~ '^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',false)
                    OR NOT COALESCE((p_accepted->>'PersonalActorId') ~ '^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',false)
                    OR NOT COALESCE((p_accepted->>'ExternalLoginId') ~ '^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',false)
                    """ : "")}
                {(reset ? """
                    OR NOT COALESCE((p_accepted->>'PreviousOperationStamp') ~ '^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',false)
                    OR p_accepted->>'PreviousOperationStamp' = previous->>'ConcurrencyStamp'
                    OR (p_accepted->>'ChangedAt')::timestamptz < (previous->>'VerifiedAt')::timestamptz
                    OR (p_accepted->>'ChangedAt')::timestamptz < (previous->>'CreatedAt')::timestamptz
                    """ : create ? "" : """
                    OR p_accepted->>'OperationStamp' = op->>'ConcurrencyStamp'
                    OR (p_accepted->>'ChangedAt')::timestamptz < (op->>'CreatedAt')::timestamptz
                    """)}
            THEN RAISE EXCEPTION 'local_administration_invalid_private_derivation' USING ERRCODE = '42501'; END IF;
            """;
        string binding(string operationValue) => $"""
            EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'User') u
                JOIN pg_catalog.jsonb_array_elements(image->'Actor') a ON a->'UserId' = u->'Id'
                JOIN pg_catalog.jsonb_array_elements(image->'UserExternalLogin') l ON l->'UserId' = u->'Id'
                WHERE u->'Id' = {operationValue}->'LocalSubjectId' AND u->'IsDeleted' = 'false'::jsonb
                    AND a->'Id' = {operationValue}->'PersonalActorId' AND a->>'ActorTypeId' = '{(int)ActorTypeEnum.User}'
                    AND a->'IsDeleted' = 'false'::jsonb AND a->'IsSuspended' = 'false'::jsonb
                    AND a->'OrganizationId' = 'null'::jsonb AND a->'GroupId' = 'null'::jsonb
                    AND a->'ExternalActorSubjectId' = 'null'::jsonb AND a->'ServicePrincipalId' = 'null'::jsonb
                    AND l->'Id' = {operationValue}->'ExternalLoginId'
                    AND l->>'AuthenticationProviderId' = '{(int)AuthenticationProviderKind.Local}'
                    AND l->'ProviderKey' = {operationValue}->'LocalSubjectId')
            """;
        string ordered = string.Join("\n", Inventory.Select(source => $"""
            expected := pg_catalog.jsonb_set(expected,ARRAY[{Text(Label(source.Type))}],
                (SELECT COALESCE(pg_catalog.jsonb_agg(r ORDER BY r->>{Text(source.Key)}),'[]'::jsonb)
                    FROM pg_catalog.jsonb_array_elements(expected->{Text(Label(source.Type))}) r));
            """));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; image jsonb; expected jsonb; op jsonb; previous jsonb;
                identity_user jsonb; credential_token jsonb; metadata jsonb; new_operation jsonb; effect jsonb; rows jsonb;
                effects jsonb := '[]'::jsonb; outcome text := 'Read'; allowed text[] := ARRAY[]::text[]; key_name text;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                {LoadAttempt(context)}
                IF admitted.plan IS NOT NULL OR admitted.applied
                    OR p_accepted IS NULL OR pg_catalog.jsonb_typeof(p_accepted) IS DISTINCT FROM 'object'
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_accepted) pair
                        WHERE pg_catalog.jsonb_typeof(pair.value) NOT IN ('string','null'))
                    OR ARRAY(SELECT key FROM pg_catalog.jsonb_object_keys(p_accepted) key ORDER BY key COLLATE "C")
                        IS DISTINCT FROM ARRAY(SELECT key FROM unnest(ARRAY['ChangedAt','LocalSubjectId','PersonalActorId',
                            'ExternalLoginId','OperationStamp','PreviousOperationStamp','PasswordHash','IdentityConcurrencyStamp',
                            'SecurityStamp','UserConcurrencyStamp','ActorConcurrencyStamp']) key ORDER BY key COLLATE "C")
                THEN RAISE EXCEPTION 'local_administration_compile_once_required' USING ERRCODE = '42501'; END IF;
                IF {FunctionSql(context, "read")}(p_request) IS DISTINCT FROM admitted.preimage
                THEN RAISE EXCEPTION 'local_administration_compile_preimage_changed' USING ERRCODE = '40001'; END IF;
                image := admitted.preimage->'Data';
                expected := image;
                SELECT r INTO op FROM pg_catalog.jsonb_array_elements(image->'LocalIdentityCredentialOperation') r
                    WHERE r->'Id' = p_request->'OperationId';
                SELECT r INTO previous FROM pg_catalog.jsonb_array_elements(image->'LocalIdentityCredentialOperation') r
                    WHERE r->'Id' = p_request->'ExpectedCurrentOperationId';
                SELECT r INTO identity_user FROM pg_catalog.jsonb_array_elements(image->'LocalIdentityUser') r
                    WHERE r->'Id' = COALESCE(previous->'LocalSubjectId',op->'LocalSubjectId',NULLIF(p_request->'LocalSubjectId','null'::jsonb));
                SELECT r INTO credential_token FROM pg_catalog.jsonb_array_elements(image->'CredentialToken') r
                    WHERE r->'UserId' = identity_user->'Id';
                IF (p_request->>'Stage')::integer IN (3,4)
                    AND pg_catalog.pg_input_is_valid(credential_token->>'Value','jsonb') THEN
                    metadata := (credential_token->>'Value')::jsonb;
                    IF pg_catalog.jsonb_typeof(metadata) IS DISTINCT FROM 'object' THEN metadata := NULL;
                    ELSIF metadata->>'Version' IS DISTINCT FROM '{LocalCredentialStateMetadata.CurrentVersion}'
                        OR (SELECT count(*) FROM pg_catalog.json_each((credential_token->>'Value')::json)) <> 4
                        OR pg_catalog.jsonb_typeof(metadata->'Version') IS DISTINCT FROM 'number'
                        OR pg_catalog.jsonb_typeof(metadata->'State') IS DISTINCT FROM 'number'
                        OR pg_catalog.jsonb_typeof(metadata->'OperationId') IS DISTINCT FROM 'string'
                        OR pg_catalog.jsonb_typeof(metadata->'ApplicationUserId') IS DISTINCT FROM 'string'
                        OR ARRAY(SELECT key FROM pg_catalog.jsonb_object_keys(metadata) key ORDER BY key COLLATE "C")
                            IS DISTINCT FROM ARRAY['ApplicationUserId','OperationId','State','Version']::text[]
                    THEN metadata := NULL; END IF;
                END IF;
                CASE (p_request->>'Stage')::integer
                    WHEN {(int)LocalAdministrationStage.CreatePending} THEN
                        IF op IS NOT NULL THEN
                            IF op->>'Kind' IS DISTINCT FROM '1' OR op->'InitiatingApplicationUserId' IS DISTINCT FROM p_request->'ActorId'
                                OR identity_user IS NULL OR identity_user->'NormalizedUserName' IS DISTINCT FROM p_request->'NormalizedUserName'
                                OR identity_user->'NormalizedEmail' IS DISTINCT FROM p_request->'NormalizedEmail'
                                OR identity_user->'FirstName' IS DISTINCT FROM p_request->'FirstName'
                                OR identity_user->'LastName' IS DISTINCT FROM p_request->'LastName'
                            THEN RAISE EXCEPTION 'local_administration_create_replay_conflict' USING ERRCODE = '42501'; END IF;
                            outcome := 'Replayed';
                        ELSE
                            IF image->'LocalIdentityUser' <> '[]'::jsonb
                            THEN RAISE EXCEPTION 'local_administration_creation_collision' USING ERRCODE = '42501'; END IF;
                            {mutationGuard(true, false)}
                            IF EXISTS (SELECT 1 FROM {sql.Table(typeof(LocalIdentityUser))} r
                                    WHERE r.{sql.Column(typeof(LocalIdentityUser), "Id")} = (p_accepted->>'LocalSubjectId')::uuid)
                                OR EXISTS (SELECT 1 FROM {sql.Table(typeof(User))} r
                                    WHERE r.{sql.Column(typeof(User), "Id")} = (p_accepted->>'LocalSubjectId')::uuid)
                                OR EXISTS (SELECT 1 FROM {sql.Table(typeof(Actor))} r
                                    WHERE r.{sql.Column(typeof(Actor), "Id")} = (p_accepted->>'PersonalActorId')::uuid
                                        OR r.{sql.Column(typeof(Actor), "UserId")} = (p_accepted->>'LocalSubjectId')::uuid)
                                OR EXISTS (SELECT 1 FROM {sql.Table(typeof(UserExternalLogin))} r
                                    WHERE r.{sql.Column(typeof(UserExternalLogin), "Id")} = (p_accepted->>'ExternalLoginId')::uuid
                                        OR r.{sql.Column(typeof(UserExternalLogin), "UserId")} = (p_accepted->>'LocalSubjectId')::uuid)
                            THEN RAISE EXCEPTION 'local_administration_generated_identity_collision' USING ERRCODE = '42501'; END IF;
                            {Effect(typeof(LocalIdentityUser), "NULL", createUser)}
                            {Effect(typeof(LocalIdentityCredentialOperation), "NULL", operation(false))}
                            {Effect(typeof(IdentityUserToken<Guid>), "NULL", createToken)}
                            outcome := 'Created';
                        END IF;
                    WHEN {(int)LocalAdministrationStage.Bind} THEN
                        IF op IS NULL OR op->>'Kind' IS DISTINCT FROM '1' OR identity_user IS NULL
                            OR NULLIF(btrim(identity_user->>'UserName'),'') IS NULL
                            OR NULLIF(btrim(identity_user->>'FirstName'),'') IS NULL
                            OR op->'ConcurrencyStamp' IS DISTINCT FROM p_request->'ExpectedOperationConcurrencyStamp'
                        THEN RAISE EXCEPTION 'local_administration_binding_snapshot_changed' USING ERRCODE = '42501'; END IF;
                        IF {binding("op")} THEN
                            IF NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'UserPii') r WHERE r->'UserId' = op->'LocalSubjectId')
                                OR NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'ActorPii') r WHERE r->'ActorId' = op->'PersonalActorId')
                            THEN RAISE EXCEPTION 'local_administration_partial_binding_graph' USING ERRCODE = '42501'; END IF;
                            outcome := 'Replayed';
                        ELSE
                            IF image->'User' <> '[]'::jsonb OR image->'UserPii' <> '[]'::jsonb OR image->'Actor' <> '[]'::jsonb
                                OR image->'ActorPii' <> '[]'::jsonb OR image->'UserExternalLogin' <> '[]'::jsonb
                                OR identity_user->>'Email' IS NULL
                            THEN RAISE EXCEPTION 'local_administration_partial_binding_graph' USING ERRCODE = '42501'; END IF;
                            allowed := ARRAY['UserConcurrencyStamp','ActorConcurrencyStamp'];
                            IF NOT COALESCE((p_accepted->>'UserConcurrencyStamp')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
                                OR NOT COALESCE((p_accepted->>'ActorConcurrencyStamp')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
                            THEN RAISE EXCEPTION 'local_administration_graph_stamps_required' USING ERRCODE = '42501'; END IF;
                            {Effect(typeof(User), "NULL", graphUser)}
                            {Effect(typeof(UserPii), "NULL", userPii)}
                            {Effect(typeof(Actor), "NULL", actor)}
                            {Effect(typeof(ActorPii), "NULL", actorPii)}
                            {Effect(typeof(UserExternalLogin), "NULL", login)}
                            outcome := 'Bound';
                        END IF;
                    WHEN {(int)LocalAdministrationStage.Activate} THEN
                        IF op IS NULL OR op->>'Kind' IS DISTINCT FROM '1' OR identity_user IS NULL OR metadata IS NULL
                            OR metadata->'OperationId' IS DISTINCT FROM op->'Id'
                            OR metadata->'ApplicationUserId' IS DISTINCT FROM op->'LocalSubjectId'
                            OR op->'ConcurrencyStamp' IS DISTINCT FROM p_request->'ExpectedOperationConcurrencyStamp'
                            OR NOT ({binding("op")})
                        THEN RAISE EXCEPTION 'local_administration_activation_conflict' USING ERRCODE = '42501'; END IF;
                        IF op->>'Stage' = '2' AND metadata->>'State' = '2' THEN outcome := 'AlreadyActivated';
                        ELSE
                            IF op->>'Stage' IS DISTINCT FROM '1' OR metadata->>'State' IS DISTINCT FROM '1'
                                OR NULLIF(btrim(identity_user->>'SecurityStamp'),'') IS NULL
                                OR NULLIF(btrim(identity_user->>'ConcurrencyStamp'),'') IS NULL
                            THEN RAISE EXCEPTION 'local_administration_activation_conflict' USING ERRCODE = '42501'; END IF;
                            {mutationGuard(false, false)}
                            {Effect(typeof(LocalIdentityCredentialOperation), "op",
                                "op || pg_catalog.jsonb_build_object('Stage',2,'ConcurrencyStamp',p_accepted->'OperationStamp','UpdatedAt',p_accepted->'ChangedAt')")}
                            {Effect(typeof(IdentityUserToken<Guid>), "credential_token",
                                "credential_token || pg_catalog.jsonb_build_object('Value'," + Metadata("op->'Id'", "op->'LocalSubjectId'", 2) + ")")}
                            {Effect(typeof(LocalIdentityUser), "identity_user",
                                "identity_user || pg_catalog.jsonb_build_object('ConcurrencyStamp',p_accepted->'IdentityConcurrencyStamp','SecurityStamp',p_accepted->'SecurityStamp','UpdatedAt',p_accepted->'ChangedAt')")}
                            outcome := 'Activated';
                        END IF;
                    WHEN {(int)LocalAdministrationStage.Reset} THEN
                        IF op IS NOT NULL THEN
                            IF op->>'Kind' IS DISTINCT FROM '2' OR op->'InitiatingApplicationUserId' IS DISTINCT FROM p_request->'ActorId'
                                OR op->'LocalSubjectId' IS DISTINCT FROM p_request->'LocalSubjectId'
                                OR op->'PreviousOperationId' IS DISTINCT FROM p_request->'ExpectedCurrentOperationId'
                                OR op->'PreviousOperationConcurrencyStamp' IS DISTINCT FROM p_request->'ExpectedCurrentOperationConcurrencyStamp'
                                OR op->'ResetReason' IS DISTINCT FROM p_request->'Reason'
                            THEN RAISE EXCEPTION 'local_administration_reset_replay_conflict' USING ERRCODE = '42501'; END IF;
                            outcome := 'Replayed';
                        ELSE
                            IF previous IS NULL OR identity_user IS NULL OR metadata IS NULL
                                OR previous->'LocalSubjectId' IS DISTINCT FROM p_request->'LocalSubjectId'
                                OR previous->'ConcurrencyStamp' IS DISTINCT FROM p_request->'ExpectedCurrentOperationConcurrencyStamp'
                                OR previous->>'Kind' NOT IN ('1','2') OR metadata->'OperationId' IS DISTINCT FROM previous->'Id'
                                OR metadata->'ApplicationUserId' IS DISTINCT FROM previous->'LocalSubjectId'
                                OR NOT COALESCE(previous->>'Stage' = '2' AND metadata->>'State' = '2'
                                    OR previous->>'Stage' = '3' AND metadata->>'State' = '3',false)
                                OR NULLIF(btrim(identity_user->>'PasswordHash'),'') IS NULL
                                OR NULLIF(btrim(identity_user->>'SecurityStamp'),'') IS NULL
                                OR NULLIF(btrim(identity_user->>'ConcurrencyStamp'),'') IS NULL
                                OR NOT ({binding("previous")})
                            THEN RAISE EXCEPTION 'local_administration_reset_conflict' USING ERRCODE = '42501'; END IF;
                            {mutationGuard(false, true)}
                            new_operation := {operation(true)};
                            {Effect(typeof(LocalIdentityCredentialOperation), "NULL", "new_operation")}
                            {Effect(typeof(LocalIdentityCredentialOperation), "previous",
                                "previous || pg_catalog.jsonb_build_object('Stage',4,'ConcurrencyStamp',p_accepted->'PreviousOperationStamp','UpdatedAt',p_accepted->'ChangedAt')")}
                            {Effect(typeof(IdentityUserToken<Guid>), "credential_token",
                                "credential_token || pg_catalog.jsonb_build_object('Value'," + Metadata("p_request->'OperationId'", "previous->'LocalSubjectId'", 2) + ")")}
                            {Effect(typeof(LocalIdentityUser), "identity_user",
                                "identity_user || pg_catalog.jsonb_build_object('PasswordHash',p_accepted->'PasswordHash','ConcurrencyStamp',p_accepted->'IdentityConcurrencyStamp','SecurityStamp',p_accepted->'SecurityStamp','UpdatedAt',p_accepted->'ChangedAt')")}
                            outcome := 'Reset';
                        END IF;
                    WHEN {(int)LocalAdministrationStage.ReadProvisioning} THEN NULL;
                    WHEN {(int)LocalAdministrationStage.Disclose} THEN NULL;
                    ELSE RAISE EXCEPTION 'local_administration_unknown_stage' USING ERRCODE = '42501';
                END CASE;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_accepted) pair
                    WHERE pair.value <> 'null'::jsonb AND NOT (pair.key = ANY(allowed)))
                THEN RAISE EXCEPTION 'local_administration_foreign_derivation' USING ERRCODE = '42501'; END IF;
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(effects) LOOP
                    key_name := CASE effect->>'Type' WHEN 'CredentialToken' THEN 'UserId'
                        WHEN 'UserPii' THEN 'UserId' WHEN 'ActorPii' THEN 'ActorId' ELSE 'Id' END;
                    SELECT COALESCE(pg_catalog.jsonb_agg(r),'[]'::jsonb) INTO rows
                        FROM pg_catalog.jsonb_array_elements(expected->(effect->>'Type')) r
                        WHERE r->key_name IS DISTINCT FROM effect->'After'->key_name;
                    expected := pg_catalog.jsonb_set(expected,ARRAY[effect->>'Type'],
                        rows || pg_catalog.jsonb_build_array(effect->'After'));
                END LOOP;
                {ordered}
                UPDATE {sql.Name(AttemptTable)} SET plan = effects,postimage = expected,result = outcome
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN pg_catalog.jsonb_build_object('Outcome',outcome,'Data',expected);
            END;
            """;
    }
}
