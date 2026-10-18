using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;

namespace Explore.Persistence.Security;

public static partial class PostgresLocalAdministrationContract
{
    private static PostgresAdministrationAttemptSql AttemptSql(ExploreDbContext context) =>
        new(new HumanPrincipalSourceSql(context), SourceTable, AttemptTable, Prefix, OwnerRole);

    private static readonly (string Name, string Signature, string Arguments, string Result)[] Routines =
    [
        ("read", "jsonb", "p_request jsonb", "jsonb"),
        ("acquire", "jsonb,jsonb", "p_request jsonb,p_before jsonb", "jsonb"),
        ("compile", "jsonb,jsonb", "p_request jsonb,p_accepted jsonb", "jsonb"),
        ("apply", "jsonb", "p_request jsonb", "jsonb"),
        ("complete", "jsonb,jsonb", "p_request jsonb,p_disclosed jsonb", "jsonb"),
        ("changed", "", "", "trigger"),
        ("settlement", "", "", "trigger")
    ];

    private static string Caller(PostgresNativeQueueEnrollment enrollment) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.RuntimeLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid
                IS DISTINCT FROM (p_request->>'TenantId')::uuid
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'local_administration_original_runtime_required' USING ERRCODE = '42501'; END IF;
        """;

    private static string RequestGuard => $"""
        IF p_request IS NULL OR pg_catalog.jsonb_typeof(p_request) IS DISTINCT FROM 'object'
            OR ARRAY(SELECT key FROM pg_catalog.jsonb_object_keys(p_request) key ORDER BY key COLLATE "C")
                IS DISTINCT FROM ARRAY(SELECT key FROM unnest(ARRAY[
                    'Producer','Stage','ActorId','TenantId','OperationId','LocalSubjectId',
                    'ExpectedCurrentOperationId','ExpectedCurrentOperationConcurrencyStamp',
                    'ExpectedOperationConcurrencyStamp','Reason','Email','FirstName','LastName',
                    'NormalizedUserName','NormalizedEmail','PageNumber','PageSize']) key ORDER BY key COLLATE "C")
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(p_request) pair
                WHERE CASE WHEN pair.key IN ('Stage','PageNumber','PageSize')
                    THEN pg_catalog.jsonb_typeof(pair.value) IS DISTINCT FROM 'number'
                    ELSE pg_catalog.jsonb_typeof(pair.value) NOT IN ('string','null') END)
            OR NOT COALESCE((p_request->>'ActorId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR NOT COALESCE((p_request->>'TenantId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR NOT COALESCE(
                p_request->>'Producer' = 'Create' AND (p_request->>'Stage')::integer IN (0,1,2,3,5)
                OR p_request->>'Producer' = 'Reconcile' AND (p_request->>'Stage')::integer IN (1,2,3)
                OR p_request->>'Producer' = 'Reset' AND (p_request->>'Stage')::integer IN (4,5)
                OR p_request->>'Producer' IN ('Status','List') AND (p_request->>'Stage')::integer = 5,false)
            OR p_request->>'Producer' <> 'List' AND NOT COALESCE(
                (p_request->>'OperationId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR (p_request->>'Stage')::integer IN (2,3) AND NOT COALESCE(
                (p_request->>'ExpectedOperationConcurrencyStamp')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR (p_request->>'Stage')::integer NOT IN (2,3) AND p_request->>'ExpectedOperationConcurrencyStamp' IS NOT NULL
            OR p_request->>'Producer' <> 'Reset' AND (
                p_request->>'LocalSubjectId' IS NOT NULL OR p_request->>'ExpectedCurrentOperationId' IS NOT NULL
                OR p_request->>'ExpectedCurrentOperationConcurrencyStamp' IS NOT NULL OR p_request->>'Reason' IS NOT NULL)
            OR p_request->>'Producer' <> 'Create' AND (
                p_request->>'Email' IS NOT NULL OR p_request->>'FirstName' IS NOT NULL OR p_request->>'LastName' IS NOT NULL
                OR p_request->>'NormalizedUserName' IS NOT NULL OR p_request->>'NormalizedEmail' IS NOT NULL)
            OR p_request->>'Producer' <> 'List' AND (
                p_request->>'PageNumber' IS DISTINCT FROM '1' OR p_request->>'PageSize' IS DISTINCT FROM '20')
            OR p_request->>'Producer' = 'List' AND (
                p_request->>'OperationId' IS NOT NULL
                OR NOT COALESCE((p_request->>'PageNumber')::integer >= 1,false)
                OR NOT COALESCE((p_request->>'PageSize')::integer BETWEEN 1 AND 100,false)
                OR ((p_request->>'PageNumber')::bigint - 1) * (p_request->>'PageSize')::bigint > 2147483647)
            OR p_request->>'Producer' = 'Create' AND (
                NULLIF(btrim(p_request->>'Email'),'') IS NULL OR length(p_request->>'Email') > 256
                OR NULLIF(btrim(p_request->>'FirstName'),'') IS NULL OR length(p_request->>'FirstName') > 200
                OR p_request->>'LastName' IS NULL OR length(p_request->>'LastName') > 200
                OR NULLIF(p_request->>'NormalizedUserName','') IS NULL OR length(p_request->>'NormalizedUserName') > 256
                OR NULLIF(p_request->>'NormalizedEmail','') IS NULL OR length(p_request->>'NormalizedEmail') > 256)
            OR p_request->>'Producer' = 'Reset' AND (
                NOT COALESCE((p_request->>'LocalSubjectId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
                OR NOT COALESCE((p_request->>'ExpectedCurrentOperationId')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
                OR NOT COALESCE((p_request->>'ExpectedCurrentOperationId')::uuid <> (p_request->>'OperationId')::uuid,false)
                OR NOT COALESCE((p_request->>'ExpectedCurrentOperationConcurrencyStamp')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
                OR NULLIF(btrim(p_request->>'Reason'),'') IS NULL OR length(p_request->>'Reason') > {LocalCredentialResetRequest.MaximumReasonLength})
        THEN RAISE EXCEPTION 'local_administration_closed_request_required' USING ERRCODE = '42501'; END IF;
        """;

    private static string Projection(ExploreDbContext context, Type type, string alias)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return "pg_catalog.jsonb_build_object(" + string.Join(",", Inventory.Single(item => item.Type == type).Properties.Select(name =>
        {
            var property = context.Model.FindEntityType(type)!.FindProperty(name)!;
            string value = alias + "." + sql.Column(type, name);
            Type scalar = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (scalar == typeof(DateTime) || scalar == typeof(DateTimeOffset))
                value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0Z'";
            return Text(name) + "," + value;
        })) + ")";
    }

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string T<T>() => sql.Table(typeof(T));
        string C<T>(string property) => sql.Column(typeof(T), property);
        string tokenKey = $"t.{C<IdentityUserToken<Guid>>("LoginProvider")} = {Text(LocalCredentialStateMetadata.TokenLoginProvider)}"
            + $" AND t.{C<IdentityUserToken<Guid>>("Name")} = {Text(LocalCredentialStateMetadata.TokenName)}";
        string Data(Type type, string source) => Text(Label(type)) + ",(SELECT COALESCE(pg_catalog.jsonb_agg("
            + Projection(context, type, "r") + " ORDER BY r." + sql.Column(type, Inventory.Single(item => item.Type == type).Key)
            + "),'[]'::jsonb) FROM " + source + " r)";
        return $"""
            DECLARE result jsonb; generation bigint;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                SELECT revision INTO STRICT generation FROM {sql.Name(SourceTable)} WHERE id = 1;
                WITH wanted_operations AS (
                    SELECT o.* FROM {T<LocalIdentityCredentialOperation>()} o
                    WHERE o.{C<LocalIdentityCredentialOperation>("Id")} IN (
                        (p_request->>'OperationId')::uuid,(p_request->>'ExpectedCurrentOperationId')::uuid)),
                selected_users AS (
                    SELECT u.* FROM {T<LocalIdentityUser>()} u
                    WHERE p_request->>'Producer' = 'List'
                        OR u.{C<LocalIdentityUser>("Id")} = (p_request->>'LocalSubjectId')::uuid
                        OR u.{C<LocalIdentityUser>("Id")} IN (SELECT {C<LocalIdentityCredentialOperation>("LocalSubjectId")} FROM wanted_operations)
                        OR p_request->>'Producer' = 'Create' AND (
                            u.{C<LocalIdentityUser>("NormalizedUserName")} = p_request->>'NormalizedUserName' COLLATE "C"
                            OR u.{C<LocalIdentityUser>("NormalizedEmail")} = p_request->>'NormalizedEmail' COLLATE "C")
                    ORDER BY u.{C<LocalIdentityUser>("CreatedAt")},u.{C<LocalIdentityUser>("Id")}
                    LIMIT CASE WHEN p_request->>'Producer' = 'List' THEN (p_request->>'PageSize')::integer ELSE NULL END
                    OFFSET CASE WHEN p_request->>'Producer' = 'List'
                        THEN ((p_request->>'PageNumber')::integer - 1) * (p_request->>'PageSize')::integer ELSE 0 END),
                subjects AS (
                    SELECT {C<LocalIdentityUser>("Id")} AS id FROM selected_users
                    UNION SELECT {C<LocalIdentityCredentialOperation>("LocalSubjectId")} FROM wanted_operations
                    UNION SELECT (p_request->>'LocalSubjectId')::uuid WHERE p_request->>'LocalSubjectId' IS NOT NULL),
                selected_tokens AS (
                    SELECT t.* FROM {T<IdentityUserToken<Guid>>()} t
                    WHERE {tokenKey} AND t.{C<IdentityUserToken<Guid>>("UserId")} IN (SELECT id FROM subjects)),
                selected_operations AS (
                    SELECT o.* FROM {T<LocalIdentityCredentialOperation>()} o
                    WHERE o.{C<LocalIdentityCredentialOperation>("Id")} IN (
                        SELECT {C<LocalIdentityCredentialOperation>("Id")} FROM wanted_operations)
                        OR o.{C<LocalIdentityCredentialOperation>("Id")}::text IN (
                            SELECT CASE WHEN pg_catalog.pg_input_is_valid(t.{C<IdentityUserToken<Guid>>("Value")},'jsonb')
                                THEN t.{C<IdentityUserToken<Guid>>("Value")}::jsonb->>'OperationId' END FROM selected_tokens t)),
                selected_graph_users AS (
                    SELECT r.* FROM {T<User>()} r WHERE r.{C<User>("Id")} IN (SELECT id FROM subjects)),
                selected_user_pii AS (
                    SELECT r.* FROM {T<UserPii>()} r WHERE r.{C<UserPii>("UserId")} IN (SELECT id FROM subjects)),
                selected_actors AS (
                    SELECT r.* FROM {T<Actor>()} r WHERE r.{C<Actor>("UserId")} IN (SELECT id FROM subjects)
                        OR r.{C<Actor>("Id")} IN (SELECT {C<LocalIdentityCredentialOperation>("PersonalActorId")} FROM selected_operations)),
                selected_actor_pii AS (
                    SELECT r.* FROM {T<ActorPii>()} r WHERE r.{C<ActorPii>("ActorId")} IN (SELECT {C<Actor>("Id")} FROM selected_actors)
                        OR r.{C<ActorPii>("ActorId")} IN (SELECT {C<LocalIdentityCredentialOperation>("PersonalActorId")} FROM selected_operations)),
                selected_logins AS (
                    SELECT r.* FROM {T<UserExternalLogin>()} r
                    WHERE r.{C<UserExternalLogin>("UserId")} IN (SELECT id FROM subjects)
                        OR r.{C<UserExternalLogin>("Id")} IN (SELECT {C<LocalIdentityCredentialOperation>("ExternalLoginId")} FROM selected_operations)
                        OR r.{C<UserExternalLogin>("AuthenticationProviderId")} = {(int)AuthenticationProviderKind.Local}
                            AND r.{C<UserExternalLogin>("ProviderKey")} IN (SELECT id::text FROM subjects))
                SELECT pg_catalog.jsonb_build_object(
                    {Data(typeof(LocalIdentityUser), "selected_users")},
                    {Data(typeof(LocalIdentityCredentialOperation), "selected_operations")},
                    {Data(typeof(IdentityUserToken<Guid>), "selected_tokens")},
                    {Data(typeof(User), "selected_graph_users")},
                    {Data(typeof(UserPii), "selected_user_pii")},
                    {Data(typeof(Actor), "selected_actors")},
                    {Data(typeof(ActorPii), "selected_actor_pii")},
                    {Data(typeof(UserExternalLogin), "selected_logins")},
                    'Page',CASE WHEN p_request->>'Producer' = 'List' THEN (
                        SELECT COALESCE(pg_catalog.jsonb_agg({C<LocalIdentityUser>("Id")}
                            ORDER BY {C<LocalIdentityUser>("CreatedAt")},{C<LocalIdentityUser>("Id")}),'[]'::jsonb) FROM selected_users) ELSE '[]'::jsonb END,
                    'TotalCount',CASE WHEN p_request->>'Producer' = 'List' THEN (SELECT count(*)::integer FROM {T<LocalIdentityUser>()}) ELSE NULL END)
                INTO result;
                IF generation IS DISTINCT FROM (SELECT revision FROM {sql.Name(SourceTable)} WHERE id = 1)
                THEN RAISE EXCEPTION 'local_administration_read_changed' USING ERRCODE = '40001'; END IF;
                RETURN pg_catalog.jsonb_build_object('Request',p_request,'Revision',generation,'Data',result);
            END;
            """;
    }

    private static string AcquireBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        AttemptSql(context).AcquireBody(Guard(context, enrollment), RequestGuard, Caller(enrollment));

    private static string LoadAttempt(ExploreDbContext context) => AttemptSql(context).LoadAttempt;

    private static string ApplyBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string writes = string.Join("\n", Inventory.Select(source =>
        {
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", source.Properties.Select(property =>
                Text(sql.ColumnName(source.Type, property)) + ",effect->'After'->" + Text(property))) + ")";
            string predicate = "r." + sql.Column(source.Type, source.Key) + " = (effect->'After'->>"
                + Text(source.Key) + ")::uuid";
            if (source.Type == typeof(IdentityUserToken<Guid>))
                predicate += $" AND r.{sql.Column(source.Type, "LoginProvider")} = {Text(LocalCredentialStateMetadata.TokenLoginProvider)}"
                    + $" AND r.{sql.Column(source.Type, "Name")} = {Text(LocalCredentialStateMetadata.TokenName)}";
            string updates = string.Join(",", UpdateColumns(source.Type).Select(property =>
                sql.Column(source.Type, property) + " = n." + sql.Column(source.Type, property)));
            string update = updates.Length == 0
                ? "RAISE EXCEPTION 'local_administration_insert_only_graph' USING ERRCODE = '42501';"
                : $"UPDATE {sql.Table(source.Type)} r SET {updates} FROM pg_catalog.jsonb_populate_record(NULL::{sql.Table(source.Type)},{mapped}) n WHERE {predicate};";
            if (source.Type == typeof(LocalIdentityUser))
            {
                string activation = string.Join(",", UpdateColumns(source.Type).Where(property => property != "PasswordHash")
                    .Select(property => sql.Column(source.Type, property) + " = n." + sql.Column(source.Type, property)));
                update = $"""
                    IF (p_request->>'Stage')::integer = {(int)LocalAdministrationStage.Activate} THEN
                        UPDATE {sql.Table(source.Type)} r SET {activation}
                            FROM pg_catalog.jsonb_populate_record(NULL::{sql.Table(source.Type)},{mapped}) n WHERE {predicate};
                    ELSE
                        {update}
                    END IF;
                    """;
            }
            return $"""
                WHEN {Text(Label(source.Type))} THEN
                    SELECT {Projection(context, source.Type, "r")} INTO stored FROM {sql.Table(source.Type)} r WHERE {predicate};
                    IF stored IS DISTINCT FROM NULLIF(effect->'Before','null'::jsonb)
                    THEN RAISE EXCEPTION 'local_administration_effect_preimage_changed' USING ERRCODE = '40001'; END IF;
                    IF stored IS NULL THEN
                        INSERT INTO {sql.Table(source.Type)} ({string.Join(",", source.Properties.Select(property => sql.Column(source.Type, property)))})
                            SELECT {string.Join(",", source.Properties.Select(property => "n." + sql.Column(source.Type, property)))}
                            FROM pg_catalog.jsonb_populate_record(NULL::{sql.Table(source.Type)},{mapped}) n;
                    ELSE
                        {update}
                    END IF;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN RAISE EXCEPTION 'local_administration_effect_cardinality' USING ERRCODE = '40001'; END IF;
                """;
        }));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; effect jsonb; stored jsonb; affected bigint; current_image jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                {LoadAttempt(context)}
                IF admitted.plan IS NULL OR admitted.applied
                THEN RAISE EXCEPTION 'local_administration_plan_missing_or_replayed' USING ERRCODE = '42501'; END IF;
                current_image := {FunctionSql(context, "read")}(p_request);
                IF current_image IS DISTINCT FROM admitted.preimage
                THEN RAISE EXCEPTION 'local_administration_compiled_preimage_changed' USING ERRCODE = '40001'; END IF;
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(admitted.plan) LOOP
                    CASE effect->>'Type'
                        {writes}
                        ELSE RAISE EXCEPTION 'local_administration_invalid_private_plan' USING ERRCODE = '55000';
                    END CASE;
                END LOOP;
                current_image := {FunctionSql(context, "read")}(p_request);
                IF current_image->'Data' IS DISTINCT FROM admitted.postimage
                    OR (current_image->>'Revision')::bigint IS DISTINCT FROM
                        (admitted.preimage->>'Revision')::bigint + pg_catalog.jsonb_array_length(admitted.plan)
                THEN RAISE EXCEPTION 'local_administration_group_effect_mismatch' USING ERRCODE = '40001'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET applied = true WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN pg_catalog.jsonb_build_object('Outcome',admitted.result,'Image',current_image);
            END;
            """;
    }

    private static string CompleteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        AttemptSql(context).CompleteBody(Guard(context, enrollment), RequestGuard, Caller(enrollment));

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return AttemptSql(context).ChangedBody(Guard(context, enrollment),
            string.Join(",", Inventory.Select(source => Text(sql.Table(source.Type)) + "::regclass")));
    }

    private static string SettlementBody(ExploreDbContext context) => AttemptSql(context).SettlementBody;

    private static IEnumerable<(string Name, string Signature, string Arguments, string Result, string Body)> Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Routines.Select(routine => (routine.Name, routine.Signature, routine.Arguments, routine.Result, routine.Name switch
        {
            "read" => ReadBody(context, enrollment),
            "acquire" => AcquireBody(context, enrollment),
            "compile" => CompileBody(context, enrollment),
            "apply" => ApplyBody(context, enrollment),
            "complete" => CompleteBody(context, enrollment),
            "changed" => ChangedBody(context, enrollment),
            "settlement" => SettlementBody(context),
            _ => throw new InvalidOperationException("local_administration_routine_inventory_drift")
        }));
}
