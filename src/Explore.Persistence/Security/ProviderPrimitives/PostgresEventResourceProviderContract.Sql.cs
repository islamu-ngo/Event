using Explore.Application.Settings;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresEventResourceProviderContract
{
    private static string Projection(ExploreDbContext context, Type type, string alias)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return "pg_catalog.jsonb_build_object(" + string.Join(",", context.Model.FindEntityType(type)!.GetProperties().Select(property =>
        {
            string value = $"{alias}.{sql.Column(type, property.Name)}";
            if (property.Name is "CreatedAt" or "UpdatedAt")
                value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0Z'";
            if (property.Name == "AllowedValues") value += "::text";
            return $"{Text(property.Name)},{value}";
        })) + ")";
    }

    private static string DataSql(ExploreDbContext context)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return "pg_catalog.jsonb_build_object(" + string.Join(",", Sources.Select(type =>
            $"{Text(type.Name)},(SELECT COALESCE(pg_catalog.jsonb_agg({Projection(context, type, "r")} ORDER BY r.{sql.Column(type, "Id")}), '[]'::jsonb) FROM {sql.Table(type)} r)")) + ")";
    }

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool attempt) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}' OR session_user <> {Text(enrollment.GlobalLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL
            OR pg_catalog.pg_has_role(session_user,pg_catalog.to_regrole({Text(enrollment.MigratorLogin)}),'SET')
            OR pg_catalog.pg_has_role(session_user,pg_catalog.to_regrole({Text(enrollment.MigratorLogin)}),'USAGE')
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'SET') OR pg_catalog.pg_has_role(session_user,r.oid,'USAGE'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
            {(attempt ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
        THEN RAISE EXCEPTION 'resource_provider_original_global' USING ERRCODE = '42501'; END IF;
        """;

    private static string AttemptRead(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
        {Guard(context, enrollment)}
        {Caller(enrollment, true)}
        SELECT * INTO admitted FROM {new HumanPrincipalSourceSql(context).Name(AttemptTable)}
            WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid() FOR UPDATE;
        IF NOT FOUND OR admitted.finished OR admitted.request IS DISTINCT FROM p_request
        THEN RAISE EXCEPTION 'resource_provider_original_attempt' USING ERRCODE = '42501'; END IF;
        """;

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, string name)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return name switch
        {
            "read" => $"""
                DECLARE generation bigint; data jsonb;
                BEGIN
                    {Guard(context, enrollment)}
                    {Caller(enrollment, false)}
                    SELECT revision INTO generation FROM {sql.Name(SourceTable)} WHERE id = 1;
                    IF generation IS NULL OR generation < 0 THEN RAISE EXCEPTION 'resource_provider_anchor_missing' USING ERRCODE = '55000'; END IF;
                    data := {DataSql(context)};
                    IF generation IS DISTINCT FROM (SELECT revision FROM {sql.Name(SourceTable)} WHERE id = 1)
                    THEN RAISE EXCEPTION 'resource_provider_observation_changed' USING ERRCODE = '40001'; END IF;
                    RETURN pg_catalog.jsonb_build_object('Data',data,'Revision',generation,'Backend',pg_catalog.pg_backend_pid(),
                        'Database',pg_catalog.current_database(),'Namespace',{Text(sql.Schema)},'Login',session_user);
                END;
                """,
            "acquire" => $"""
                DECLARE observed jsonb;
                BEGIN
                    {Guard(context, enrollment)}
                    {Caller(enrollment, true)}
                    IF pg_catalog.jsonb_typeof(p_request) <> 'object'
                        OR (p_request->>'Actor')::uuid IS NULL OR (p_request->>'Actor')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                        OR ((p_request->'Bind' <> 'null'::jsonb)::integer + (p_request->'Begin' <> 'null'::jsonb)::integer
                            + (p_request->'Activate' <> 'null'::jsonb)::integer + (p_request->>'Publication')::boolean::integer) IS DISTINCT FROM 1
                        OR p_request->>'OccurredAt' !~ {Text(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,6}|\.\d{6}0)?Z$")}
                        OR (p_request->>'OccurredAt')::timestamptz IS NULL
                        OR EXISTS (SELECT 1 FROM unnest(ARRAY['OperationId','Revision','SettingId','ConcurrencyStamp']) key
                            WHERE NOT COALESCE(p_request->>key ~ {Text("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")},false))
                    THEN RAISE EXCEPTION 'resource_provider_closed_original' USING ERRCODE = '42501'; END IF;
                    LOCK TABLE {string.Join(",", WriteSources.Select(sql.Table))} IN SHARE ROW EXCLUSIVE MODE NOWAIT;
                    UPDATE {sql.Name(SourceTable)} SET revision = revision WHERE id = 1;
                    IF NOT FOUND THEN RAISE EXCEPTION 'resource_provider_anchor_missing' USING ERRCODE = '55000'; END IF;
                    observed := {FunctionSql(context, "read")}();
                    IF observed IS DISTINCT FROM p_before
                    THEN RAISE EXCEPTION 'resource_provider_original_data_changed' USING ERRCODE = '40001'; END IF;
                    INSERT INTO {sql.Name(AttemptTable)} (attempt,backend,request,image,applied,finished,pending,observed)
                        VALUES (pg_catalog.txid_current(),pg_catalog.pg_backend_pid(),p_request,p_before->'Data',false,false,NULL,false);
                END;
                """,
            "apply" => ApplyBody(context, enrollment),
            "verify" or "complete" => $"""
                DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE;
                BEGIN
                    {AttemptRead(context, enrollment)}
                    IF admitted.pending IS NOT NULL OR admitted.observed
                        OR admitted.image IS DISTINCT FROM p_expected OR admitted.image IS DISTINCT FROM {DataSql(context)}
                        {(name == "complete" ? "OR NOT admitted.applied" : "")}
                    THEN RAISE EXCEPTION 'resource_provider_incomplete_original' USING ERRCODE = '42501'; END IF;
                    {(name == "complete" ? $"UPDATE {sql.Name(AttemptTable)} SET finished = true WHERE attempt = admitted.attempt AND backend = admitted.backend;" : "")}
                END;
                """,
            "changed" => $"""
                DECLARE pending_effect jsonb;
                BEGIN
                    {Guard(context, enrollment)}
                    IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'BEFORE' OR TG_LEVEL <> 'STATEMENT'
                        OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE')
                        OR TG_RELID NOT IN ({string.Join(",", Sources.Select(type => Text(sql.Table(type)) + "::regclass"))})
                    THEN RAISE EXCEPTION 'resource_provider_source_trigger' USING ERRCODE = '55000'; END IF;
                    SELECT pending INTO pending_effect FROM {sql.Name(AttemptTable)}
                        WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid();
                    IF FOUND AND (pending_effect IS NULL OR TG_OP = 'TRUNCATE')
                    THEN RAISE EXCEPTION 'resource_provider_untracked_statement' USING ERRCODE = '42501'; END IF;
                    UPDATE {sql.Name(SourceTable)} SET revision = revision + 1 WHERE id = 1;
                    IF NOT FOUND THEN RAISE EXCEPTION 'resource_provider_anchor_missing' USING ERRCODE = '55000'; END IF;
                    RETURN NULL;
                END;
                """,
            "row" => RowBody(context, enrollment),
            "settlement" => $"""
                BEGIN
                    IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW'
                        OR TG_OP NOT IN ('INSERT','UPDATE') OR TG_TABLE_SCHEMA <> {Text(sql.Schema)} OR TG_TABLE_NAME <> '{AttemptTable}'
                    THEN RAISE EXCEPTION 'resource_provider_settlement_target' USING ERRCODE = '55000'; END IF;
                    IF NOT EXISTS (SELECT 1 FROM {sql.Name(AttemptTable)}
                        WHERE attempt = NEW.attempt AND backend = NEW.backend AND applied AND finished AND pending IS NULL AND NOT observed)
                    THEN RAISE EXCEPTION 'resource_provider_missing_completion' USING ERRCODE = '42501'; END IF;
                    UPDATE {sql.Name(AttemptTable)} SET request = pg_catalog.jsonb_build_object(),image = pg_catalog.jsonb_build_object()
                        WHERE attempt = NEW.attempt AND backend = NEW.backend AND request <> pg_catalog.jsonb_build_object();
                    RETURN NULL;
                END;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    private static string ApplyBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string key = Text(EventResourceProviderBindingDocument.SettingKey);
        string writes = string.Join("\n", WriteSources.Select(type =>
        {
            string table = sql.Table(type);
            string properties = string.Join(",", context.Model.FindEntityType(type)!.GetProperties().Select(property => sql.Column(type, property.Name)));
            string mapped = "pg_catalog.jsonb_build_object(" + string.Join(",", context.Model.FindEntityType(type)!.GetProperties()
                .Select(property => $"{Text(sql.ColumnName(type, property.Name))},after_row->{Text(property.Name)}")) + ")";
            string assignments = string.Join(",", context.Model.FindEntityType(type)!.GetProperties()
                .Where(property => property.Name != "Id").Select(property => $"{sql.Column(type, property.Name)} = n.{sql.Column(type, property.Name)}"));
            return $"""
                before_row := {(type == typeof(SystemSetting) ? "setting" : "activation")};
                after_row := {(type == typeof(SystemSetting) ? "next_setting" : "next_activation")};
                UPDATE {sql.Name(AttemptTable)} SET pending = pg_catalog.jsonb_build_object(
                    'Type',{Text(type.Name)},'Before',before_row,'After',after_row),observed = false
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                IF before_row IS NULL THEN
                    INSERT INTO {table} ({properties}) SELECT {string.Join(",", context.Model.FindEntityType(type)!.GetProperties().Select(property => "n." + sql.Column(type, property.Name)))}
                        FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n;
                ELSE
                    UPDATE {table} SET {assignments} FROM pg_catalog.jsonb_populate_record(NULL::{table},{mapped}) n
                        WHERE {table}.{sql.Column(type, "Id")} = (before_row->>'Id')::uuid;
                END IF;
                IF NOT (SELECT observed FROM {sql.Name(AttemptTable)} WHERE attempt = admitted.attempt AND backend = admitted.backend)
                THEN RAISE EXCEPTION 'resource_provider_missing_pair_effect' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET pending = NULL,observed = false WHERE attempt = admitted.attempt AND backend = admitted.backend;
                """;
        }));
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE; setting jsonb; activation jsonb; document jsonb;
                binding jsonb; previous jsonb; deployment uuid; next_document jsonb; next_setting jsonb; next_activation jsonb;
                canonical jsonb; before_row jsonb; after_row jsonb; no_change boolean := false; state integer; instant text;
            BEGIN
                {AttemptRead(context, enrollment)}
                IF admitted.applied OR admitted.pending IS NOT NULL OR admitted.image IS DISTINCT FROM {DataSql(context)}
                THEN RAISE EXCEPTION 'resource_provider_replayed_action' USING ERRCODE = '42501'; END IF;
                instant := pg_catalog.to_char((p_request->>'OccurredAt')::timestamptz AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS.US') || '0Z';
                SELECT value INTO setting FROM pg_catalog.jsonb_array_elements(admitted.image->'SystemSetting')
                    WHERE value->>'SettingKey' = {key} COLLATE "C";
                document := COALESCE((setting->>'Value')::jsonb,
                    pg_catalog.jsonb_build_object('revision','00000000-0000-0000-0000-000000000000','deployments','[]'::jsonb));
                IF pg_catalog.jsonb_typeof(document->'deployments') IS DISTINCT FROM 'array'
                    OR document->>'revision' IS NULL
                    OR setting IS NOT NULL AND document->>'revision' !~ {Text("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")}
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(document->'deployments') d
                        WHERE pg_catalog.jsonb_typeof(d->'endpoints') IS DISTINCT FROM 'array' OR pg_catalog.jsonb_array_length(d->'endpoints') = 0
                            OR d->>'scope' IS NULL OR length(d->>'scope') > 256 OR btrim(d->>'scope') <> d->>'scope'
                            OR NULLIF(btrim(d->>'policyVersion'),'') IS NULL OR length(d->>'policyVersion') > 128
                            OR btrim(d->>'policyVersion') <> d->>'policyVersion'
                            OR NOT COALESCE(d->>'deploymentId' ~ {Text("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")},false))
                    OR (SELECT count(*) FROM pg_catalog.jsonb_array_elements(document->'deployments'))
                        <> (SELECT count(DISTINCT d->>'deploymentId') FROM pg_catalog.jsonb_array_elements(document->'deployments') d)
                    OR (SELECT count(*) FROM pg_catalog.jsonb_array_elements(document->'deployments') d,
                            pg_catalog.jsonb_array_elements_text(d->'endpoints') e)
                        <> (SELECT count(DISTINCT e) FROM pg_catalog.jsonb_array_elements(document->'deployments') d,
                            pg_catalog.jsonb_array_elements_text(d->'endpoints') e)
                    OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(document->'deployments') d,
                        pg_catalog.jsonb_array_elements_text(d->'endpoints') e
                        WHERE e !~ '^https?://[^/@?#[:space:]]+$' OR e <> lower(e))
                THEN RAISE EXCEPTION 'resource_provider_binding_document' USING ERRCODE = '42501'; END IF;
                IF p_request->'Bind' <> 'null'::jsonb THEN
                    IF (p_request->'Bind'->>'ExpectedRevision')::uuid IS DISTINCT FROM (document->>'revision')::uuid
                    THEN RAISE EXCEPTION 'resource_provider_binding_revision' USING ERRCODE = '40001'; END IF;
                    binding := pg_catalog.jsonb_build_object('deploymentId',p_request->'Bind'->'Binding'->'DeploymentId',
                        'endpoints',p_request->'Bind'->'Binding'->'Endpoints','scope',p_request->'Bind'->'Binding'->'Scope',
                        'policyVersion',p_request->'Bind'->'Binding'->'PolicyVersion');
                    deployment := (binding->>'deploymentId')::uuid;
                    SELECT d INTO previous FROM pg_catalog.jsonb_array_elements(document->'deployments') d WHERE (d->>'deploymentId')::uuid = deployment;
                    IF NOT COALESCE(binding->>'deploymentId' ~ {Text("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")},false)
                        OR pg_catalog.jsonb_typeof(binding->'endpoints') IS DISTINCT FROM 'array' OR pg_catalog.jsonb_array_length(binding->'endpoints') = 0
                        OR binding->>'scope' IS NULL OR length(binding->>'scope') > 256 OR btrim(binding->>'scope') <> binding->>'scope'
                        OR NULLIF(btrim(binding->>'policyVersion'),'') IS NULL OR length(binding->>'policyVersion') > 128
                        OR btrim(binding->>'policyVersion') <> binding->>'policyVersion'
                        OR (SELECT count(*) FROM pg_catalog.jsonb_array_elements(binding->'endpoints'))
                            <> (SELECT count(DISTINCT e) FROM pg_catalog.jsonb_array_elements(binding->'endpoints') e)
                        OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements_text(binding->'endpoints') e
                            WHERE e !~ '^https?://[^/@?#[:space:]]+$' OR e <> lower(e))
                        OR previous IS NOT NULL AND NOT ((binding->'endpoints') @> (previous->'endpoints'))
                        OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(document->'deployments') d,
                            pg_catalog.jsonb_array_elements_text(d->'endpoints') e
                            WHERE (d->>'deploymentId')::uuid <> deployment AND binding->'endpoints' ? e)
                    THEN RAISE EXCEPTION 'resource_provider_binding_aliases' USING ERRCODE = '42501'; END IF;
                    next_document := pg_catalog.jsonb_build_object('revision',p_request->'Revision','deployments',
                        (SELECT COALESCE(pg_catalog.jsonb_agg(d ORDER BY ord),'[]'::jsonb)
                            FROM pg_catalog.jsonb_array_elements(document->'deployments') WITH ORDINALITY rows(d,ord)
                            WHERE (d->>'deploymentId')::uuid <> deployment) || pg_catalog.jsonb_build_array(binding));
                ELSE
                    IF (p_request->>'Publication')::boolean THEN
                        SELECT d INTO binding FROM pg_catalog.jsonb_array_elements(document->'deployments') d
                            WHERE d->'endpoints' ? (p_request->>'PublicationEndpoint');
                        no_change := binding IS NULL;
                        deployment := (binding->>'deploymentId')::uuid;
                    ELSE
                        deployment := COALESCE(p_request->'Begin'->>'DeploymentId',p_request->'Activate'->>'DeploymentId')::uuid;
                        SELECT d INTO binding FROM pg_catalog.jsonb_array_elements(document->'deployments') d WHERE (d->>'deploymentId')::uuid = deployment;
                        IF binding IS NULL THEN RAISE EXCEPTION 'resource_provider_unbound_deployment' USING ERRCODE = '42501'; END IF;
                    END IF;
                    next_document := CASE WHEN p_request->'Activate' <> 'null'::jsonb THEN document
                        ELSE document || pg_catalog.jsonb_build_object('revision',p_request->'Revision') END;
                END IF;
                IF NOT no_change THEN
                    SELECT a INTO activation FROM pg_catalog.jsonb_array_elements(admitted.image->'EventResourceProviderActivation') a
                        WHERE (a->>'Id')::uuid = deployment;
                    IF activation IS NULL AND p_request->'Bind' = 'null'::jsonb
                    THEN RAISE EXCEPTION 'resource_provider_activation_missing' USING ERRCODE = '42501'; END IF;
                    IF p_request->'Activate' <> 'null'::jsonb THEN
                        no_change := (activation->>'CurrentOperationId')::uuid IS DISTINCT FROM (p_request->'Activate'->>'OperationId')::uuid
                            OR (activation->>'Epoch')::bigint IS DISTINCT FROM (p_request->'Activate'->>'Epoch')::bigint
                            OR (activation->>'State')::integer <> 1 OR (p_request->'Activate'->>'Epoch')::bigint <= 0;
                        state := CASE WHEN (p_request->'Activate'->>'PreviousWritersStopped')::boolean
                            AND (p_request->'Activate'->>'FrozenParentPolicyContractConfirmed')::boolean
                            AND p_request->'Activate'->>'Scope' = binding->>'scope'
                            AND p_request->'Activate'->>'PolicyVersion' = binding->>'policyVersion'
                            AND (p_request->'Activate'->>'ReachableReplicaCount')::integer > 0
                            AND p_request->'Activate'->>'ReachableReplicaCount' = p_request->'Activate'->>'DeclaredPolicyReplicaCount' THEN 2 ELSE 3 END;
                        next_activation := activation || pg_catalog.jsonb_build_object('State',state);
                    ELSE
                        next_activation := COALESCE(activation,pg_catalog.jsonb_build_object('Id',deployment,'CreatedAt',instant,'CreatedBy',p_request->'Actor'))
                            || pg_catalog.jsonb_build_object('Epoch',COALESCE((activation->>'Epoch')::bigint,0) + 1,
                                'CurrentOperationId',p_request->'OperationId','State',1);
                    END IF;
                END IF;
                IF no_change THEN
                    IF p_expected IS DISTINCT FROM admitted.image
                    THEN RAISE EXCEPTION 'resource_provider_false_noop' USING ERRCODE = '42501'; END IF;
                ELSE
                    IF activation IS NOT NULL AND (p_request->>'OccurredAt')::timestamptz < COALESCE((activation->>'UpdatedAt')::timestamptz,(activation->>'CreatedAt')::timestamptz)
                    THEN RAISE EXCEPTION 'resource_provider_backwards_time' USING ERRCODE = '42501'; END IF;
                    next_activation := next_activation || pg_catalog.jsonb_build_object('UpdatedAt',instant,'UpdatedBy',p_request->'Actor','ConcurrencyStamp',p_request->'ConcurrencyStamp');
                    SELECT s INTO next_setting FROM pg_catalog.jsonb_array_elements(p_expected->'SystemSetting') s WHERE s->>'SettingKey' = {key};
                    IF (next_setting->>'Value')::jsonb IS DISTINCT FROM next_document
                    THEN RAISE EXCEPTION 'resource_provider_document_effect' USING ERRCODE = '42501'; END IF;
                    canonical := CASE WHEN p_request->'Activate' <> 'null'::jsonb THEN setting
                        ELSE COALESCE(setting,pg_catalog.jsonb_build_object('Id',p_request->'SettingId','SettingKey',{key},'SettingValueTypeId',{(int)SettingValueType.Json},
                        'Category','Cerbos','Description',NULL,'AllowedValues',NULL,'DisplayOrder',0,'CreatedAt',instant,'CreatedBy',p_request->'Actor'))
                        || pg_catalog.jsonb_build_object('Value',next_setting->'Value','IsLocked',true,'UpdatedAt',instant,'UpdatedBy',p_request->'Actor') END;
                    IF next_setting IS DISTINCT FROM canonical THEN RAISE EXCEPTION 'resource_provider_setting_effect' USING ERRCODE = '42501'; END IF;
                    canonical := pg_catalog.jsonb_build_object('TenantSetting',admitted.image->'TenantSetting','SystemSetting',
                        (SELECT pg_catalog.jsonb_agg(s ORDER BY (s->>'Id')::uuid) FROM (
                            SELECT s FROM pg_catalog.jsonb_array_elements(admitted.image->'SystemSetting') s WHERE s->>'SettingKey' <> {key}
                            UNION ALL SELECT next_setting) rows(s)), 'EventResourceProviderActivation',
                        (SELECT pg_catalog.jsonb_agg(a ORDER BY (a->>'Id')::uuid) FROM (
                            SELECT a FROM pg_catalog.jsonb_array_elements(admitted.image->'EventResourceProviderActivation') a WHERE (a->>'Id')::uuid <> deployment
                            UNION ALL SELECT next_activation) rows(a)));
                    IF canonical IS DISTINCT FROM p_expected THEN RAISE EXCEPTION 'resource_provider_pair_effect' USING ERRCODE = '42501'; END IF;
                    {writes}
                END IF;
                IF {DataSql(context)} IS DISTINCT FROM p_expected THEN RAISE EXCEPTION 'resource_provider_pair_not_written' USING ERRCODE = '42501'; END IF;
                UPDATE {sql.Name(AttemptTable)} SET image = p_expected,applied = true WHERE attempt = admitted.attempt AND backend = admitted.backend;
            END;
            """;
    }

    private static string RowBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE pending_effect jsonb; seen boolean; kind text; before_row jsonb := 'null'::jsonb; after_row jsonb := 'null'::jsonb;
            BEGIN
                {Guard(context, enrollment)}
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW' OR TG_OP NOT IN ('INSERT','UPDATE','DELETE')
                THEN RAISE EXCEPTION 'resource_provider_row_trigger' USING ERRCODE = '55000'; END IF;
                CASE TG_RELID
                    {string.Join("\n", Sources.Select(type => $"""
                        WHEN {Text(sql.Table(type))}::regclass THEN kind := {Text(type.Name)};
                            IF TG_OP <> 'INSERT' THEN before_row := {Projection(context, type, "OLD")}; END IF;
                            IF TG_OP <> 'DELETE' THEN after_row := {Projection(context, type, "NEW")}; END IF;
                        """))}
                    ELSE RAISE EXCEPTION 'resource_provider_foreign_row' USING ERRCODE = '55000';
                END CASE;
                SELECT pending,observed INTO pending_effect,seen FROM {sql.Name(AttemptTable)}
                    WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid();
                IF FOUND THEN
                    IF pending_effect IS NULL OR seen OR pending_effect->>'Type' IS DISTINCT FROM kind
                        OR pending_effect->'Before' IS DISTINCT FROM before_row OR pending_effect->'After' IS DISTINCT FROM after_row
                    THEN RAISE EXCEPTION 'resource_provider_untracked_row' USING ERRCODE = '42501'; END IF;
                    UPDATE {sql.Name(AttemptTable)} SET observed = true WHERE attempt = pg_catalog.txid_current() AND backend = pg_catalog.pg_backend_pid();
                END IF;
                RETURN NULL;
            END;
            """;
    }
}
