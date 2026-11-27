using System.Data;
using System.Data.Common;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Explore.Persistence.Security;

/// <summary>
/// A column SELECT on PayloadJson cannot distinguish branding from directory identity rows.
/// This barrier view restricts the document key; the existing forced tenant policy restricts
/// its unprivileged owner. Neither the view nor its installer decides export authorization.
/// </summary>
public static class PostgresTenantBrandingReadContract
{
    public const string OwnerRole = "event_tenant_branding_read_owner";
    public const string ViewName = "tenant_branding_read";
    private const string Drift = "tenant_branding_read_contract_drift";
    private const string Key = SettingsDocumentKeys.Tenant.Branding;
    private static readonly string[] Fields =
        ["Id", "TenantId", "DocumentKey", "SchemaVersion", "DefaultsVersion", "PayloadJson", "ConcurrencyStamp"];

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        if (context.TenantFilterTenantId is not null || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("tenant_branding_installer_scope");
        var sql = new ContractSql(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(ct) : null;
        // One statement: a failed postcondition rolls back all grants even in a caller's transaction.
        string body = $"""
            DECLARE signature text; previous_role text := pg_catalog.current_setting('role');
            BEGIN
                IF NOT ({Identity(enrollment.MigratorLogin)})
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NOT NULL THEN
                    RAISE EXCEPTION 'tenant_branding_installer_identity' USING ERRCODE='42501';
                END IF;
                IF pg_catalog.to_regrole('{OwnerRole}') IS NULL AND pg_catalog.to_regclass({Text(sql.View)}) IS NULL THEN
                    CREATE ROLE {OwnerRole} NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION;
                    GRANT {OwnerRole} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE;
                    GRANT USAGE, CREATE ON SCHEMA {sql.SchemaIdentifier} TO {OwnerRole};
                    GRANT SELECT ({sql.ColumnList}) ON {sql.Table} TO {OwnerRole};
                    GRANT USAGE ON SCHEMA {sql.SchemaIdentifier} TO {sql.Quote(enrollment.RuntimeLogin)};
                    SET LOCAL ROLE {OwnerRole};
                    CREATE VIEW {sql.View} WITH (security_barrier=true) AS
                        SELECT {sql.ColumnList} FROM {sql.Table}
                        WHERE {sql.KeyColumn} COLLATE pg_catalog."C" OPERATOR(pg_catalog.=) {Text(Key)}::pg_catalog.text;
                    GRANT SELECT ON {sql.View} TO {sql.Quote(enrollment.RuntimeLogin)};
                    SELECT {RuleSignature} INTO STRICT signature FROM pg_catalog.pg_rewrite rw
                        WHERE rw.ev_class=pg_catalog.to_regclass({Text(sql.View)}) AND rw.rulename='_RETURN';
                    PERFORM pg_catalog.set_config('role',previous_role,true);
                    REVOKE CREATE ON SCHEMA {sql.SchemaIdentifier} FROM {OwnerRole};
                    EXECUTE 'COMMENT ON ROLE {OwnerRole} IS ' || pg_catalog.quote_literal(signature);
                END IF;
                IF ({sql.Catalogue(enrollment)}) IS NOT TRUE THEN
                    RAISE EXCEPTION '{Drift}' USING ERRCODE='42501';
                END IF;
            END
            """;
        await using var command = Command(context, "DO " + Text(body));
        await command.ExecuteNonQueryAsync(ct);
        if (owned is not null) await owned.CommitAsync(ct);
    }

    public static async Task<IReadOnlyList<TenantSettingsDocument>> ReadAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, Guid tenantId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        enrollment.Validate();
        var contextId = context.ContextId;
        var lease = context.TenantContext;
        void RequireTenant()
        {
            context.RequireBoundTenantLease();
            if (!context.Database.IsNpgsql() || context.ContextId != contextId || tenantId == Guid.Empty
                || !ReferenceEquals(lease, context.TenantContext)
                || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner)
                throw new UnauthorizedAccessException("tenant_branding_selected_tenant_required");
        }
        RequireTenant();
        var sql = new ContractSql(context);
        var connection = RelationalConnectionIdentity.Capture(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(ct) : null;
        var transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        int backend = connection.PostgresProcessId;
        void RequireOriginal()
        {
            RequireTenant();
            if (!connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.PostgresProcessId != backend || !connection.Owns(transaction)
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction))
                throw new UnauthorizedAccessException("tenant_branding_read_owner_changed");
        }
        RequireOriginal();
        // Referencing the view here retains its relation lock through the projected read, even
        // for a tenant with no branding row. A missing or unready view is never an empty export.
        await using var proof = Command(context, $"""
            SELECT ({Identity(enrollment.RuntimeLogin)})
                AND pg_catalog.current_setting('app.current_tenant_id',true)={Text(tenantId.ToString("D"))}
                AND ({sql.Catalogue(enrollment)})
                AND NOT EXISTS (SELECT 1 FROM {sql.View} LIMIT 0)
            """);
        if (await proof.ExecuteScalarAsync(ct) is not true) throw new UnauthorizedAccessException(Drift);
        RequireOriginal();
        string source = $"SELECT {sql.ColumnList} FROM {sql.View}";
        var documents = await new NpgsqlRetryingExecutionStrategy(context, maxRetryCount: 0).ExecuteAsync(
            () => context.TenantSettingsDocuments.FromSqlRaw(source).AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.DocumentKey == Key)
            .Select(d => new TenantSettingsDocument
            {
                Id = d.Id, TenantId = d.TenantId, Tenant = null!, DocumentKey = d.DocumentKey,
                SchemaVersion = d.SchemaVersion, DefaultsVersion = d.DefaultsVersion,
                PayloadJson = d.PayloadJson, ConcurrencyStamp = d.ConcurrencyStamp
            }).ToListAsync(ct));
        RequireOriginal();
        if (owned is not null) await owned.CommitAsync(ct);
        RequireTenant();
        return documents;
    }

    private const string RuleSignature =
        "'tenant-branding-read:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(rw.ev_action::text,'UTF8')),'hex')";
    private static string Identity(string login) =>
        $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string Text(string text) => "E'" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal) + "'";
    private static DbCommand Command(ExploreDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = sql;
        if (context.Database.GetCommandTimeout() is { } timeout) command.CommandTimeout = timeout;
        return command;
    }

    private sealed class ContractSql
    {
        private readonly ISqlGenerationHelper _names;
        private readonly string[] _columns;
        private readonly string _expectedShape;
        private readonly string _policyPrefix;
        internal string SchemaIdentifier { get; }
        internal string View { get; }
        internal string Table { get; }
        internal string ColumnList => string.Join(',', _columns.Select(Quote));
        internal string KeyColumn => Quote(_columns[2]);
        internal string Quote(string identifier) => _names.DelimitIdentifier(identifier);

        internal ContractSql(ExploreDbContext context)
        {
            if (ConfigurationManifestCatalog.TenantDocuments.Values.Where(entry =>
                    entry.Storage == ConfigurationManifestDocumentStorage.TenantSettingsDocument)
                .Select(entry => entry.DocumentKey).ToArray() is not [Key])
                throw new InvalidOperationException(Drift);
            var entity = context.Model.FindEntityType(typeof(TenantSettingsDocument))!;
            string table = entity.GetTableName()!;
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
                ?? entity.GetSchema() ?? "public";
            _names = context.GetService<ISqlGenerationHelper>();
            SchemaIdentifier = Quote(schema);
            View = _names.DelimitIdentifier(ViewName, schema);
            Table = _names.DelimitIdentifier(table, schema);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var properties = Fields.Select(field => entity.FindProperty(field)!).ToArray();
            _columns = properties.Select(p => p.GetColumnName(store)!).ToArray();
            _expectedShape = string.Join(',', properties.Select(p =>
                $"({Text(p.GetColumnName(store)!)},{Text(p.GetRelationalTypeMapping().StoreType)})"));
            var tenant = context.GetTenantTableInventory().Single(t =>
                t.TableName == table && (t.Schema ?? schema) == schema);
            if (tenant.AllowsNullTenant || tenant.ParentJoin is not null) throw new InvalidOperationException(Drift);
            _policyPrefix = PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, schema);
        }

        internal string Catalogue(PostgresNativeQueueEnrollment enrollment)
        {
            string columns = $"ARRAY[{string.Join(',', _columns.Select(Text))}]::text[]";
            string condition = $"""
                '(' || pg_catalog.quote_ident({Text(_columns[1])}) || ' = (NULLIF(current_setting(''app.current_tenant_id''::text, true), ''''::text))::uuid)'
                """;
            return $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                    JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                    JOIN pg_catalog.pg_class v ON v.relnamespace=n.oid AND v.oid=pg_catalog.to_regclass({Text(View)})
                    JOIN pg_catalog.pg_roles o ON o.oid=v.relowner AND o.rolname='{OwnerRole}'
                    JOIN pg_catalog.pg_roles m ON m.oid=t.relowner AND m.rolname={Text(enrollment.MigratorLogin)}
                    CROSS JOIN pg_catalog.pg_roles r
                    WHERE t.oid=pg_catalog.to_regclass({Text(Table)}) AND r.rolname={Text(enrollment.RuntimeLogin)}
                    AND t.relkind='r' AND t.relpersistence='p' AND t.relrowsecurity AND t.relforcerowsecurity
                    AND v.relkind='v' AND v.relpersistence='p' AND v.reloptions=ARRAY['security_barrier=true']
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhparent=t.oid OR inhrelid=t.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=t.oid OR ev_class=v.oid AND rulename<>'_RETURN')
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite rw WHERE rw.ev_class=v.oid AND rw.rulename='_RETURN'
                        AND pg_catalog.shobj_description(o.oid,'pg_authid')=({RuleSignature})
                        AND EXISTS (SELECT 1 FROM pg_catalog.pg_depend dep WHERE dep.classid='pg_catalog.pg_rewrite'::regclass
                            AND dep.objid=rw.oid AND dep.refclassid='pg_catalog.pg_class'::regclass AND dep.refobjid=t.oid))
                    AND ARRAY(SELECT attname::text FROM pg_catalog.pg_attribute WHERE attrelid=v.oid
                        AND attnum>0 AND NOT attisdropped ORDER BY attnum)={columns}
                    AND NOT EXISTS (SELECT 1 FROM (VALUES {_expectedShape}) expected(column_name,store_type)
                        WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid
                            AND a.attname=expected.column_name AND a.attnum>0 AND a.attnotnull AND NOT a.attisdropped
                            AND a.attgenerated='' AND a.attidentity=''
                            AND pg_catalog.format_type(a.atttypid,a.atttypmod)=expected.store_type))
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname='tenant_isolation'
                        AND p.polpermissive AND p.polcmd='*' AND p.polroles=ARRAY[0::oid]
                        AND pg_catalog.pg_get_expr(p.polqual,t.oid)=({condition})
                        AND pg_catalog.pg_get_expr(p.polwithcheck,t.oid)=({condition})
                        AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(_policyPrefix)}
                            ||pg_catalog.md5(p.polqual::text)||':'||pg_catalog.md5(p.polwithcheck::text)
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend dep WHERE dep.classid='pg_catalog.pg_policy'::regclass
                            AND dep.objid=p.oid AND dep.refclassid IN ('pg_catalog.pg_proc'::regclass,'pg_catalog.pg_operator'::regclass)
                            AND NOT (dep.refclassid='pg_catalog.pg_proc'::regclass AND EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
                                WHERE f.oid=dep.refobjid AND f.pronamespace='pg_catalog'::regnamespace)
                                OR dep.refclassid='pg_catalog.pg_operator'::regclass AND EXISTS (SELECT 1 FROM pg_catalog.pg_operator op
                                WHERE op.oid=dep.refobjid AND op.oprnamespace='pg_catalog'::regnamespace))))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy WHERE polrelid=t.oid AND polname<>'tenant_isolation'
                        AND (0::oid=ANY(polroles) OR o.oid=ANY(polroles)))
                    AND NOT (o.rolcanlogin OR o.rolinherit OR o.rolsuper OR o.rolbypassrls OR o.rolreplication OR o.rolcreatedb OR o.rolcreaterole)
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid=o.oid AND member=m.oid AND set_option)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE member=o.oid OR
                        roleid=o.oid AND (member<>m.oid OR inherit_option))
                    AND r.rolcanlogin AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid=r.oid)
                    AND NOT pg_catalog.pg_has_role(r.oid,{Text(enrollment.GlobalLogin)},'MEMBER')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles p WHERE pg_catalog.pg_has_role(r.oid,p.oid,'MEMBER')
                        AND (p.oid IN (m.oid,o.oid) OR p.rolsuper OR p.rolbypassrls OR p.rolcreatedb OR p.rolcreaterole OR p.rolreplication
                            OR pg_catalog.has_schema_privilege(p.oid,n.oid,'CREATE')
                            OR pg_catalog.has_table_privilege(p.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("p.oid","t.oid",false)}
                            OR pg_catalog.has_any_column_privilege(p.oid,t.oid,'SELECT,INSERT,UPDATE,REFERENCES')))
                    AND pg_catalog.has_schema_privilege(r.oid,n.oid,'USAGE') AND pg_catalog.has_schema_privilege(o.oid,n.oid,'USAGE')
                    AND NOT pg_catalog.has_schema_privilege(o.oid,n.oid,'CREATE,USAGE WITH GRANT OPTION')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace ns WHERE pg_catalog.has_schema_privilege(o.oid,ns.oid,'CREATE'))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_database db WHERE pg_catalog.has_database_privilege(o.oid,db.oid,'CREATE'))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class other JOIN pg_catalog.pg_namespace ns ON ns.oid=other.relnamespace
                        WHERE ns.nspname NOT IN ('information_schema','pg_catalog') AND ns.nspname !~ '^pg_(toast|temp)'
                        AND other.oid NOT IN (v.oid,t.oid) AND (other.relkind IN ('r','p','v','m','f') AND
                            (pg_catalog.has_table_privilege(o.oid,other.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                             OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("o.oid","other.oid",false)}
                             OR pg_catalog.has_any_column_privilege(o.oid,other.oid,'SELECT,INSERT,UPDATE,REFERENCES'))
                            OR other.relkind='S' AND pg_catalog.has_sequence_privilege(o.oid,other.oid,'USAGE,SELECT,UPDATE')))
                    AND NOT pg_catalog.has_table_privilege(o.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    AND NOT ({PostgresPrivilegeGuard.MaintainPrivilegeSql("o.oid","t.oid",false)})
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                        AND (pg_catalog.has_column_privilege(o.oid,t.oid,a.attnum,'SELECT')<>(a.attname::text=ANY({columns}))
                            OR pg_catalog.has_column_privilege(o.oid,t.oid,a.attnum,'SELECT WITH GRANT OPTION,INSERT,UPDATE,REFERENCES')))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.unnest({columns}) col WHERE NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE a.attrelid=t.oid AND a.attname=col AND acl.grantor=m.oid AND acl.grantee=o.oid
                            AND acl.privilege_type='SELECT' AND NOT acl.is_grantable))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_shdepend d WHERE d.refclassid='pg_catalog.pg_authid'::regclass
                        AND d.refobjid=o.oid AND d.deptype IN ('o','a') AND d.classid<>'pg_catalog.pg_auth_members'::regclass AND NOT
                            (d.dbid=(SELECT oid FROM pg_catalog.pg_database WHERE datname=pg_catalog.current_database())
                             AND ((d.classid='pg_catalog.pg_class'::regclass AND (d.objid=v.oid OR d.objid=t.oid AND d.deptype='a'))
                               OR d.classid='pg_catalog.pg_namespace'::regclass AND d.objid=n.oid AND d.deptype='a')))
                    AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(v.relacl) acl WHERE acl.grantee=r.oid AND acl.privilege_type='SELECT')
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(v.relacl) acl WHERE acl.grantor<>o.oid
                        OR acl.grantee<>o.oid AND (acl.grantee<>r.oid OR acl.privilege_type<>'SELECT' OR acl.is_grantable))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute WHERE attrelid=v.oid AND attacl IS NOT NULL))
                """;
        }
    }
}
