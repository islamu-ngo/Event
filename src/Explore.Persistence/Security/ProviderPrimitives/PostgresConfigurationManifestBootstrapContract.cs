using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Independent bootstrap-only native operations on the selected nonhuman Global connector.</summary>
public static partial class PostgresConfigurationManifestBootstrapContract
{
    public const string OwnerRole = "event_manifest_bootstrap_owner";
    public const string MigratorRole = "event_manifest_bootstrap_migrator";
    public const string GlobalRole = "event_manifest_bootstrap_global";
    private const string Prefix = "event-manifest-bootstrap:v1:";
    private const string Policy = "manifest_bootstrap_owner";
    private const string Boundary = "manifest_bootstrap_owner_boundary";

    public static async Task ApplyAsync(ExploreDbContext context, string globalLogin, CancellationToken token = default)
    {
        if (!context.Database.IsNpgsql() || context.TenantFilterTenantId is not null
            || context.IsConfigurationInstanceImportOwner || context.HasHumanCommitContextLease)
            throw new InvalidOperationException("Bootstrap installation requires its migration owner.");
        await using var own = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        await using var identity = context.Database.GetDbConnection().CreateCommand();
        identity.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        identity.CommandText = "SELECT CURRENT_USER WHERE CURRENT_USER = SESSION_USER";
        string migrationLogin = (string?)await identity.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The original migration login is required.");
        ValidateLogins(migrationLogin, globalLogin);
        var data = new ConfigurationManifestBootstrapData(context);
        var functions = Functions(context, data, migrationLogin, globalLogin);
        var sql = context.GetService<ISqlGenerationHelper>();
        string schemas = string.Join(", ", data.Relations.Select(relation => relation.Schema).Distinct(StringComparer.Ordinal)
            .Select(schema => sql.DelimitIdentifier(schema)));
        var grants = data.Relations.Select(relation =>
        {
            string columns = string.Join(", ", relation.Columns.Select(column => column.Sql));
            string update = string.Join(", ", UpdateNames(relation).Select(name => relation.Field(name)));
            return $"GRANT SELECT ON {relation.Table} TO {OwnerRole};\n" +
                $"GRANT INSERT ({columns}) ON {relation.Table} TO {OwnerRole};" +
                (update.Length == 0 ? "" : $"\nGRANT UPDATE ({update}) ON {relation.Table} TO {OwnerRole};");
        });
        var policies = data.Relations.Where(relation => relation.Columns.Any(column => column.Name == "TenantId"))
            .Select(relation =>
            {
                string predicate = RowBoundary(relation);
                return string.Join("\n", new[] { (Policy, "PERMISSIVE"), (Boundary, "RESTRICTIVE") }.Select(policy => $"""
                    DROP POLICY IF EXISTS "{policy.Item1}" ON {relation.Table};
                    CREATE POLICY "{policy.Item1}" ON {relation.Table} AS {policy.Item2} FOR ALL TO {OwnerRole}
                        USING ({predicate}) WITH CHECK ({predicate});
                    DO $stamp$ BEGIN
                        EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L', p.polname,
                            {Text(relation.Table)}, {Text(Prefix + Hash(predicate) + ":")} ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(p.polqual::text, 'UTF8')), 'hex') || ':' ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(p.polwithcheck::text, 'UTF8')), 'hex'))
                            FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(relation.Table)}::regclass AND p.polname = '{policy.Item1}');
                    END;$stamp$;
                    """));
            });
        string ddl = string.Join("\n", functions.Select(function => $"""
            CREATE OR REPLACE FUNCTION {function.Name}({function.Arguments}) RETURNS {function.Returns}
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $body${function.Body}$body$;
            REVOKE ALL ON FUNCTION {function.Name}({function.Signature}) FROM PUBLIC, {GlobalRole};
            GRANT EXECUTE ON FUNCTION {function.Name}({function.Signature}) TO {GlobalRole};
            """));
        string script = $"""
            DO $roles$
            DECLARE name text;
            BEGIN
                IF CURRENT_USER <> {Text(migrationLogin)} OR CURRENT_USER <> SESSION_USER
                    OR CURRENT_USER = {Text(globalLogin)} THEN
                    RAISE EXCEPTION 'manifest_bootstrap_migration_owner' USING ERRCODE = '42501';
                END IF;
                FOREACH name IN ARRAY ARRAY['{OwnerRole}','{MigratorRole}','{GlobalRole}'] LOOP
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname = name) THEN
                        EXECUTE pg_catalog.format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS', name);
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE r.rolname = name
                        AND (r.rolcanlogin OR r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolinherit)) THEN
                        RAISE EXCEPTION 'manifest_bootstrap_role_invalid' USING ERRCODE = '42501';
                    END IF;
                END LOOP;
            END;$roles$;
            GRANT {OwnerRole} TO {MigratorRole} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT {MigratorRole} TO {sql.DelimitIdentifier(migrationLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE;
            GRANT USAGE ON SCHEMA {schemas} TO {OwnerRole}, {GlobalRole};
            GRANT CREATE ON SCHEMA {sql.DelimitIdentifier(data.Relations[0].Schema)} TO {OwnerRole};
            {string.Join("\n", grants)}
            {string.Join("\n", policies)}
            SET LOCAL ROLE {OwnerRole};
            {ddl}
            RESET ROLE;
            REVOKE CREATE ON SCHEMA {sql.DelimitIdentifier(data.Relations[0].Schema)} FROM {OwnerRole};
            """;
        await context.Database.ExecuteSqlRawAsync(script, token);
        if (own is not null) await own.CommitAsync(token);
    }

