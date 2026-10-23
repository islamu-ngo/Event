using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;

namespace Explore.Persistence.Security;

public static partial class PostgresManagedProviderProvisioningContract
{
    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool transaction = true) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.GlobalLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL
            {(transaction ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'human_provisioning_original_global_required' USING ERRCODE = '42501'; END IF;
        """;

    private static string RequestGuard => $$"""
        IF p_request IS NULL OR pg_catalog.jsonb_typeof(p_request) <> 'object'
            OR ARRAY(SELECT key FROM pg_catalog.jsonb_object_keys(p_request) key ORDER BY key COLLATE "C")
                IS DISTINCT FROM ARRAY(SELECT key FROM unnest(ARRAY[{{string.Join(",", typeof(Model.Request).GetProperties()
                    .Select(property => Text(property.Name)))}}]::text[]) key ORDER BY key COLLATE "C")
            OR pg_catalog.jsonb_typeof(p_request->'Input') <> 'object'
            OR pg_catalog.jsonb_typeof(p_request->'Seeds') <> 'object'
            OR ARRAY(SELECT key FROM pg_catalog.jsonb_object_keys(p_request->'Seeds') key ORDER BY key COLLATE "C")
                IS DISTINCT FROM ARRAY(SELECT key FROM unnest(ARRAY[{{string.Join(",", typeof(Model.Seeds).GetProperties()
                    .Select(property => Text(property.Name)))}}]::text[]) key ORDER BY key COLLATE "C")
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each_text(p_request->'Seeds') s
                WHERE s.value IS NULL OR s.value !~ '^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')
            OR (SELECT count(DISTINCT value) FROM pg_catalog.jsonb_each_text(p_request->'Seeds')) <> {{typeof(Model.Seeds).GetProperties().Length}}
            OR NOT COALESCE((p_request->>'Human')::uuid <> '00000000-0000-0000-0000-000000000000'::uuid,false)
            OR p_request->>'At' IS NULL OR p_request->>'At' IS DISTINCT FROM
                pg_catalog.to_char((p_request->>'At')::timestamptz AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US') || '0Z'
            OR NULLIF(p_request#>>'{Input,ProviderKey}','') IS NULL OR length(p_request#>>'{Input,ProviderKey}') > 255
            OR NULLIF(p_request#>>'{Input,ExternalSystem}','') IS NULL OR length(p_request#>>'{Input,ExternalSystem}') > 255
            OR NULLIF(p_request#>>'{Input,ExternalCustomerId}','') IS NULL OR length(p_request#>>'{Input,ExternalCustomerId}') > 500
            OR NULLIF(p_request#>>'{Input,TenantFullName}','') IS NULL OR length(p_request#>>'{Input,TenantFullName}') > 500
            OR NOT COALESCE(p_request#>>'{Input,TenantSlug}' ~ '^[a-z0-9-]+$',false)
            OR length(p_request#>>'{Input,TenantSlug}') > 500
            OR pg_catalog.jsonb_typeof(p_request#>'{Input,ActivateTenant}') <> 'boolean'
            OR (p_request#>>'{Input,LocalIdentity,LocalSubjectId}' IS NULL) = (p_request#>>'{Input,ExternalAdmin,Subject}' IS NULL)
            OR p_request->>'Account' IS NULL OR p_request->>'IdentitySystem' IS NULL OR p_request->>'Subject' IS NULL
            OR (p_request->>'Provider')::integer NOT IN (1,2,3,4)
            OR p_request#>>'{Input,LocalIdentity,LocalSubjectId}' IS NOT NULL AND (
                p_request->>'Provider' <> '4' OR p_request->>'IdentitySystem' <> 'local'
                OR p_request->>'Subject' IS DISTINCT FROM p_request#>>'{Input,LocalIdentity,LocalSubjectId}'
                OR p_request->>'Account' IS DISTINCT FROM p_request->>'Subject')
            OR p_request#>>'{Input,ExternalAdmin,Subject}' IS NOT NULL AND (
                p_request->>'Provider' = '4' OR p_request->>'Subject' IS DISTINCT FROM p_request#>>'{Input,ExternalAdmin,Subject}'
                OR NULLIF(p_request#>>'{Input,ExternalAdmin,Email}','') IS NULL
                OR pg_catalog.jsonb_typeof(p_request#>'{Input,ExternalAdmin,EmailVerified}') <> 'boolean'
                OR p_request->>'Provider' = '2' AND (p_request->>'IdentitySystem' <> 'atproto' OR p_request->>'Account' IS DISTINCT FROM p_request->>'Subject')
                OR p_request->>'Provider' IN ('1','3') AND (p_request->>'Account' NOT LIKE 'oidc:%'
                    OR p_request->>'IdentitySystem' NOT IN ('google','keycloak')
                    OR p_request->>'Provider' = '3' AND p_request->>'IdentitySystem' <> 'google'))
            OR p_request#>>'{Input,Organizer,Kind}' IS NOT NULL AND p_request#>>'{Input,Organizer,Kind}' NOT IN ('1','2')
            OR p_request->>'BrandingKey' <> {{Text(SettingsDocumentKeys.Tenant.Branding)}}
            OR p_request->>'BrandingSchema' <> '{{TenantBrandingSettingsDocumentDefaults.SchemaVersion}}'
            OR p_request->>'BrandingVersion' <> {{Text(TenantBrandingSettingsDocumentDefaults.DefaultsVersion)}}
            OR p_request->'BrandingPayload' IS DISTINCT FROM pg_catalog.jsonb_build_object(
                'displayName',p_request#>'{Input,TenantFullName}','logoUrl',NULL,'faviconUrl',NULL,'customCssUrl',NULL)
            OR p_request->>'DirectoryKey' <> {{Text(SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity)}}
            OR p_request->>'DirectorySchema' <> '{{TenantDirectoryOperatorIdentityDocumentDefaults.SchemaVersion}}'
            OR p_request->>'DirectoryVersion' <> {{Text(TenantDirectoryOperatorIdentityDocumentDefaults.DefaultsVersion)}}
            OR pg_catalog.jsonb_typeof(p_request->'DirectoryPayload') <> 'object'
            OR (p_request#>>'{Input,ActivateTenant}')::boolean AND (
                NULLIF(p_request#>>'{DirectoryPayload,publicName}','') IS NULL
                OR NULLIF(p_request#>>'{DirectoryPayload,legalName}','') IS NULL
                OR NOT COALESCE(p_request#>>'{DirectoryPayload,operatorKindCode}' IN
                    ('registered_organization','sole_trader','individual','public_body','unincorporated_association'),false)
                OR NOT COALESCE(p_request#>>'{DirectoryPayload,jurisdictionCountryCode}' ~ '^[A-Z]{2}$',false)
                OR NULLIF(p_request#>>'{DirectoryPayload,publicContactEmail}','') IS NULL
                OR NOT COALESCE(p_request#>>'{DirectoryPayload,legalNoticeUrl}' LIKE 'https://%',false)
                OR NOT COALESCE(p_request#>>'{DirectoryPayload,privacyUrl}' LIKE 'https://%',false))
        THEN RAISE EXCEPTION 'human_provisioning_closed_request_required' USING ERRCODE = '42501'; END IF;
        """;

    private static string ReadBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new ManagedProviderProvisioningSql(context, true);
        return $"""
            DECLARE data jsonb; epoch bigint; mode integer; active bigint; reserved bigint;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment, false)}
                IF EXISTS (SELECT 1 FROM {sql.Names.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() AND completed)
                THEN RAISE EXCEPTION 'human_provisioning_already_completed' USING ERRCODE = '42501'; END IF;
                SELECT revision INTO STRICT epoch FROM {sql.Names.Name(SourceTable)} WHERE id = 1;
                SELECT capacity.mode,capacity.active,capacity.reserved INTO mode,active,reserved FROM ({sql.CapacitySelect}) capacity(mode,active,reserved);
                data := {sql.JsonProjection} || pg_catalog.jsonb_build_object('Capacity',pg_catalog.jsonb_build_object(
                    'DeploymentMode',mode,'ActiveTenants',active,'ReservedTenants',reserved));
                IF epoch IS DISTINCT FROM (SELECT revision FROM {sql.Names.Name(SourceTable)} WHERE id = 1)
                THEN RAISE EXCEPTION 'human_provisioning_observation_changed' USING ERRCODE = '40001'; END IF;
                RETURN pg_catalog.jsonb_build_object('Data',data,'Revision',epoch,'Request',p_request,
                    'Backend',pg_catalog.pg_backend_pid(),'Login',session_user);
            END;
            """;
    }

    private static string AcquireBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE observed jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                PERFORM pg_catalog.pg_advisory_xact_lock({RelationalNamedLock.ComputeStableKey("discovery-tenant-enrollment")});
                UPDATE {sql.Name(SourceTable)} SET revision = revision WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'human_provisioning_epoch_missing' USING ERRCODE = '55000'; END IF;
                observed := {FunctionSql(context, "read")}(p_request);
                IF observed IS DISTINCT FROM p_before
                THEN RAISE EXCEPTION 'human_provisioning_preimage_changed' USING ERRCODE = '40001'; END IF;
                INSERT INTO {sql.Name(AttemptTable)} (attempt,backend,request,preimage,plan,expected,applied,completed)
                    VALUES (pg_catalog.txid_current(),pg_catalog.pg_backend_pid(),p_request,observed,NULL,NULL,false,false);
                RETURN observed;
            END;
            """;
    }

    private static string LoadAttempt(ExploreDbContext context) => $"""
        SELECT * INTO admitted FROM {new HumanPrincipalSourceSql(context).Name(AttemptTable)}
            WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() FOR UPDATE;
        IF NOT FOUND OR admitted.request IS DISTINCT FROM p_request OR admitted.completed
        THEN RAISE EXCEPTION 'human_provisioning_original_attempt_required' USING ERRCODE = '42501'; END IF;
        """;

    private static string CompileBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; compiled jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                {LoadAttempt(context)}
                IF admitted.plan IS NOT NULL OR admitted.applied
                    OR {FunctionSql(context, "read")}(p_request) IS DISTINCT FROM admitted.preimage
                THEN RAISE EXCEPTION 'human_provisioning_compile_once_unchanged_required' USING ERRCODE = '40001'; END IF;
                compiled := {FunctionSql(context, "program")}(p_request,admitted.preimage->'Data');
                UPDATE {sql.Name(AttemptTable)} SET plan = compiled->'Plan',expected = compiled->'Data'
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN compiled - 'Plan';
            END;
            """;
    }

    private static string ApplyBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new ManagedProviderProvisioningSql(context, true);
        string cases = string.Join("\n", Model.Inventory.Where(relation => relation.Insert).Select(relation =>
        {
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", relation.Columns.Select(column =>
                Text(sql.Names.ColumnName(relation.Type, column)) + ",effect->'After'->" + Text(column))) + ")";
            string predicate = "r." + sql.Column(relation.Type, relation.Key) + " = (effect->'After'->>" + Text(relation.Key) + ")::uuid";
            string update = relation.Update.Length == 0 ? "RAISE EXCEPTION 'human_provisioning_insert_only' USING ERRCODE = '42501';"
                : $"UPDATE {sql.Table(relation.Type)} r SET {string.Join(",", relation.Update.Select(column =>
                    sql.Column(relation.Type, column) + " = n." + sql.Column(relation.Type, column)))} "
                    + $"FROM pg_catalog.jsonb_populate_record(NULL::{sql.Table(relation.Type)},{mapped}) n WHERE {predicate};";
            return $"""
                WHEN {Text(relation.Type.Name)} THEN
                    SELECT {sql.Projection(relation)} INTO stored FROM {sql.Table(relation.Type)} r WHERE {predicate};
                    IF stored IS DISTINCT FROM NULLIF(effect->'Before','null'::jsonb)
                    THEN RAISE EXCEPTION 'human_provisioning_row_preimage_changed' USING ERRCODE = '40001'; END IF;
                    IF stored IS NULL THEN
                        INSERT INTO {sql.Table(relation.Type)} ({string.Join(",", relation.Columns.Select(column => sql.Column(relation.Type, column)))})
                            SELECT {string.Join(",", relation.Columns.Select(column => "n." + sql.Column(relation.Type, column)))}
                            FROM pg_catalog.jsonb_populate_record(NULL::{sql.Table(relation.Type)},{mapped}) n;
                    ELSE {update} END IF;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN RAISE EXCEPTION 'human_provisioning_effect_cardinality' USING ERRCODE = '40001'; END IF;
                """;
        }));
        return $"""
            DECLARE admitted {sql.Names.Name(AttemptTable)}%ROWTYPE; effect jsonb; stored jsonb; observed jsonb; affected bigint;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                {LoadAttempt(context)}
                IF admitted.plan IS NULL OR admitted.applied
                    OR {FunctionSql(context, "read")}(p_request) IS DISTINCT FROM admitted.preimage
                THEN RAISE EXCEPTION 'human_provisioning_sealed_plan_required' USING ERRCODE = '40001'; END IF;
                FOR effect IN SELECT value FROM pg_catalog.jsonb_array_elements(admitted.plan) LOOP
                    IF effect->>'Type' = 'EventDiscoveryRevision' THEN
                        PERFORM pg_catalog.pg_advisory_xact_lock(
                            ('x' || substr(pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(
                                'discovery-epoch:' || replace(p_request#>>ARRAY['Seeds','Tenant'],'-',''),'UTF8')),'hex'),1,16))::bit(64)::bigint);
                    END IF;
                    CASE effect->>'Type'
                        {cases}
                        ELSE RAISE EXCEPTION 'human_provisioning_private_plan_inventory_drift' USING ERRCODE = '55000';
                    END CASE;
                END LOOP;
                observed := {FunctionSql(context, "read")}(p_request);
                IF observed->'Data' IS DISTINCT FROM admitted.expected
                    OR (observed->>'Revision')::bigint <> (admitted.preimage->>'Revision')::bigint + pg_catalog.jsonb_array_length(admitted.plan)
                THEN RAISE EXCEPTION 'human_provisioning_full_success_image_mismatch' USING ERRCODE = '40001'; END IF;
                UPDATE {sql.Names.Name(AttemptTable)} SET applied = true WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN observed;
            END;
            """;
    }

    private static string CompleteBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; observed jsonb;
            BEGIN
                {Guard(context, enrollment)}
                {RequestGuard}
                {Caller(enrollment)}
                {LoadAttempt(context)}
                IF NOT admitted.applied OR admitted.plan IS NULL
                THEN RAISE EXCEPTION 'human_provisioning_apply_required' USING ERRCODE = '42501'; END IF;
                observed := {FunctionSql(context, "read")}(p_request);
                IF observed->'Data' IS DISTINCT FROM admitted.expected
                    OR (observed->>'Revision')::bigint <> (admitted.preimage->>'Revision')::bigint + pg_catalog.jsonb_array_length(admitted.plan)
                THEN RAISE EXCEPTION 'human_provisioning_terminal_image_changed' USING ERRCODE = '40001'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET completed = true,request = pg_catalog.jsonb_build_object(),preimage = pg_catalog.jsonb_build_object(),
                    plan = '[]'::jsonb,expected = pg_catalog.jsonb_build_object() WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN observed;
            END;
            """;
    }

    private static string SettlementBody(ExploreDbContext context) => $"""
        BEGIN
            IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW' OR TG_OP <> 'INSERT'
                OR TG_RELID <> {Text(new HumanPrincipalSourceSql(context).Name(AttemptTable))}::regclass
                OR NEW.attempt <> pg_catalog.txid_current() OR NEW.backend <> pg_catalog.pg_backend_pid()
                OR NOT EXISTS (SELECT 1 FROM {new HumanPrincipalSourceSql(context).Name(AttemptTable)} a
                    WHERE a.attempt = NEW.attempt AND a.backend = NEW.backend AND a.applied AND a.completed
                        AND a.request = pg_catalog.jsonb_build_object() AND a.preimage = pg_catalog.jsonb_build_object()
                        AND a.plan = '[]'::jsonb AND a.expected = pg_catalog.jsonb_build_object())
            THEN RAISE EXCEPTION 'human_provisioning_physical_seal_missing' USING ERRCODE = '42501'; END IF;
            RETURN NULL;
        END;
        """;
}
