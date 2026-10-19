namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventResponseClockContract
{
    private static string CallerPrivilegeGuard(SourceSql sql, PostgresNativeQueueEnrollment enrollment)
    {
        string sources = string.Join(",", sql.Sources.Keys.Select(type => Text(sql.Names.Table(type)) + "::regclass"));
        return $"""
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                    OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_class t CROSS JOIN pg_catalog.pg_roles r WHERE t.oid IN ({sources})
                    AND (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                    AND (r.oid = t.relowner
                        OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR pg_catalog.has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                    WHERE (pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'USAGE')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},r.oid,'SET'))
                        AND pg_catalog.has_schema_privilege(r.oid,pg_catalog.to_regnamespace({Text(sql.Names.Schema)}),'CREATE'))
            THEN RAISE EXCEPTION 'public_clock_runtime_source_privilege' USING ERRCODE = '42501'; END IF;
            """;
    }

    private static string PolicyGuard(SourceSql sql, IReadOnlyDictionary<Type, string> policies) =>
        string.Join("\n", policies.Select(pair => EventReadProjectionSql.ReadPolicyGuard(
            sql.Names.Table(pair.Key), Policy, OwnerRole, Stamp + Hash(pair.Value) + ":", "public_clock")));

    private static string SourceGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment) =>
        EventReadProjectionSql.PublicSourceGuard(context, sql.Names, sql.Sources, enrollment.MigratorLogin, "public_clock");
}
