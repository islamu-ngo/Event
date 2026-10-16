using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresOrdinarySettingsContract
{
    private static (string Property, string Field)[] Fields(Type type) =>
    [
        ("Id", "Id"), .. type != typeof(SystemSetting) ? new[] { ("TenantId", "TenantId") } : [],
        .. type == typeof(UserPreference) ? new[] { ("UserId", "UserId") } : [],
        ("SettingKey", "Key"), ("Value", "Value"),
        .. type == typeof(SystemSetting) ? new[] { ("SettingValueTypeId", "Type") } : [],
        .. type != typeof(UserPreference) ? new[] { ("IsLocked", "Locked") } : [],
        .. type == typeof(SystemSetting) ? new[]
        {
            ("AllowedValues", "AllowedValues"), ("Description", "Description"),
            ("Category", "Category"), ("DisplayOrder", "DisplayOrder")
        } : [],
        ("CreatedAt", "CreatedAt"), ("CreatedBy", "CreatedBy"), ("UpdatedAt", "UpdatedAt"), ("UpdatedBy", "UpdatedBy")
    ];

    private static string Group(Type type) => type == typeof(SystemSetting) ? "System"
        : type == typeof(TenantSetting) ? "Tenant" : "User";

    private static string Projection(ExploreDbContext context, Type type, string alias)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return "pg_catalog.jsonb_build_object(" + string.Join(",", Fields(type).Select(field =>
        {
            string value = $"{alias}.{sql.Column(type, field.Property)}";
            if (field.Property == "AllowedValues") value += "::pg_catalog.text";
            if (field.Property is "CreatedAt" or "UpdatedAt")
                value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0Z'";
            return $"{Text(field.Field)},{value}";
        })) + ")";
    }

    private static string ImageSql(ExploreDbContext context, string tenant, string user, string keys)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return "pg_catalog.jsonb_build_object(" + string.Join(",", Sources.Select(type =>
        {
            string predicate = type == typeof(SystemSetting) ? "true"
                : $"r.{sql.Column(type, "TenantId")} = {tenant}"
                    + (type == typeof(UserPreference) ? $" AND r.{sql.Column(type, "UserId")} = {user}" : "");
            string rows = $"""
                SELECT r.{sql.Column(type, "SettingKey")} AS key,{Projection(context, type, "r")} AS row
                    FROM {sql.Table(type)} r WHERE {predicate}
                """;
            return $"""
                {Text(Group(type))},(WITH rows AS ({rows}),
                    keys AS (SELECT pg_catalog.jsonb_array_elements_text({keys}) AS key UNION SELECT key FROM rows)
                    SELECT COALESCE(pg_catalog.jsonb_object_agg(keys.key,rows.row),pg_catalog.jsonb_build_object())
                    FROM keys LEFT JOIN rows ON rows.key = keys.key COLLATE "C")
                """;
        })) + ")";
    }

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool attempt) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR NOT COALESCE((session_user = {Text(enrollment.RuntimeLogin)} AND p_tenant IS NOT NULL
                OR session_user = {Text(enrollment.GlobalLogin)} AND p_tenant IS NULL),false)
            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            OR p_user = '00000000-0000-0000-0000-000000000000'::uuid OR p_user IS NOT NULL AND p_tenant IS NULL
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
            OR pg_catalog.pg_has_role(session_user,pg_catalog.to_regrole({Text(enrollment.MigratorLogin)}),'SET')
            OR pg_catalog.pg_has_role(session_user,pg_catalog.to_regrole({Text(enrollment.MigratorLogin)}),'USAGE')
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'SET') OR pg_catalog.pg_has_role(session_user,r.oid,'USAGE'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
            {(attempt ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
        THEN RAISE EXCEPTION 'ordinary_settings_original_service' USING ERRCODE = '42501'; END IF;
        """;

    private const string KeysGuard = """
        IF pg_catalog.jsonb_typeof(p_keys) IS DISTINCT FROM 'array'
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_keys) v WHERE pg_catalog.jsonb_typeof(v) <> 'string' OR v = '""'::jsonb)
            OR (SELECT count(*) FROM pg_catalog.jsonb_array_elements(p_keys))
                <> (SELECT count(DISTINCT v) FROM pg_catalog.jsonb_array_elements(p_keys) v)
        THEN RAISE EXCEPTION 'ordinary_settings_source_keys' USING ERRCODE = '42501'; END IF;
        """;

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE revision_before bigint; tenant_revision_before bigint; image jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, false)}
                {KeysGuard}
                SELECT revision,tenant_revision INTO revision_before,tenant_revision_before FROM {sql.Name(SourceTable)} WHERE id = 1;
                IF revision_before IS NULL OR revision_before < 0 OR tenant_revision_before IS NULL OR tenant_revision_before < 0
                THEN RAISE EXCEPTION 'ordinary_settings_anchor_missing' USING ERRCODE = '55000'; END IF;
                image := {ImageSql(context, "p_tenant", "p_user", "p_keys")};
                IF revision_before IS DISTINCT FROM (SELECT revision FROM {sql.Name(SourceTable)} WHERE id = 1)
                    OR tenant_revision_before IS DISTINCT FROM (SELECT tenant_revision FROM {sql.Name(SourceTable)} WHERE id = 1)
                THEN RAISE EXCEPTION 'ordinary_settings_observation_changed' USING ERRCODE = '40001'; END IF;
                RETURN pg_catalog.jsonb_build_object('Image',image,'Revision',revision_before,'TenantRevision',tenant_revision_before,
                    'Backend',pg_catalog.pg_backend_pid(),'Database',pg_catalog.current_database(),
                    'Namespace',{Text(sql.Schema)},'Tenant',p_tenant,'User',p_user,'Login',session_user);
            END;
            """;
    }

    private static string AcquireBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string definitions = string.Join(",", SettingRegistry.All.Where(definition => !Specialized(definition.Key))
            .Select(definition => $"({Text(definition.Key)},{(int)definition.MinScope},{(int)definition.MaxScope},{definition.IsLockable.ToString().ToLowerInvariant()})"));
        return $"""
            DECLARE p_keys jsonb := p_request->'Keys'; observed jsonb; effect jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, true)}
                {KeysGuard}
                IF pg_catalog.jsonb_typeof(p_request->'Effects') IS DISTINCT FROM 'array'
                    OR pg_catalog.jsonb_typeof(p_request->'NewIds') IS DISTINCT FROM 'object'
                    OR (p_request->>'Actor')::uuid IS NULL OR (p_request->>'Actor')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    OR p_request->>'OccurredAt' IS NULL
                    OR (p_request->>'OccurredAt')::timestamptz IS NULL
                    OR p_request->>'OccurredAt' !~ {Text(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{6}0Z$")}
                    OR p_user IS NOT NULL AND (p_request->>'Actor')::uuid IS DISTINCT FROM p_user
                    OR (SELECT count(*) FROM pg_catalog.jsonb_array_elements(p_request->'Effects'))
                        <> (SELECT count(DISTINCT e->>'Key') FROM pg_catalog.jsonb_array_elements(p_request->'Effects') e)
                    OR (SELECT count(*) FROM pg_catalog.jsonb_object_keys(p_request->'NewIds'))
                        <> (SELECT count(*) FROM pg_catalog.jsonb_array_elements(p_request->'Effects'))
                THEN RAISE EXCEPTION 'ordinary_settings_original_request' USING ERRCODE = '42501'; END IF;
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(p_request->'Effects') LOOP
                    IF NOT (p_keys ? (effect->>'Key'))
                        OR (effect->>'TenantId')::uuid IS DISTINCT FROM p_tenant
                        OR NOT COALESCE(((effect->>'Scope')::integer = 0 AND p_tenant IS NULL AND p_user IS NULL
                            OR (effect->>'Scope')::integer = 1 AND p_tenant IS NOT NULL AND p_user IS NULL
                            OR (effect->>'Scope')::integer = 4 AND p_tenant IS NOT NULL AND p_user IS NOT NULL),false)
                        OR (effect->>'UserId')::uuid IS DISTINCT FROM p_user
                        OR (effect->>'Operation')::integer = 1 AND (effect->>'Kind')::integer = 0
                            AND effect->>'Key' = {Text(GovernanceSettingKeys.Organizations.VerificationRequired)}
                            AND NOT (p_keys ? {Text(GovernanceSettingKeys.Organizations.TenantCanOmitVerification)})
                        OR NOT COALESCE(((effect->>'Scope')::integer = 0 AND (effect->>'Operation')::integer = 2
                            OR (effect->>'Scope')::integer = 1 AND (effect->>'Operation')::integer IN (1,3)
                            OR (effect->>'Scope')::integer = 4 AND (effect->>'Operation')::integer = 0),false)
                        OR NOT COALESCE(((effect->>'Kind')::integer = 0 AND pg_catalog.jsonb_typeof(effect->'Value') = 'string'
                                AND effect->'IsLocked' = 'null'::jsonb
                            OR (effect->>'Kind')::integer = 1 AND effect->'Value' = 'null'::jsonb
                                AND pg_catalog.jsonb_typeof(effect->'IsLocked') = 'boolean' AND p_user IS NULL
                            OR (effect->>'Kind')::integer = 2 AND effect->'Value' = 'null'::jsonb
                                AND effect->'IsLocked' = 'null'::jsonb AND p_tenant IS NOT NULL),false)
                        OR NOT EXISTS (SELECT 1 FROM (VALUES {definitions}) d(key,minimum,maximum,lockable)
                            WHERE d.key = effect->>'Key' COLLATE "C"
                                AND ((effect->>'Kind')::integer = 1 OR (effect->>'Scope')::integer BETWEEN d.minimum AND d.maximum)
                                AND ((effect->>'Kind')::integer <> 1 OR d.lockable
                                    OR effect->'IsLocked' = 'false'::jsonb AND (effect->>'Operation')::integer <> 3))
                        OR NOT COALESCE((p_request->'NewIds'->>(effect->>'Key')) ~
                            {Text("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")},false)
                    THEN RAISE EXCEPTION 'ordinary_settings_effect_scope' USING ERRCODE = '42501'; END IF;
                END LOOP;
                IF p_tenant IS NULL AND EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(p_request->'Effects') e
                    WHERE e->>'Key' COLLATE "C" = ANY({DisclosureKeysSql()})) THEN
                    -- Same gate as native tenant enrollment, before any source exclusion.
                    PERFORM pg_catalog.pg_advisory_xact_lock(
                        {RelationalNamedLock.ComputeStableKey(EventDiscoverySourceProviderOperations.TenantEnrollmentResource)}::bigint);
                END IF;
                -- PolicySourceSetFence is acquired by the original managed owner first.
                -- NOWAIT avoids waiting behind a writer that is itself waiting for that fence.
                LOCK TABLE {string.Join(",", Sources.Select(sql.Table))} IN SHARE ROW EXCLUSIVE MODE NOWAIT;
                UPDATE {sql.Name(SourceTable)} SET revision = revision WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'ordinary_settings_anchor_missing' USING ERRCODE = '55000'; END IF;
                observed := {FunctionSql(context, "read")}(p_tenant,p_user,p_keys);
                IF observed IS DISTINCT FROM p_before
                THEN RAISE EXCEPTION 'ordinary_settings_original_image_changed' USING ERRCODE = '40001'; END IF;
                INSERT INTO {sql.Name(AttemptTable)}
                    (attempt,backend,tenant,user_id,request,image,completed,pending,observed,finished,disclosure_tenants)
                    VALUES (pg_catalog.txid_current(),pg_catalog.pg_backend_pid(),p_tenant,p_user,p_request,
                        p_before->'Image',pg_catalog.jsonb_build_object(),NULL,false,false,ARRAY[]::uuid[]);
            END;
            """;
    }

    private static bool Specialized(string key) => EmailDeliverySettingKeys.Contains(key)
        || key.Trim().Equals(EventResourceProviderBindingDocument.SettingKey, StringComparison.OrdinalIgnoreCase)
        || EventResourceSettingMutationGuard.Handles(key) || VisitorAccessSettingMutationGuard.Handles(key)
        || PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal)
        || LocationPrivacySettingDefinitions.All.Any(definition => definition.Key == key);

    private static string AttemptRead(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        SELECT * INTO admitted FROM {new HumanPrincipalSourceSql(context).Name(AttemptTable)}
            WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'ordinary_settings_original_attempt' USING ERRCODE = '42501'; END IF;
        p_tenant := admitted.tenant; p_user := admitted.user_id;
        {Caller(enrollment, true)}
        """;

    private static string WriteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string branches = string.Join("\n", Sources.Select(type =>
        {
            string group = Group(type);
            string mapping = "pg_catalog.jsonb_build_object(" + string.Join(",", Fields(type)
                .Select(field => $"{Text(sql.ColumnName(type, field.Property))},canonical->{Text(field.Field)}")) + ")";
            string columns = string.Join(",", Fields(type).Select(field => sql.Column(type, field.Property)));
            string values = string.Join(",", Fields(type).Select(field => "n." + sql.Column(type, field.Property)));
            string updates = string.Join(",", new[] { "Value", "UpdatedAt", "UpdatedBy" }
                .Concat(type != typeof(UserPreference) ? ["IsLocked"] : [])
                .Select(property => $"{sql.Column(type, property)} = n.{sql.Column(type, property)}"));
            string table = sql.Table(type);
            return $"""
                WHEN {Text(group)} THEN
                    IF canonical = 'null'::jsonb THEN
                        DELETE FROM {table} WHERE {sql.Column(type, "Id")} = (p_before->>'Id')::uuid;
                    ELSIF p_before = 'null'::jsonb THEN
                        INSERT INTO {table} ({columns})
                            SELECT {values} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapping}) n;
                    ELSE
                        UPDATE {table} SET {updates} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapping}) n
                            WHERE {table}.{sql.Column(type, "Id")} = (p_before->>'Id')::uuid;
                    END IF;
                """;
        }));
        string definitions = string.Join(",", SettingRegistry.All.Where(definition => !Specialized(definition.Key)).Select(definition =>
            $"({Text(definition.Key)},{(int)definition.ValueType},{Text(definition.Description)},{Text(definition.Category)})"));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; p_tenant uuid; p_user uuid;
                effect jsonb; canonical jsonb; changed boolean := true; actual jsonb; kind integer; affected uuid[];
            BEGIN
                {Guard(context, enrollment)}
                {AttemptRead(context, enrollment)}
                SELECT value INTO effect FROM pg_catalog.jsonb_array_elements(admitted.request->'Effects')
                    WHERE value->>'Key' = p_key COLLATE "C"
                        AND (value->>'Scope')::integer = CASE p_group WHEN 'System' THEN 0 WHEN 'Tenant' THEN 1 WHEN 'User' THEN 4 ELSE -1 END;
                IF effect IS NULL OR admitted.pending IS NOT NULL OR admitted.completed ? p_key
                    OR p_before IS NULL OR p_after IS NULL OR p_before IS DISTINCT FROM admitted.image->p_group->p_key
                    OR p_operation IS NULL OR p_operation NOT BETWEEN 0 AND 2
                THEN RAISE EXCEPTION 'ordinary_settings_foreign_duplicate_or_stale_effect' USING ERRCODE = '42501'; END IF;
                kind := (effect->>'Kind')::integer;
                IF p_group = 'User' THEN
                    IF (p_operation = 2) IS DISTINCT FROM (kind = 2)
                        OR p_operation = 0 AND p_before <> 'null'::jsonb
                        OR p_operation = 1 AND p_before = 'null'::jsonb
                    THEN RAISE EXCEPTION 'ordinary_settings_preference_presence' USING ERRCODE = '42501'; END IF;
                ELSIF kind IS DISTINCT FROM p_operation THEN
                    RAISE EXCEPTION 'ordinary_settings_effect_kind' USING ERRCODE = '42501';
                END IF;
                IF p_group <> 'System' AND kind <> 2 AND admitted.image->'System'->p_key->'Locked' = 'true'::jsonb
                    OR p_group = 'Tenant' AND admitted.image->'System'->p_key->'Locked' = 'true'::jsonb
                    OR p_group = 'User' AND kind = 0 AND admitted.image->'Tenant'->p_key->'Locked' = 'true'::jsonb
                THEN RAISE EXCEPTION 'ordinary_settings_original_parent_lock' USING ERRCODE = '42501'; END IF;
                IF p_group = 'Tenant' AND kind = 0 AND (effect->>'Operation')::integer = 1
                    AND p_key = {Text(GovernanceSettingKeys.Organizations.VerificationRequired)} AND effect->>'Value' = 'false'
                    AND lower(admitted.image->'System'->{Text(GovernanceSettingKeys.Organizations.TenantCanOmitVerification)}->>'Value') IS DISTINCT FROM 'true'
                THEN RAISE EXCEPTION 'ordinary_settings_original_verification_ceiling' USING ERRCODE = '42501'; END IF;
                canonical := p_before;
                IF kind = 2 THEN
                    canonical := 'null'::jsonb; changed := p_before <> 'null'::jsonb;
                ELSIF kind = 1 AND p_before = 'null'::jsonb THEN
                    IF p_group = 'System' THEN RAISE EXCEPTION 'ordinary_settings_missing_system_lock' USING ERRCODE = '42501'; END IF;
                    changed := false;
                ELSIF kind = 1 AND p_group = 'Tenant' AND p_before->'Locked' = effect->'IsLocked' THEN
                    changed := false;
                ELSIF p_before = 'null'::jsonb THEN
                    canonical := pg_catalog.jsonb_build_object('Id',admitted.request->'NewIds'->p_key,'Key',p_key,
                        'Value',effect->'Value','CreatedAt',admitted.request->'OccurredAt','CreatedBy',admitted.request->'Actor',
                        'UpdatedAt',NULL,'UpdatedBy',NULL);
                    IF p_group = 'System' THEN
                        SELECT canonical || pg_catalog.jsonb_build_object('Type',d.type,'Locked',false,'AllowedValues',NULL,
                            'Description',d.description,'Category',d.category,'DisplayOrder',0) INTO canonical
                            FROM (VALUES {definitions}) d(key,type,description,category) WHERE d.key = p_key COLLATE "C";
                    ELSE
                        canonical := canonical || pg_catalog.jsonb_build_object('TenantId',p_tenant);
                        IF p_group = 'User' THEN canonical := canonical || pg_catalog.jsonb_build_object('UserId',p_user);
                        ELSE canonical := canonical || pg_catalog.jsonb_build_object('Locked',false); END IF;
                    END IF;
                ELSE
                    canonical := canonical || pg_catalog.jsonb_build_object('UpdatedAt',admitted.request->'OccurredAt',
                        'UpdatedBy',admitted.request->'Actor') || CASE kind
                            WHEN 0 THEN pg_catalog.jsonb_build_object('Value',effect->'Value')
                            ELSE pg_catalog.jsonb_build_object('Locked',effect->'IsLocked') END;
                END IF;
                IF canonical IS DISTINCT FROM p_after
                THEN RAISE EXCEPTION 'ordinary_settings_noncanonical_effect' USING ERRCODE = '42501'; END IF;
                IF changed AND p_group <> 'User' AND p_key COLLATE "C" = ANY({DisclosureKeysSql()}) THEN
                    IF p_group = 'System' THEN
                        SELECT COALESCE(array_agg(id ORDER BY id),ARRAY[]::uuid[]) INTO affected
                            FROM {PostgresDiscoveryDisclosureContract.FunctionSql(context, "dependencies")}('all',ARRAY[]::uuid[]) id;
                    ELSE affected := ARRAY[p_tenant]; END IF;
                    SELECT COALESCE(array_agg(DISTINCT id ORDER BY id),ARRAY[]::uuid[]) INTO affected
                        FROM unnest(admitted.disclosure_tenants || affected) id;
                    IF cardinality(affected) > {EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants}
                    THEN RAISE EXCEPTION 'discovery_disclosure_fanout_exceeded' USING ERRCODE = '54000'; END IF;
                    UPDATE {sql.Name(AttemptTable)} SET disclosure_tenants = affected
                        WHERE attempt = admitted.attempt AND backend = admitted.backend;
                END IF;
                UPDATE {sql.Name(AttemptTable)} SET finished = false, observed = false,
                    pending = pg_catalog.jsonb_build_object('Group',p_group,'Key',p_key,'Before',p_before,'After',canonical)
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                IF changed THEN
                    CASE p_group {branches}
                        ELSE RAISE EXCEPTION 'ordinary_settings_foreign_group' USING ERRCODE = '42501';
                    END CASE;
                    IF NOT (SELECT observed FROM {sql.Name(AttemptTable)} WHERE attempt = admitted.attempt AND backend = admitted.backend)
                    THEN RAISE EXCEPTION 'ordinary_settings_missing_native_effect' USING ERRCODE = '42501'; END IF;
                END IF;
                actual := {ImageSql(context, "p_tenant", "p_user", "admitted.request->'Keys'")};
                admitted.image := pg_catalog.jsonb_set(admitted.image,ARRAY[p_group,p_key],canonical);
                IF actual IS DISTINCT FROM admitted.image
                THEN RAISE EXCEPTION 'ordinary_settings_untracked_native_effect' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET image = admitted.image,
                    completed = completed || pg_catalog.jsonb_build_object(p_key,true), pending = NULL, observed = false
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN pg_catalog.jsonb_build_object('Before',p_before,'After',canonical);
            END;
            """;
    }

    private static string CompleteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool terminal)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; p_tenant uuid; p_user uuid;
                omitted jsonb; source_row jsonb; row_group text; disposition text;
            BEGIN
                {Guard(context, enrollment)}
                {AttemptRead(context, enrollment)}
                IF admitted.pending IS NOT NULL OR admitted.observed OR p_expected IS DISTINCT FROM admitted.image
                    OR admitted.image IS DISTINCT FROM {ImageSql(context, "p_tenant", "p_user", "admitted.request->'Keys'")}
                THEN RAISE EXCEPTION 'ordinary_settings_incomplete_expected_image' USING ERRCODE = '42501'; END IF;
                {(terminal ? $"""
                    FOR omitted IN SELECT value FROM pg_catalog.jsonb_array_elements(admitted.request->'Effects') e(value)
                        WHERE NOT (admitted.completed ? (value->>'Key')) LOOP
                        row_group := CASE (omitted->>'Scope')::integer WHEN 0 THEN 'System' WHEN 1 THEN 'Tenant' ELSE 'User' END;
                        source_row := admitted.image->row_group->(omitted->>'Key');
                        disposition := NULL;
                        -- A refusal is proved from the retained, excluded original parent
                        -- sources. No caller supplies a skipped/successful completion flag.
                        IF (omitted->>'Kind')::integer = 0 AND (
                            (omitted->>'Scope')::integer IN (1,4)
                                AND admitted.image->'System'->(omitted->>'Key')->'Locked' = 'true'::jsonb
                            OR (omitted->>'Scope')::integer = 4
                                AND admitted.image->'Tenant'->(omitted->>'Key')->'Locked' = 'true'::jsonb) THEN
                            disposition := 'parent-locked';
                        ELSIF (omitted->>'Kind')::integer = 0 AND (omitted->>'Operation')::integer = 1
                            AND omitted->>'Key' = {Text(GovernanceSettingKeys.Organizations.VerificationRequired)}
                            AND omitted->>'Value' = 'false'
                            AND lower(admitted.image->'System'->{Text(GovernanceSettingKeys.Organizations.TenantCanOmitVerification)}->>'Value') IS DISTINCT FROM 'true' THEN
                            disposition := 'verification-ceiling';
                        ELSIF (omitted->>'Kind')::integer = 2 AND source_row = 'null'::jsonb THEN
                            disposition := 'already-absent';
                        ELSIF (omitted->>'Kind')::integer = 1 AND source_row = 'null'::jsonb THEN
                            disposition := 'missing-lock-target';
                        ELSIF (omitted->>'Kind')::integer = 1 AND row_group = 'Tenant'
                            AND source_row->'Locked' = omitted->'IsLocked' THEN
                            disposition := 'unchanged-tenant-lock';
                        END IF;
                        IF disposition IS NULL
                        THEN RAISE EXCEPTION 'ordinary_settings_omitted_native_effect' USING ERRCODE = '42501'; END IF;
                        admitted.completed := admitted.completed || pg_catalog.jsonb_build_object(omitted->>'Key',disposition);
                    END LOOP;
                    UPDATE {sql.Name(AttemptTable)} SET finished = true,completed = admitted.completed
                        WHERE attempt = admitted.attempt AND backend = admitted.backend;
                    """ : "")}
            END;
            """;
    }

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE pending_effect jsonb;
            BEGIN
                {Guard(context, enrollment)}
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                    OR TG_RELID NOT IN ({string.Join(",", Sources.Select(type => Text(sql.Table(type)) + "::regclass"))})
                THEN RAISE EXCEPTION 'ordinary_settings_foreign_source_trigger' USING ERRCODE = '55000'; END IF;
                SELECT pending INTO pending_effect FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid();
                IF FOUND AND (pending_effect IS NULL OR TG_OP = 'TRUNCATE')
                THEN RAISE EXCEPTION 'ordinary_settings_untracked_statement' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(SourceTable)} SET revision = revision + 1 WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'ordinary_settings_anchor_missing' USING ERRCODE = '55000'; END IF;
                RETURN NULL;
            END;
            """;
    }

    private static string RowBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string rows = string.Join("\n", Sources.Select(type => $"""
            WHEN {Text(sql.Table(type))}::regclass THEN
                row_group := {Text(Group(type))};
                IF TG_OP <> 'INSERT' THEN before_row := {Projection(context, type, "OLD")}; END IF;
                IF TG_OP <> 'DELETE' THEN after_row := {Projection(context, type, "NEW")}; END IF;
            """));
        return $"""
            DECLARE pending_effect jsonb; already_observed boolean; row_group text;
                before_row jsonb := 'null'::jsonb; after_row jsonb := 'null'::jsonb;
            BEGIN
                {Guard(context, enrollment)}
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW' OR TG_OP NOT IN ('INSERT','UPDATE','DELETE')
                THEN RAISE EXCEPTION 'ordinary_settings_foreign_row_trigger' USING ERRCODE = '55000'; END IF;
                CASE TG_RELID {rows}
                    ELSE RAISE EXCEPTION 'ordinary_settings_foreign_row_source' USING ERRCODE = '55000';
                END CASE;
                SELECT pending,observed INTO pending_effect,already_observed FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid();
                IF FOUND THEN
                    IF pending_effect IS NULL OR already_observed
                        OR pending_effect->>'Group' IS DISTINCT FROM row_group
                        OR pending_effect->'Before' IS DISTINCT FROM before_row OR pending_effect->'After' IS DISTINCT FROM after_row
                    THEN RAISE EXCEPTION 'ordinary_settings_untracked_row' USING ERRCODE = '42501'; END IF;
                    UPDATE {sql.Name(AttemptTable)} SET observed = true
                        WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid();
                END IF;
                RETURN NULL;
            END;
            """;
    }

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, string name) => name switch
    {
        "read" => ReadBody(context, enrollment),
        "acquire" => AcquireBody(context, enrollment),
        "write" => WriteBody(context, enrollment),
        "complete" => CompleteBody(context, enrollment, true),
        "verify" => CompleteBody(context, enrollment, false),
        "changed" => ChangedBody(context, enrollment),
        "row" => RowBody(context, enrollment),
        "tenant_changed" => TenantMembershipBody(context, enrollment, false),
        "tenant_truncated" => TenantMembershipBody(context, enrollment, true),
        "settlement" => SettlementBody(context, enrollment),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };
}
