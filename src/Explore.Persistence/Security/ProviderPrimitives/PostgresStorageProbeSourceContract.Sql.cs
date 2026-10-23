namespace Explore.Persistence.Security;

public static partial class PostgresStorageProbeSourceContract
{
    private static string AnchorRow(ExploreDbContext context) => $"""
        IF (SELECT count(*) FROM {new HumanPrincipalSourceSql(context).Name(SourceTable)} WHERE id = 1) <> 1
        THEN RAISE EXCEPTION 'storage_probe_source_missing' USING ERRCODE = '55000'; END IF;
        """;

    private static string Caller(PostgresNativeQueueEnrollment enrollment, bool acquire) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> '{OwnerRole}'
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            {(acquire ? "OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'" : "")}
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM p_tenant
            OR p_tenant = '00000000-0000-0000-0000-000000000000'::uuid
            OR NOT COALESCE((session_user = {Text(enrollment.GlobalLogin)} AND p_tenant IS NULL
                OR session_user = {Text(enrollment.RuntimeLogin)} AND p_tenant IS NOT NULL),false)
            OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role(session_user,r.oid,'USAGE') OR pg_catalog.pg_has_role(session_user,r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
        THEN RAISE EXCEPTION 'storage_probe_source_caller' USING ERRCODE = '42501'; END IF;
        """;

    private static string Body(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool acquire)
    {
        string anchor = new HumanPrincipalSourceSql(context).Name(SourceTable);
        return $"""
            DECLARE generation bigint;
            BEGIN
                {Guard(context, enrollment)}
                {Caller(enrollment, acquire)}
                {AnchorRow(context)}
                {(acquire
                    ? $"UPDATE {anchor} SET revision = revision WHERE id = 1 RETURNING revision INTO generation;"
                    : $"SELECT revision INTO generation FROM {anchor} WHERE id = 1;")}
                IF generation IS NULL OR generation < 0
                THEN RAISE EXCEPTION 'storage_probe_source_missing' USING ERRCODE = '55000'; END IF;
                {Caller(enrollment, acquire)}
                RETURN generation;
            END;
            """;
    }

    private static string ChangedBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string sources = string.Join(",", Sources.Select(type => $"{Text(sql.Table(type))}::regclass"));
        return $"""
            BEGIN
                IF current_user <> '{OwnerRole}' OR TG_LEVEL <> 'STATEMENT' OR TG_WHEN <> 'BEFORE'
                    OR TG_OP NOT IN ('INSERT','UPDATE','DELETE','TRUNCATE') OR TG_RELID NOT IN ({sources})
                THEN RAISE EXCEPTION 'storage_probe_source_trigger_target' USING ERRCODE = '55000'; END IF;
                {Guard(context, enrollment)}
                UPDATE {sql.Name(SourceTable)} SET revision = revision + 1 WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'storage_probe_source_missing' USING ERRCODE = '55000'; END IF;
                RETURN NULL;
            END;
            """;
    }

    private static (string Operation, string Signature, string Arguments, string Result, string Body)[] Functions(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
    [
        ("observe", "uuid", "p_tenant uuid", "bigint", Body(context, enrollment, false)),
        ("acquire", "uuid", "p_tenant uuid", "bigint", Body(context, enrollment, true)),
        ("changed", "", "", "trigger", ChangedBody(context, enrollment))
    ];

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        string.Join("\n", Functions(context, enrollment).Select(function => $"""
            IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context, function.Operation) + "(" + function.Signature + ")")})
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(function.Body))}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + Hash(function.Body))})
            THEN RAISE EXCEPTION 'storage_probe_source_body_drift' USING ERRCODE = '55000'; END IF;
            """));
}
