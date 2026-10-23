using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Fixed credential storage permissions; authentication and usage decisions execute in C#.</summary>
public static class PostgresExternalApiKeyAuthenticationContract
{
    public const string OwnerRole = "event_api_key_authentication_owner";
    public const string MigratorRole = "event_api_key_authentication_migrator";
    public const string GlobalRole = "event_api_key_authentication_global";
    private static readonly PostgresAuthenticationSourceProfile Profile = new(typeof(ExternalApiKey), GlobalRole,
        "api_key_authentication", [OwnerRole, MigratorRole],
        ["Id", "TenantId", "Name", "Description", "KeyId", "SecretHash", "Scopes", "ExternalApiKeyOwnerTypeId", "OwnerId",
            "ExternalApiKeyStatusId", "ExternalApiKeyCreditPeriodId", "CreditLimit", "MaxRolloverCredits", "ExpiresAt",
            "LastUsedAt", "LastUsedIp", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"],
        [nameof(ExternalApiKey.LastUsedAt), nameof(ExternalApiKey.LastUsedIp)],
        ["_for_authentication(text)", "_touch_usage(uuid,timestamp with time zone,text,timestamp with time zone)"]);

    // Machine-purpose guards still use these names to detect forbidden retired artifacts.
    public static string FunctionSql(ExploreDbContext context) => Profile.RetiredName(context, "_for_authentication");
    public static string UsageFunctionSql(ExploreDbContext context) => Profile.RetiredName(context, "_touch_usage");
    public static Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default) => Profile.ApplyAsync(context, enrollment, cancellationToken);
    public static Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default) => Profile.RequireReadyAsync(context, enrollment, cancellationToken);
    internal static string GlobalPolicyException(ExploreDbContext context, string globalLogin) => Profile.PolicyException(context, globalLogin);
}

