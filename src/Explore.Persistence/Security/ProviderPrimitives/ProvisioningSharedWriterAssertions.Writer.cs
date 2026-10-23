using System.Security.Cryptography;
using System.Text;

namespace Explore.Persistence.Security;

internal static partial class ProvisioningSharedWriterAssertions
{
    internal sealed record Writer(
        string Name, string Function, string Anchor, string Stamp,
        IReadOnlyList<(Type Type, int Flags)> Targets,
        PostgresPrivilegeGuard.Contract Privileges, string Body, bool PrecedesProvisioning = false,
        PostgresPrivilegeGuard.Contract? AdmissionPrivileges = null)
    {
        internal string Presence(ExploreDbContext context)
        {
            var sql = new HumanPrincipalSourceSql(context);
            return $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(",", Privileges.GuardedRoles.Select(Text))}))
                OR pg_catalog.to_regclass({Text(sql.Name(Anchor))}) IS NOT NULL
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc f JOIN pg_catalog.pg_namespace n ON n.oid = f.pronamespace
                    WHERE n.nspname = {Text(sql.Schema)} AND f.proname IN ({string.Join(",", Privileges.Routines.Select(routine => Text(routine.Name)))}))
                OR EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = {Text(sql.Schema)} AND t.tgname = {Text(Name)})
                """;
        }

        internal string AssertInstalled(ExploreDbContext context)
        {
            var sql = new HumanPrincipalSourceSql(context);
            string function = $"pg_catalog.to_regprocedure({Text(sql.Name(Function) + "()")})";
            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Body)));
            string targets = string.Join(",", Targets.Select(target => $"pg_catalog.to_regclass({Text(sql.Table(target.Type))})"));
            string triggers = string.Join("\n", Targets.Select(target => $"""
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid = pg_catalog.to_regclass({Text(sql.Table(target.Type))})
                        AND t.tgname = {Text(Name)} AND t.tgfoid = {function}
                        AND t.tgtype = {target.Flags} AND t.tgenabled = 'A' AND NOT t.tgisinternal
                        AND t.tgqual IS NULL AND t.tgnargs = 0 AND t.tgargs = ''::bytea
                        AND t.tgattr = ''::int2vector AND t.tgconstraint = 0 AND t.tgparentid = 0
                        AND NOT t.tgdeferrable AND NOT t.tginitdeferred
                        AND t.tgoldtable IS NULL AND t.tgnewtable IS NULL)
                THEN RAISE EXCEPTION 'human_provisioning_shared_writer_drift' USING ERRCODE = '55000'; END IF;
                """));
            return $"""
                {PostgresPrivilegeGuard.BuildAssertSql(AdmissionPrivileges ?? Privileges)}
                IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f WHERE f.oid = {function}
                    AND f.prokind = 'f' AND f.prosecdef AND f.provolatile = 'v' AND f.proparallel = 'u'
                    AND NOT f.proleakproof AND NOT f.proisstrict AND NOT f.proretset
                    AND f.pronargs = 0 AND f.pronargdefaults = 0 AND f.proargdefaults IS NULL
                    AND f.proallargtypes IS NULL AND f.proargmodes IS NULL AND f.proargnames IS NULL AND f.provariadic = 0
                    AND f.prorettype = 'pg_catalog.trigger'::regtype
                    AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(hash)}
                    AND pg_catalog.obj_description(f.oid,'pg_proc') = {Text(Stamp + hash)})
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname IN ({string.Join(",", Privileges.GuardedRoles.Select(Text))})
                        AND pg_catalog.shobj_description(r.oid,'pg_authid') IS DISTINCT FROM {Text(Stamp + "role:")} || r.rolname)
                THEN RAISE EXCEPTION 'human_provisioning_shared_body_drift' USING ERRCODE = '55000'; END IF;
                {triggers}
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE (t.tgfoid = {function}
                        OR (t.tgname = {Text(Name)} AND t.tgrelid IN
                            (SELECT c.oid FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                                WHERE n.nspname = {Text(sql.Schema)})))
                        AND (t.tgrelid NOT IN ({targets}) OR t.tgname <> {Text(Name)} OR t.tgfoid IS DISTINCT FROM {function}))
                THEN RAISE EXCEPTION 'human_provisioning_shared_target_drift' USING ERRCODE = '55000'; END IF;
                """;
        }

        // An entirely absent later protocol is legal only during the declared
        // migrator's installation window. A name-only counterfeit, missing function,
        // orphan role or partially removed protocol takes the strict assertion path.
        internal string AssertInstalledOrMigrationAbsent(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) => $"""
            IF {Presence(context)} THEN
                {AssertInstalled(context)}
            ELSIF session_user <> {Text(enrollment.MigratorLogin)}
                OR current_user <> session_user OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            THEN RAISE EXCEPTION 'human_provisioning_shared_installation_missing' USING ERRCODE = '55000'; END IF;
            """;
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