    internal static async Task<JsonObject> ReadAsync(ExploreDbContext context, ConfigurationManifestBootstrapData data,
        JsonObject scope, string migrationLogin, string globalLogin, CancellationToken token)
    {
        RequireOwner(context);
        await AttestAsync(context, data, migrationLogin, globalLogin, token);
        return await CallAsync(context, "source", token, ("scope", scope.ToJsonString()));
    }

    internal static async Task<JsonObject> ReadValidationAsync(ExploreDbContext context, ConfigurationManifestBootstrapData data,
        JsonObject scope, string migrationLogin, string globalLogin, CancellationToken token)
    {
        RequireOwner(context);
        await AttestAsync(context, data, migrationLogin, globalLogin, token);
        return await CallAsync(context, "validate_source", token, ("scope", scope.ToJsonString()));
    }

    internal static async Task WriteAsync(ExploreDbContext context, ConfigurationManifestBootstrapData data,
        JsonObject scope, string migrationLogin, string globalLogin, JsonObject before, JsonArray changes,
        JsonObject after, CancellationToken token)
    {
        RequireOwner(context);
        await AttestAsync(context, data, migrationLogin, globalLogin, token);
        var result = await CallAsync(context, "write", token,
            ("scope", scope.ToJsonString()), ("before", before.ToJsonString()),
            ("changes", changes.ToJsonString()), ("after", after.ToJsonString()));
        if (!ConfigurationManifestBootstrapData.Same(result, after))
            throw new UnauthorizedAccessException("The original bootstrap native writer did not acknowledge its postimage.");
    }

    internal static async Task WriteEvidenceAsync(ExploreDbContext context, ConfigurationManifestBootstrapData data,
        JsonObject scope, string migrationLogin, string globalLogin, JsonObject before, JsonArray changes,
        JsonObject after, CancellationToken token)
    {
        RequireOwner(context);
        await AttestAsync(context, data, migrationLogin, globalLogin, token);
        var result = await CallAsync(context, "write_failed", token, ("scope", scope.ToJsonString()),
            ("before", before.ToJsonString()), ("changes", changes.ToJsonString()), ("after", after.ToJsonString()));
        if (!ConfigurationManifestBootstrapData.Same(result, after))
            throw new UnauthorizedAccessException("The failed-history writer did not acknowledge its exact evidence.");
    }

