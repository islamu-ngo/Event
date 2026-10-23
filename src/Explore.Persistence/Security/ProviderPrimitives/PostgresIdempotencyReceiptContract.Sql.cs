using Explore.Domain;

namespace Explore.Persistence.Security;

public static partial class PostgresIdempotencyReceiptContract
{
    private static readonly string[] ReceiptFields =
    [
        "Id", "Key", "TenantId", "UserId", "RequestMethod", "RequestTarget", "RequestContentType",
        "RequestBodyHash", "PrincipalFingerprint", "StatusCode", "ResponseBody", "ContentType", "CreatedAt", "ExpiresAt"
    ];
    private static readonly string[] RequestFields =
    [
        "TenantId", "Key", "UserId", "RequestMethod", "RequestTarget", "RequestContentType",
        "RequestBodyHash", "PrincipalFingerprint"
    ];

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool discovery,
        string tenant = "p_tenant", bool definer = true) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> {(definer ? Text(discovery ? DiscoveryOwnerRole : OwnerRole) : "session_user::text")}
            OR session_user <> {Text(discovery ? enrollment.GlobalLogin : enrollment.RuntimeLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR {(discovery ? "NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL"
                : $"{tenant} IS NULL OR {tenant} = '00000000-0000-0000-0000-000000000000'::uuid"
                    + $" OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM {tenant}")}
            OR pg_catalog.pg_has_role(session_user,pg_catalog.to_regrole({Text(enrollment.MigratorLogin)}),'SET')
            OR pg_catalog.pg_has_role(session_user,pg_catalog.to_regrole({Text(enrollment.MigratorLogin)}),'USAGE')
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles elevated
                WHERE (pg_catalog.pg_has_role(session_user,elevated.oid,'SET')
                    OR pg_catalog.pg_has_role(session_user,elevated.oid,'USAGE'))
                    AND (elevated.rolsuper OR elevated.rolbypassrls OR elevated.rolcreatedb
                        OR elevated.rolcreaterole OR elevated.rolreplication))
        THEN RAISE EXCEPTION 'idempotency_receipt_original_service' USING ERRCODE = '42501'; END IF;
        """;

    private static string RequestGuard => $"""
        IF pg_catalog.jsonb_typeof(request) IS DISTINCT FROM 'object' THEN
            RAISE EXCEPTION 'idempotency_receipt_request_shape' USING ERRCODE = '22023'; END IF;
        IF ARRAY(SELECT k FROM pg_catalog.jsonb_object_keys(request) k ORDER BY k COLLATE "C")
            IS DISTINCT FROM ARRAY[{string.Join(",", RequestFields.Order(StringComparer.Ordinal).Select(Text))}]::text[]
            OR EXISTS (SELECT 1 FROM pg_catalog.jsonb_each(request) field
                WHERE pg_catalog.jsonb_typeof(field.value) IS DISTINCT FROM 'string'
                    AND NOT (field.key IN ('UserId','RequestContentType') AND field.value = 'null'::jsonb))
            OR (request->>'TenantId')::uuid IS DISTINCT FROM p_tenant
            OR pg_catalog.char_length(request->>'Key') NOT BETWEEN 1 AND 128
            OR pg_catalog.btrim(request->>'Key') = ''
            OR pg_catalog.char_length(request->>'UserId') > 256
            OR pg_catalog.char_length(request->>'RequestMethod') NOT BETWEEN 1 AND 16
            OR pg_catalog.btrim(request->>'RequestMethod') = ''
            OR pg_catalog.char_length(request->>'RequestTarget') NOT BETWEEN 1 AND 512
            OR pg_catalog.btrim(request->>'RequestTarget') = ''
            OR pg_catalog.char_length(request->>'RequestContentType') > 256
            OR (request->>'RequestBodyHash') !~ {Text("^[0-9a-fA-F]{64}$")}
            OR (request->>'PrincipalFingerprint') !~ {Text("^[0-9a-fA-F]{64}$")}
        THEN RAISE EXCEPTION 'idempotency_receipt_request_shape' USING ERRCODE = '22023'; END IF;
        """;

    private const string CleanupGuard = """
        IF p_take IS NULL OR p_take NOT BETWEEN 1 AND 1000 OR p_cutoff IS NULL
            OR NOT pg_catalog.isfinite(p_cutoff) THEN
            RAISE EXCEPTION 'idempotency_receipt_cleanup_bound' USING ERRCODE = '22023'; END IF;
        """;

    private static string Projection(ExploreDbContext context, string alias, string now)
    {
        var sql = new IdempotencyReceiptSql(context);
        string C(string property) => $"{alias}.{sql.Column(typeof(IdempotencyRecord), property)}";
        string expired = $"{C("ExpiresAt")} <= GREATEST({now},pg_catalog.clock_timestamp())";
        return "pg_catalog.jsonb_build_object(" + string.Join(",", ReceiptFields.Select(property =>
        {
            string value = C(property);
            if (property is "CreatedAt" or "ExpiresAt")
                value = $"pg_catalog.to_char({value} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0Z'";
            if (property == "StatusCode") value = $"CASE WHEN {expired} THEN -1 ELSE {value} END";
            if (property is "ResponseBody" or "ContentType")
                value = $"CASE WHEN {expired} OR {C("StatusCode")} <= 0 THEN NULL ELSE {value} END";
            return $"{Text(property)},{value}";
        })) + ")";
    }

    private static string IdentityMatch(ExploreDbContext context, string alias)
    {
        var sql = new IdempotencyReceiptSql(context);
        return string.Join(" AND ", RequestFields.Select(property =>
            property == "TenantId" ? $"{alias}.{sql.Column(typeof(IdempotencyRecord), property)} = p_tenant"
                : $"({alias}.{sql.Column(typeof(IdempotencyRecord), property)} COLLATE \"C\")"
                    + $" IS NOT DISTINCT FROM ((request->>{Text(property)}) COLLATE \"C\")"));
    }

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, string operation)
    {
        var sql = new IdempotencyReceiptSql(context);
        string table = sql.Table(typeof(IdempotencyRecord));
        string ownership = sql.Name(OwnershipTable);
        string C(string property) => sql.Column(typeof(IdempotencyRecord), property);
        string columns = string.Join(",", ReceiptFields.Select(C));
        string proposalValues = string.Join(",", ReceiptFields.Select(property => property switch
        {
            "Id" => "(p_proposal->>'Id')::uuid",
            "TenantId" => "p_tenant",
            "CreatedAt" => "(p_proposal->>'CreatedAt')::timestamp with time zone",
            "ExpiresAt" => "(p_proposal->>'ExpiresAt')::timestamp with time zone",
            "StatusCode" => "0",
            "ResponseBody" or "ContentType" => "NULL",
            _ => $"request->>{Text(property)}"
        }));
        string work = operation switch
        {
            "claim" => $"""
                IF pg_catalog.jsonb_typeof(p_proposal) IS DISTINCT FROM 'object' THEN
                    RAISE EXCEPTION 'idempotency_receipt_proposal_shape' USING ERRCODE = '22023'; END IF;
                IF ARRAY(SELECT k FROM pg_catalog.jsonb_object_keys(p_proposal) k ORDER BY k COLLATE "C")
                        IS DISTINCT FROM ARRAY['CreatedAt','ExpiresAt','Id','Request']::text[]
                    OR pg_catalog.jsonb_typeof(p_proposal->'Id') IS DISTINCT FROM 'string'
                    OR (p_proposal->>'Id')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
                    OR pg_catalog.jsonb_typeof(p_proposal->'CreatedAt') IS DISTINCT FROM 'string'
                    OR pg_catalog.jsonb_typeof(p_proposal->'ExpiresAt') IS DISTINCT FROM 'string'
                    OR (p_proposal->>'CreatedAt') !~ {Text("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}[.][0-9]{6}0Z$")}
                    OR (p_proposal->>'ExpiresAt') !~ {Text("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}[.][0-9]{6}0Z$")}
                    OR NOT pg_catalog.isfinite((p_proposal->>'CreatedAt')::timestamp with time zone)
                    OR NOT pg_catalog.isfinite((p_proposal->>'ExpiresAt')::timestamp with time zone)
                    OR (p_proposal->>'CreatedAt')::timestamp with time zone > pg_catalog.clock_timestamp() + interval '1 minute'
                    OR (p_proposal->>'ExpiresAt')::timestamp with time zone
                        <= GREATEST((p_proposal->>'CreatedAt')::timestamp with time zone,pg_catalog.clock_timestamp())
                    OR p_nonce IS NULL OR pg_catalog.octet_length(p_nonce) <> 32 OR p_bound IS NULL
                THEN RAISE EXCEPTION 'idempotency_receipt_proposal_shape' USING ERRCODE = '22023'; END IF;
                request := p_proposal->'Request';
                {RequestGuard}
                INSERT INTO {table} ({columns}) VALUES ({proposalValues})
                    ON CONFLICT ({C("Key")},{C("TenantId")}) DO NOTHING;
                GET DIAGNOSTICS changed = ROW_COUNT;
                owned := changed = 1;
                IF owned THEN
                    INSERT INTO {ownership} (receipt_id,tenant_id,nonce_hash,backend,original_transaction)
                    VALUES ((p_proposal->>'Id')::uuid,p_tenant,pg_catalog.sha256(p_nonce),pg_catalog.pg_backend_pid(),
                        CASE WHEN p_bound THEN pg_catalog.pg_current_xact_id()::text::bigint ELSE NULL END);
                END IF;
                -- A separate statement receives the committed winner under Read Committed.
                -- Serializable losers propagate their native serialization failure to the owner.
                SELECT r.* INTO receipt FROM {table} r
                    WHERE r.{C("TenantId")} = p_tenant AND r.{C("Key")} COLLATE "C" = (request->>'Key') COLLATE "C";
                IF NOT FOUND THEN RAISE EXCEPTION 'idempotency_receipt_winner_not_visible' USING ERRCODE = '40001'; END IF;
                IF NOT ({IdentityMatch(context, "receipt")}) THEN
                    RETURN pg_catalog.jsonb_build_object('Outcome',1,'Record',NULL,'IsOwner',false); END IF;
                RETURN pg_catalog.jsonb_build_object('Outcome',2,'Record',
                    {Projection(context, "receipt", "(p_proposal->>'CreatedAt')::timestamp with time zone")},'IsOwner',owned);
                """,
            "read" => $"""
                request := p_request;
                {RequestGuard}
                IF p_now IS NULL OR NOT pg_catalog.isfinite(p_now) THEN
                    RAISE EXCEPTION 'idempotency_receipt_read_instant' USING ERRCODE = '22023'; END IF;
                SELECT r.* INTO receipt FROM {table} r
                    WHERE r.{C("TenantId")} = p_tenant AND r.{C("Key")} COLLATE "C" = (request->>'Key') COLLATE "C";
                IF NOT FOUND THEN RETURN pg_catalog.jsonb_build_object('Outcome',0,'Record',NULL,'IsOwner',false); END IF;
                IF NOT ({IdentityMatch(context, "receipt")}) THEN
                    RETURN pg_catalog.jsonb_build_object('Outcome',1,'Record',NULL,'IsOwner',false); END IF;
                RETURN pg_catalog.jsonb_build_object('Outcome',2,'Record',{Projection(context, "receipt", "p_now")},'IsOwner',false);
                """,
            "complete" => $"""
                IF p_id IS NULL OR p_nonce IS NULL OR pg_catalog.octet_length(p_nonce) <> 32
                    OR p_now IS NULL OR NOT pg_catalog.isfinite(p_now)
                    OR p_status IS NULL OR NOT (p_status = -1 OR p_status BETWEEN 100 AND 599)
                    OR p_status = -1 AND (p_body IS NOT NULL OR p_content_type IS NOT NULL)
                    OR pg_catalog.octet_length(p_body) > {MaximumProtectedPayloadBytes}
                    OR pg_catalog.char_length(p_content_type) > 256 THEN
                    RAISE EXCEPTION 'idempotency_receipt_completion_shape' USING ERRCODE = '22023'; END IF;
                UPDATE {table} r SET {C("StatusCode")} = p_status,{C("ResponseBody")} = p_body,{C("ContentType")} = p_content_type
                WHERE r.{C("TenantId")} = p_tenant AND r.{C("Id")} = p_id AND r.{C("StatusCode")} = 0
                    AND r.{C("ExpiresAt")} > GREATEST(p_now,pg_catalog.clock_timestamp())
                    AND EXISTS (SELECT 1 FROM {ownership} o WHERE o.tenant_id = p_tenant AND o.receipt_id = p_id
                        AND o.nonce_hash = pg_catalog.sha256(p_nonce) AND o.backend = pg_catalog.pg_backend_pid()
                        AND (o.original_transaction IS NULL
                            OR o.original_transaction = pg_catalog.pg_current_xact_id()::text::bigint));
                GET DIAGNOSTICS changed = ROW_COUNT;
                IF changed = 1 THEN
                    DELETE FROM {ownership} WHERE tenant_id = p_tenant AND receipt_id = p_id
                        AND nonce_hash = pg_catalog.sha256(p_nonce) AND backend = pg_catalog.pg_backend_pid();
                END IF;
                RETURN changed = 1;
                """,
            "consume" => $"""
                IF p_jti IS NULL OR pg_catalog.btrim(p_jti) = '' OR pg_catalog.char_length(p_jti) > 64
                    OR p_created IS NULL OR p_expires IS NULL
                    OR NOT pg_catalog.isfinite(p_created) OR NOT pg_catalog.isfinite(p_expires)
                    OR p_created > pg_catalog.clock_timestamp() + interval '1 minute'
                    OR p_expires <= GREATEST(p_created,pg_catalog.clock_timestamp()) THEN
                    RAISE EXCEPTION 'idempotency_receipt_bootstrap_shape' USING ERRCODE = '22023'; END IF;
                INSERT INTO {table} ({columns})
                    VALUES (pg_catalog.uuidv7(),'atproto-bootstrap:' || p_jti,p_tenant,NULL,'POST','/api/auth/atproto/session',
                        NULL,'','',-1,NULL,NULL,p_created,p_expires)
                    ON CONFLICT ({C("Key")},{C("TenantId")}) DO NOTHING;
                GET DIAGNOSTICS changed = ROW_COUNT;
                RETURN changed = 1;
                """,
            "tenants" => $"""
                {CleanupGuard}
                RETURN (WITH bounded AS (
                    SELECT r.{C("TenantId")} AS tenant FROM {table} r
                    WHERE r.{C("ExpiresAt")} <= p_cutoff AND r.{C("StatusCode")} <> -1
                    ORDER BY r.{C("ExpiresAt")},r.{C("Id")} LIMIT p_take)
                    SELECT COALESCE(pg_catalog.jsonb_agg(tenant ORDER BY tenant),'[]'::jsonb)
                    FROM (SELECT DISTINCT tenant FROM bounded) selected);
                """,
            "count" => $"""
                {CleanupGuard}
                RETURN (SELECT count(*)::integer FROM (
                    SELECT r.{C("Id")} FROM {table} r WHERE r.{C("TenantId")} = p_tenant
                        AND r.{C("ExpiresAt")} <= p_cutoff AND r.{C("StatusCode")} <> -1
                    ORDER BY r.{C("ExpiresAt")},r.{C("Id")} LIMIT p_take) bounded);
                """,
            "scrub" => $"""
                {CleanupGuard}
                WITH bounded AS (
                    SELECT r.{C("Id")} FROM {table} r WHERE r.{C("TenantId")} = p_tenant
                        AND r.{C("ExpiresAt")} <= p_cutoff AND r.{C("StatusCode")} <> -1
                    ORDER BY r.{C("ExpiresAt")},r.{C("Id")} LIMIT p_take FOR UPDATE SKIP LOCKED),
                scrubbed AS (
                    UPDATE {table} r SET {C("StatusCode")} = -1,{C("ResponseBody")} = NULL,{C("ContentType")} = NULL
                    FROM bounded b WHERE r.{C("TenantId")} = p_tenant AND r.{C("Id")} = b.{C("Id")}
                        AND r.{C("ExpiresAt")} <= p_cutoff AND r.{C("StatusCode")} <> -1 RETURNING r.{C("Id")}),
                spent AS (
                    DELETE FROM {ownership} o USING scrubbed r
                    WHERE o.tenant_id = p_tenant AND o.receipt_id = r.{C("Id")} RETURNING o.receipt_id)
                SELECT count(*)::integer INTO changed FROM scrubbed;
                RETURN changed;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        bool discovery = Routines.Single(routine => routine.Name == operation).Discovery;
        return $"""
            DECLARE receipt record; request jsonb; changed integer; owned boolean := false;
            BEGIN
                {Caller(enrollment, discovery)}
                {Guard(context, enrollment)}
                {work}
            END;
            """;
    }
}
