using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Services;

namespace Explore.Persistence.Security;

public static partial class PostgresOrdinarySettingsContract
{
    private static string DisclosureKeysSql() => "ARRAY[" + string.Join(",", SettingRegistry.All
        .Where(definition => !Specialized(definition.Key)
            && EventDiscoveryDisclosureMutationScope.IsDisclosureSetting(definition.Key))
        .Select(definition => Text(definition.Key))) + "]::text[]";

    private static string DisclosureGuard(ExploreDbContext context)
    {
        var bodies = PostgresDiscoveryDisclosureContract.NestedBodies(context);
        return PostgresDiscoveryDisclosureContract.RoutineAcl(context, true) + "\n"
            + string.Join("\n", PostgresDiscoveryDisclosureContract.NestedRoutines.Select(routine => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                    WHERE f.oid = pg_catalog.to_regprocedure({Text(PostgresDiscoveryDisclosureContract.FunctionSql(context, routine.Name)
                        + "(" + routine.Signature + ")")})
                        AND f.proowner = pg_catalog.to_regrole('{PostgresDiscoveryDisclosureContract.OwnerRole}')::oid
                        AND f.prokind = 'f' AND f.prosecdef AND f.proconfig = ARRAY['search_path=pg_catalog']::text[]
                        AND f.prorettype = 'pg_catalog.{routine.Result}'::regtype
                        AND f.proretset = {routine.Set.ToString().ToLowerInvariant()} AND f.provolatile = '{routine.Volatility}'
                        AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                        AND f.proargdefaults IS NULL AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                        AND f.proargnames = ARRAY[{string.Join(",", routine.Arguments.Split(',').Select(Text))}]::text[]
                        AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                        AND f.prosrc = {Text(bodies[routine.Name])}
                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(f.proacl) a
                            WHERE a.grantee = pg_catalog.to_regrole('{OwnerRole}')::oid
                                AND a.grantor = f.proowner AND a.privilege_type = 'EXECUTE' AND NOT a.is_grantable))
                THEN RAISE EXCEPTION 'ordinary_settings_disclosure_contract_drift' USING ERRCODE = '55000'; END IF;
                """));
    }

    private static string TenantMembershipBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, bool truncate)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            BEGIN
                {Guard(context, enrollment)}
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'AFTER'
                    OR TG_LEVEL <> '{(truncate ? "STATEMENT" : "ROW")}'
                    OR TG_RELID <> {Text(sql.Table(typeof(Tenant)))}::regclass
                    OR TG_OP NOT IN ({(truncate ? "'TRUNCATE'" : "'INSERT','UPDATE','DELETE'")})
                THEN RAISE EXCEPTION 'ordinary_settings_tenant_trigger_target' USING ERRCODE = '55000'; END IF;
                {(truncate ? "" : $"""
                    IF TG_OP = 'UPDATE' AND NEW.{sql.Column(typeof(Tenant), nameof(Tenant.Id))}
                        IS NOT DISTINCT FROM OLD.{sql.Column(typeof(Tenant), nameof(Tenant.Id))} THEN RETURN NULL; END IF;
                    """)}
                UPDATE {sql.Name(SourceTable)} SET tenant_revision = tenant_revision + 1 WHERE id = 1;
                IF NOT FOUND THEN RAISE EXCEPTION 'ordinary_settings_anchor_missing' USING ERRCODE = '55000'; END IF;
                RETURN NULL;
            END;
            """;
    }

    private static string SettlementBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        var sql = new HumanPrincipalSourceSql(context);
        return $"""
            DECLARE admitted {sql.Name(AttemptTable)}%ROWTYPE;
            BEGIN
                {Guard(context, enrollment)}
                IF current_user <> '{OwnerRole}' OR TG_WHEN <> 'AFTER' OR TG_LEVEL <> 'ROW'
                    OR TG_OP NOT IN ('INSERT','UPDATE') OR TG_TABLE_SCHEMA <> {Text(sql.Schema)}
                    OR TG_TABLE_NAME <> '{AttemptTable}'
                THEN RAISE EXCEPTION 'ordinary_settings_settlement_target' USING ERRCODE = '55000'; END IF;
                -- Deferred events carry historic NEW tuples. Only the current retained
                -- attempt can prove completion and own this transaction's finalization.
                SELECT * INTO admitted FROM {sql.Name(AttemptTable)}
                    WHERE attempt = NEW.attempt AND backend = NEW.backend FOR UPDATE;
                IF NOT FOUND OR NOT admitted.finished OR admitted.pending IS NOT NULL OR admitted.observed
                THEN RAISE EXCEPTION 'ordinary_settings_missing_completion' USING ERRCODE = '42501'; END IF;
                IF admitted.request = pg_catalog.jsonb_build_object() THEN RETURN NULL; END IF;
                -- Clear custody before issuing nested work, including under IMMEDIATE
                -- constraints. Recursive/queued trigger calls see this tombstone.
                UPDATE {sql.Name(AttemptTable)}
                    SET tenant = NULL,user_id = NULL,request = pg_catalog.jsonb_build_object(),
                        image = pg_catalog.jsonb_build_object(),completed = pg_catalog.jsonb_build_object(),
                        disclosure_tenants = ARRAY[]::uuid[]
                    WHERE attempt = admitted.attempt AND backend = admitted.backend;
                RETURN NULL;
            END;
            """;
    }
}