    internal static async Task AttestAsync(ExploreDbContext context, ConfigurationManifestBootstrapData data,
        string migrationLogin, string globalLogin, CancellationToken token)
    {
        ValidateLogins(migrationLogin, globalLogin);
        foreach (var function in Functions(context, data, migrationLogin, globalLogin))
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                SELECT CURRENT_USER = SESSION_USER AND SESSION_USER = @login
                    AND p.prosrc = @body AND p.proowner = owner.oid AND NOT owner.rolcanlogin
                    AND p.prosecdef AND p.provolatile = 'v' AND p.proparallel = 'u'
                    AND NOT p.proleakproof AND NOT p.proisstrict AND NOT p.proretset
                    AND p.prokind = 'f' AND p.proargdefaults IS NULL
                    AND p.proconfig = ARRAY['search_path=pg_catalog']::text[]
                    AND p.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                    AND p.prorettype = @returns::pg_catalog.regtype
                    AND pg_catalog.has_function_privilege(@login, p.oid, 'EXECUTE')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(COALESCE(p.proacl, pg_catalog.acldefault('f', p.proowner))) acl
                        WHERE acl.grantee NOT IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN (@owner,@executor))
                            OR acl.privilege_type <> 'EXECUTE' OR acl.grantor <> p.proowner
                            OR acl.grantee <> p.proowner AND acl.is_grantable)
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_roles owner ON owner.rolname = @owner
                WHERE p.oid = pg_catalog.to_regprocedure(@signature)
                """;
            Add(command, "login", globalLogin); Add(command, "body", function.Body); Add(command, "owner", OwnerRole);
            Add(command, "executor", GlobalRole); Add(command, "returns", "pg_catalog." + function.Returns);
            Add(command, "signature", function.Name + "(" + function.Signature + ")");
            if (await command.ExecuteScalarAsync(token) is not true)
                throw new UnauthorizedAccessException("The independently expected bootstrap namespace or native function is not installed.");
        }
        foreach (var relation in data.Relations)
        {
            foreach (var column in relation.Columns)
            {
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = """
                    SELECT a.attnotnull = @required AND pg_catalog.format_type(a.atttypid,a.atttypmod) = @type
                        AND a.attgenerated = '' AND NOT a.attisdropped
                    FROM pg_catalog.pg_attribute a WHERE a.attrelid = pg_catalog.to_regclass(@table)
                        AND a.attname = @column AND a.attnum > 0
                    """;
                Add(command, "table", relation.Table); Add(command, "column", column.Property.GetColumnName()!);
                Add(command, "type", column.StoreType);
                command.Parameters.Add(new Npgsql.NpgsqlParameter("required", NpgsqlDbType.Boolean) { Value = !column.Property.IsNullable });
                if (await command.ExecuteScalarAsync(token) is not true)
                    throw new UnauthorizedAccessException("The original bootstrap native column contract changed.");
            }
        }
    }

    private static (string Name, string Signature, string Arguments, string Returns, string Body)[] Functions(
        ExploreDbContext context, ConfigurationManifestBootstrapData data, string migrationLogin, string globalLogin)
    {
        string assert = Function(context, "assert_authority");
        string read = Function(context, "source");
        return
        [
            (assert, "", "", "void", AuthorityBody(context, data, migrationLogin, globalLogin)),
            (read, "jsonb", "p_scope jsonb", "jsonb", ReadBody(data, assert, retainSource: true)),
            (Function(context, "validate_source"), "jsonb", "p_scope jsonb", "jsonb", ReadBody(data, assert, retainSource: false)),
            (Function(context, "write"), "jsonb,jsonb,jsonb,jsonb", "p_scope jsonb, p_before jsonb, p_changes jsonb, p_after jsonb",
                "jsonb", WriteBody(data, assert, read)),
            (Function(context, "write_failed"), "jsonb,jsonb,jsonb,jsonb", "p_scope jsonb, p_before jsonb, p_changes jsonb, p_after jsonb",
                "jsonb", $"""
                    BEGIN
                        PERFORM {assert}();
                        IF p_changes IS NULL OR pg_catalog.jsonb_typeof(p_changes) <> 'array'
                            OR pg_catalog.jsonb_array_length(p_changes) <> 1 OR p_changes->0->>'Tag' <> 'Operations'
                            OR p_changes->0->'Before' <> 'null'::jsonb
                            OR p_changes->0->'After'->>'Id' <> p_scope->>'Operation'
                            OR p_changes->0->'After'->'Status' IS DISTINCT FROM
                                {Text(data.ProviderValue("Operations", nameof(ConfigurationManifestOperation.Status), ConfigurationManifestOperationStatus.Failed)!.ToJsonString())}::jsonb THEN
                            RAISE EXCEPTION 'manifest_bootstrap_failed_history_only' USING ERRCODE = '42501';
                        END IF;
                        RETURN {Function(context, "write")}(p_scope,p_before,p_changes,p_after);
                    END;
                    """)
        ];
    }

    private static string AuthorityBody(ExploreDbContext context, ConfigurationManifestBootstrapData data,
        string migrationLogin, string globalLogin)
    {
        var access = data.Relations.SelectMany(relation => relation.Columns.SelectMany(column =>
            new[] { "SELECT", "INSERT" }.Concat(UpdateNames(relation).Contains(column.Name) ? ["UPDATE"] : [])
                .Select(privilege => $"({Text(relation.Table)}::regclass,{Text(column.Property.GetColumnName())},{Text(privilege)})")));
        string expected = string.Join(", ", access);
        var policies = data.Relations.Where(relation => relation.Columns.Any(column => column.Name == "TenantId")).Select(relation =>
        {
            string prefix = Prefix + Hash(RowBoundary(relation)) + ":";
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class r WHERE r.oid = {Text(relation.Table)}::regclass
                    AND r.relrowsecurity AND r.relforcerowsecurity)
                OR EXISTS (SELECT 1 FROM (VALUES ('{Policy}',true),('{Boundary}',false)) expected(name,permissive)
                    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid = {Text(relation.Table)}::regclass
                        AND p.polname = expected.name AND p.polpermissive = expected.permissive AND p.polcmd = '*'
                        AND p.polroles = ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}')]
                        AND pg_catalog.obj_description(p.oid,'pg_policy') = {Text(prefix)} ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(p.polqual::text,'UTF8')),'hex') || ':' ||
                            pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(p.polwithcheck::text,'UTF8')),'hex')))
                """;
        });
        string functionIds = string.Join(", ", new[] { ("assert_authority", ""), ("source", "jsonb"), ("validate_source", "jsonb"), ("write", "jsonb,jsonb,jsonb,jsonb"),
                ("write_failed", "jsonb,jsonb,jsonb,jsonb") }
            .Select(item => Text(Function(context, item.Item1) + "(" + item.Item2 + ")") + "::regprocedure"));
        return $"""
            BEGIN
                IF CURRENT_USER <> '{OwnerRole}' OR SESSION_USER <> {Text(globalLogin)}
                    OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL
                    OR NOT pg_catalog.pg_has_role(SESSION_USER,'{GlobalRole}','USAGE')
                    OR pg_catalog.pg_has_role(SESSION_USER,'{OwnerRole}','MEMBER')
                    OR pg_catalog.pg_has_role(SESSION_USER,'{MigratorRole}','MEMBER')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = SESSION_USER
                        AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication))
                    OR (SELECT count(*) FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}')) <> 3
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}')
                        AND (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge
                        JOIN pg_catalog.pg_roles member ON member.oid = edge.member
                        JOIN pg_catalog.pg_roles parent ON parent.oid = edge.roleid
                        WHERE member.rolname IN ('{OwnerRole}','{GlobalRole}')
                            OR member.rolname = '{MigratorRole}' AND parent.rolname <> '{OwnerRole}'
                            OR parent.rolname = '{OwnerRole}' AND member.rolname <> '{MigratorRole}'
                            OR parent.rolname = '{MigratorRole}' AND member.rolname <> {Text(migrationLogin)}
                            OR parent.rolname = '{GlobalRole}' AND member.rolname <> {Text(globalLogin)}
                            OR parent.rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}') AND edge.admin_option
                            OR parent.rolname IN ('{OwnerRole}','{MigratorRole}') AND (edge.inherit_option OR NOT edge.set_option)
                            OR parent.rolname = '{GlobalRole}' AND (NOT edge.inherit_option OR edge.set_option))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_class relation JOIN pg_catalog.pg_namespace namespace ON namespace.oid = relation.relnamespace
                        WHERE namespace.nspname NOT IN ('pg_catalog','information_schema') AND namespace.nspname NOT LIKE 'pg_toast%'
                            AND relation.relkind IN ('r','p','v','m','f')
                            AND (relation.relowner IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}'))
                                OR pg_catalog.has_table_privilege('{OwnerRole}',relation.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR pg_catalog.has_table_privilege('{MigratorRole}',relation.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR pg_catalog.has_any_column_privilege('{MigratorRole}',relation.oid,'SELECT,INSERT,UPDATE,REFERENCES')
                                OR pg_catalog.has_table_privilege('{GlobalRole}',relation.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                OR pg_catalog.has_any_column_privilege('{GlobalRole}',relation.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class r ON r.oid = a.attrelid
                        JOIN pg_catalog.pg_namespace n ON n.oid = r.relnamespace
                        CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) access(privilege)
                        WHERE a.attnum > 0 AND NOT a.attisdropped AND n.nspname NOT IN ('pg_catalog','information_schema')
                            AND n.nspname NOT LIKE 'pg_toast%' AND pg_catalog.has_column_privilege('{OwnerRole}',r.oid,a.attnum,access.privilege)
                            AND NOT EXISTS (SELECT 1 FROM (VALUES {expected}) e(relation_id,column_name,privilege)
                                WHERE e.relation_id = r.oid AND e.column_name = a.attname AND e.privilege = access.privilege))
                    OR EXISTS (SELECT 1 FROM (VALUES {expected}) e(relation_id,column_name,privilege)
                        WHERE NOT pg_catalog.has_column_privilege('{OwnerRole}',e.relation_id,e.column_name,e.privilege))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc p WHERE p.proowner IN
                        (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}'))
                        AND (p.proowner <> (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = '{OwnerRole}') OR p.oid NOT IN ({functionIds})))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_proc p CROSS JOIN LATERAL pg_catalog.aclexplode(p.proacl) acl
                        WHERE p.oid NOT IN ({functionIds}) AND acl.grantee IN
                            (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}')))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_class r CROSS JOIN LATERAL pg_catalog.aclexplode(r.relacl) acl
                        WHERE acl.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}'))
                            AND acl.is_grantable)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE acl.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}'))
                            AND acl.is_grantable)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_namespace n CROSS JOIN pg_catalog.pg_roles r
                        WHERE r.rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}')
                            AND pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE'))
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_class s CROSS JOIN pg_catalog.pg_roles r
                        WHERE s.relkind = 'S' AND r.rolname IN ('{OwnerRole}','{MigratorRole}','{GlobalRole}')
                            AND pg_catalog.has_sequence_privilege(r.oid,s.oid,'USAGE,SELECT,UPDATE'))
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge JOIN pg_catalog.pg_roles p ON p.oid = edge.roleid
                        JOIN pg_catalog.pg_roles m ON m.oid = edge.member
                        WHERE p.rolname = '{OwnerRole}' AND m.rolname = '{MigratorRole}')
                    OR NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members edge JOIN pg_catalog.pg_roles p ON p.oid = edge.roleid
                        JOIN pg_catalog.pg_roles m ON m.oid = edge.member
                        WHERE p.rolname = '{MigratorRole}' AND m.rolname = {Text(migrationLogin)})
                    OR {string.Join(" OR ", policies)} THEN
                    RAISE EXCEPTION 'manifest_bootstrap_native_authority_invalid' USING ERRCODE = '42501';
                END IF;
                RETURN;
            END;
            """;
    }

    private static string RowBoundary(ConfigurationManifestBootstrapData.Relation relation) => relation.Tag switch
    {
        "Overrides" => $"{relation.Field("SettingKey")} = ANY ({Values(ConfigurationManifestCatalog.TenantSettings.Keys)})",
        "Documents" => $"{relation.Field("DocumentKey")} = ANY ({Values([SettingsDocumentKeys.Tenant.Branding, SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity])})",
        "Outbox" => $"{relation.Field("AggregateType")} = {Text(ConfigurationManifestEffectOutbox.AggregateType)} " +
            $"AND {relation.Field("EventType")} = {Text(ConfigurationManifestEffectOutbox.EventType)}",
        _ => "true"
    };

    private static string Function(ExploreDbContext context, string name) =>
        context.GetService<ISqlGenerationHelper>().DelimitIdentifier("configuration_manifest_bootstrap_" + name,
            new ConfigurationManifestBootstrapData(context).Relations[0].Schema);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateLogins(string migration, string global)
    {
        if (string.IsNullOrWhiteSpace(migration) || string.IsNullOrWhiteSpace(global) || migration == global
            || migration != migration.Trim() || global != global.Trim() || migration.Any(char.IsControl) || global.Any(char.IsControl))
            throw new UnauthorizedAccessException("Distinct original migration and Global enrollment names are required.");
    }
    private static void RequireOwner(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!context.Database.IsNpgsql() || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.HasHumanCommitContextLease
            || context.Database.GetDbConnection().State != ConnectionState.Open
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("The original nonhuman Global bootstrap transaction is required.");
    }
    private static void Add(System.Data.Common.DbCommand command, string name, string value) =>
        command.Parameters.Add(new Npgsql.NpgsqlParameter(name, NpgsqlDbType.Text) { Value = value });
    private static async Task<JsonObject> CallAsync(ExploreDbContext context, string operation, CancellationToken token,
        params (string Name, string Value)[] arguments)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {Function(context, operation)}({string.Join(", ", arguments.Select(argument => "@" + argument.Name))})";
        foreach (var argument in arguments)
            command.Parameters.Add(new Npgsql.NpgsqlParameter(argument.Name, NpgsqlDbType.Jsonb) { Value = argument.Value });
        return JsonNode.Parse((string)(await command.ExecuteScalarAsync(token))!)!.AsObject();
    }
}