/// <summary>Fixed-column catalogue mechanics shared by the two authentication sources, never business queries.</summary>
internal sealed class PostgresAuthenticationSourceProfile(Type type, string role, string policyPrefix,
    string[] obsoleteRoles, string[] properties, string[] updateProperties, string[] retiredSignatures)
{
    private const string NotReady = "authentication_source_permissions_not_ready";

    internal async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment, CancellationToken token)
    {
        if (!context.Database.IsNpgsql()) return;
        Validate(enrollment);
        var source = Map(context);
        await using var owned = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(token) : null;
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        var physical = context.Database.CurrentTransaction!.GetDbTransaction();
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) { if (change.CurrentState != ConnectionState.Open) interrupted = true; }
        void RequireOriginal()
        {
            if (interrupted || context.ContextId != contextId || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || !connection.Owns(physical)) throw new UnauthorizedAccessException(NotReady);
        }
        connection.StateChange += Changed;
        string savepoint = "authentication_permissions_" + Guid.CreateVersion7().ToString("N");
        bool saved = false;
        try
        {
            await context.Database.CurrentTransaction!.CreateSavepointAsync(savepoint, token);
            saved = true;
            RequireOriginal();
            if (!await ScalarAsync(context, Session(enrollment.MigratorLogin), token)) throw new UnauthorizedAccessException(NotReady);
            bool absent = await ScalarAsync(context, Absent(source), token);
            RequireOriginal();
            if (!await ScalarAsync(context, Catalogue(context, source, enrollment, !absent), token)) throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            if (absent)
            {
                var names = context.GetService<ISqlGenerationHelper>();
                string target = names.DelimitIdentifier(role);
                await ExecuteAsync(context, $"CREATE ROLE {target} NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS", token);
                await ExecuteAsync(context, $"GRANT {target} TO {names.DelimitIdentifier(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE", token);
                await ExecuteAsync(context, $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {target}", token);
                await ExecuteAsync(context, $"GRANT SELECT ({string.Join(',', source.Columns.Values.Select(column => names.DelimitIdentifier(column.Name)))}) ON {source.Table} TO {target}", token);
                if (updateProperties.Length != 0)
                    await ExecuteAsync(context, $"GRANT UPDATE ({string.Join(',', updateProperties.Select(property => names.DelimitIdentifier(source.Columns[property].Name)))}) ON {source.Table} TO {target}", token);
                foreach (string privilege in Privileges)
                {
                    string predicate = RowPredicate(enrollment.GlobalLogin);
                    await ExecuteAsync(context, $"CREATE POLICY {names.DelimitIdentifier(PolicyName(privilege))} ON {source.Table} FOR {privilege} TO {target} USING ({predicate}) {(privilege == "UPDATE" ? $"WITH CHECK ({predicate})" : "")}", token);
                    await ExecuteAsync(context, $"""
                        DO $stamp$ BEGIN
                            EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',p.polname,{Text(source.Table)},
                                {Text(PolicyStamp(enrollment.GlobalLogin, privilege))} || pg_catalog.md5(p.polqual::text) || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),''))
                                FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(PolicyName(privilege))});
                        END $stamp$;
                        """, token);
                    RequireOriginal();
                }
                if (!await ScalarAsync(context, Catalogue(context, source, enrollment, true), token)) throw new UnauthorizedAccessException(NotReady);
                RequireOriginal();
            }
            await context.Database.CurrentTransaction!.ReleaseSavepointAsync(savepoint, token);
            saved = false;
            RequireOriginal();
            if (owned is not null) await owned.CommitAsync(token);
        }
        catch
        {
            if (saved && context.Database.CurrentTransaction is { } transaction)
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
        finally { connection.StateChange -= Changed; }
    }

    internal async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment, CancellationToken token)
    {
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException(NotReady);
        Validate(enrollment);
        context.RequireBoundTenantLease();
        context.ValidateTenantLease();
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId is not null || context.CurrentUserService is not null)
            throw new UnauthorizedAccessException(NotReady);
        if (context.Database.GetDbConnection().State != ConnectionState.Open) await context.Database.OpenConnectionAsync(token);
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) { if (change.CurrentState != ConnectionState.Open) interrupted = true; }
        connection.StateChange += Changed;
        try
        {
            if (!await ScalarAsync(context, RowPredicate(enrollment.GlobalLogin), token)
                || !await ScalarAsync(context, Catalogue(context, Map(context), enrollment, true), token))
                throw new UnauthorizedAccessException(NotReady);
            context.ValidateTenantLease();
            if (interrupted || context.ContextId != contextId || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend || context.TenantFilterTenantId is not null || context.CurrentUserService is not null
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())) throw new UnauthorizedAccessException(NotReady);
        }
        finally { connection.StateChange -= Changed; }
    }

    internal string RetiredName(ExploreDbContext context, string suffix)
    {
        var source = Map(context);
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(source.Name + suffix, source.Schema);
    }

    internal string PolicyException(ExploreDbContext context, string globalLogin)
    {
        var source = Map(context);
        return $"n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({string.Join(" OR ", Privileges.Select(privilege => PolicyPredicate(source, globalLogin, privilege)))})";
    }

    private string[] Privileges => updateProperties.Length == 0 ? ["SELECT"] : ["SELECT", "UPDATE"];
    private string PolicyName(string privilege) => policyPrefix + (privilege == "SELECT" ? "_read" : "_usage");
    private string PolicyStamp(string login, string privilege) => policyPrefix + ":ef:v1:" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(role + "|" + privilege + "|" + RowPredicate(login)))) + ":";
    private string Absent(Source source) => $"NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={Text(role)}) AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND polname IN ({string.Join(',', Privileges.Select(privilege => Text(PolicyName(privilege))))}))";

    private string Catalogue(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string roleId = RoleId(role);
        string expected = string.Join(',', source.Columns.Values.Select(column => $"({Text(column.Name)},'SELECT')")
            .Concat(updateProperties.Select(property => $"({Text(source.Columns[property].Name)},'UPDATE')")));
        string AllowedColumns(string login)
        {
            string global = $"""
                ({Boolean(login == enrollment.GlobalLogin)} AND a.attnum>0
                    AND (r.rolname={Text(role)} OR (r.rolname={Text(enrollment.GlobalLogin)}
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) direct_acl
                            WHERE direct_acl.grantee=r.oid AND direct_acl.privilege_type=permission.privilege)))
                    AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=permission.privilege)
                    AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee={roleId}
                        AND acl.grantor=(SELECT relowner FROM pg_catalog.pg_class WHERE oid=a.attrelid)
                        AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))
                """;
            string runtime = (type == typeof(ExternalApiKey) || type == typeof(AdmissionScannerCapability)) && login == enrollment.RuntimeLogin
                ? PostgresTenantApiKeyStoragePermissions.RuntimeColumnException(context, enrollment,
                    "r.oid", "a.attrelid", "a.attname", "permission.privilege", type) : "false";
            return $"(({global}) OR ({runtime}))";
        }
        string callers = string.Join(" AND ", new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin }.Select(login => $"""
            NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({RoleId(login)},r.oid,'MEMBER')
                AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                    OR pg_catalog.has_schema_privilege(r.oid,{Text(source.Schema)},'CREATE')
                    OR r.oid=(SELECT relowner FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass({Text(source.Table)}))
                    OR pg_catalog.has_table_privilege(r.oid,{Text(source.Table)},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                        WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum<>0 AND NOT a.attisdropped
                            AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                OR (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege)
                                    AND NOT ({AllowedColumns(login)}))))))
            """));
        string installed = complete ? $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={roleId} AND NOT rolcanlogin AND NOT rolinherit
                AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication)
            AND (SELECT count(*)=1 FROM pg_catalog.pg_auth_members WHERE roleid={roleId} OR member={roleId})
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={roleId} AND member={RoleId(enrollment.GlobalLogin)}
                AND inherit_option AND NOT set_option AND NOT admin_option)
            AND pg_catalog.has_schema_privilege({roleId},{Text(source.Schema)},'USAGE')
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname=e.column_name AND a.attnum>0
                    AND acl.grantee={roleId} AND acl.grantor=(SELECT relowner FROM pg_catalog.pg_class WHERE oid=a.attrelid)
                    AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
            AND ({string.Join(" AND ", Privileges.Select(privilege => $"EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(source, enrollment.GlobalLogin, privilege)})"))})
            """ : Absent(source);
        string retired = string.Join(" AND ", retiredSignatures.Select(signature =>
        {
            int separator = signature.IndexOf('(');
            string name = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(source.Name + signature[..separator], source.Schema);
            return $"pg_catalog.to_regprocedure({Text(name + signature[separator..])}) IS NULL";
        }));
        return $"""
            (WITH expected(column_name,privilege) AS (VALUES {expected})
            SELECT ({SourcePredicate(context, source, enrollment.MigratorLogin)})
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.GlobalLogin)} AND rolcanlogin)
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.RuntimeLogin)} AND rolcanlogin)
                AND NOT pg_catalog.pg_has_role({RoleId(enrollment.GlobalLogin)},{RoleId(enrollment.RuntimeLogin)},'MEMBER')
                AND NOT pg_catalog.pg_has_role({RoleId(enrollment.RuntimeLogin)},{RoleId(enrollment.GlobalLogin)},'MEMBER')
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN ({string.Join(',', obsoleteRoles.Select(Text))}))
                AND ({retired}) AND ({callers}) AND ({installed})
                AND ({PostgresTenantApiKeyStoragePermissions.CompleteManifest(context, enrollment)})
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND (0::oid=ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) policy_role(oid)
                        WHERE CASE WHEN policy_role.oid=0 THEN false ELSE pg_catalog.pg_has_role({RoleId(enrollment.GlobalLogin)},policy_role.oid,'MEMBER') END))
                    AND NOT (p.polname='tenant_isolation' OR ({string.Join(" OR ", Privileges.Select(privilege => PolicyPredicate(source, enrollment.GlobalLogin, privilege)))}))))
            """;
    }

    private string SourcePredicate(ExploreDbContext context, Source source, string migrator)
    {
        var entity = context.Model.FindEntityType(type)!;
        var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model).Single(table => table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        string shape = string.Join(" AND ", source.Columns.Values.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attnum>0
                AND NOT a.attisdropped AND a.attname={Text(column.Name)} AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                AND a.attnotnull={Boolean(column.Required)} AND a.attgenerated='')
            """));
        string key = string.Join(',', entity.FindPrimaryKey()!.Properties.Select(property => Text(source.Columns[property.Name].Name)));
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)}) AND c.relkind='r' AND c.relpersistence='p' AND NOT c.relispartition
                    AND c.relowner={RoleId(migrator)} AND n.nspowner={RoleId(migrator)} AND c.relrowsecurity AND c.relforcerowsecurity)
            AND (SELECT count(*)={source.Columns.Count} FROM pg_catalog.pg_attribute WHERE attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND attnum>0 AND NOT attisdropped)
            AND ({shape})
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_index i WHERE i.indrelid=pg_catalog.to_regclass({Text(source.Table)}) AND i.indisprimary
                AND i.indisunique AND i.indisvalid AND i.indisready AND i.indimmediate AND i.indexprs IS NULL AND i.indpred IS NULL
                AND ARRAY(SELECT a.attname FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(id,ord)
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.id ORDER BY k.ord)=ARRAY[{key}]::name[])
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, source.Schema))}
                    || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger WHERE tgrelid=pg_catalog.to_regclass({Text(source.Table)}) AND NOT tgisinternal)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=pg_catalog.to_regclass({Text(source.Table)}))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=pg_catalog.to_regclass({Text(source.Table)}) OR inhparent=pg_catalog.to_regclass({Text(source.Table)}))
            """;
    }

    private string PolicyPredicate(Source source, string login, string privilege) => $"""
        p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(PolicyName(privilege))} AND p.polcmd='{(privilege == "SELECT" ? "r" : "w")}'
        AND p.polpermissive AND p.polroles=ARRAY[{RoleId(role)}] AND p.polqual IS NOT NULL
        AND {(privilege == "SELECT" ? "p.polwithcheck IS NULL" : "p.polwithcheck IS NOT NULL")}
        AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)=({ExpectedPolicyExpression(login)})
        {(privilege == "SELECT" ? "" : $"AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)=({ExpectedPolicyExpression(login)})")}
        AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PolicyStamp(login, privilege))}
            || pg_catalog.md5(p.polqual::text) || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
        """;

    private static string ExpectedPolicyExpression(string login)
    {
        // Expected standard-conforming deparser text is distinct from its enclosing executable E-literal.
        string roleLiteral = "'" + login.Replace("'", "''", StringComparison.Ordinal) + "'";
        return Text("((SESSION_USER = CURRENT_USER) AND (CURRENT_USER = " + roleLiteral
            + "::name) AND (current_setting('role'::text) = ANY (ARRAY['none'::text, (SESSION_USER)::text])) AND (current_setting('session_replication_role'::text) = 'origin'::text) AND (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text) IS NULL))");
    }

    private Source Map(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(type) ?? throw new InvalidOperationException(NotReady);
        if (!entity.GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(properties))
            throw new InvalidOperationException("authentication_source_fixed_inventory_changed");
        var mapped = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
        return new(schema, mapped.Name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(mapped.Name, schema), properties.ToDictionary(property => property, property =>
        {
            var value = entity.FindProperty(property)!;
            return new Column(value.GetColumnName(mapped)!, value.GetRelationalTypeMapping().StoreType, !value.IsNullable);
        }, StringComparer.Ordinal));
    }

    private void Validate(PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.MigratorLogin, enrollment.RuntimeLogin, enrollment.GlobalLogin, enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }
            .Any(login => obsoleteRoles.Contains(login) || login == role)) throw new InvalidOperationException("authentication_source_enrollment_collision");
    }

    private static string RoleId(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Session(string login) => $"session_user=current_user AND current_user={Text(login)}::name AND pg_catalog.current_setting('role') IN ('none',session_user::text) AND pg_catalog.current_setting('session_replication_role')='origin'";
    private static string RowPredicate(string login) => Session(login) + " AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";
    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static async Task<bool> ScalarAsync(ExploreDbContext context, string expression, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT COALESCE((" + expression + "),false)";
        return await command.ExecuteScalarAsync(token) is true;
    }
    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }
    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(string Schema, string Name, string Table, Dictionary<string, Column> Columns);
}
