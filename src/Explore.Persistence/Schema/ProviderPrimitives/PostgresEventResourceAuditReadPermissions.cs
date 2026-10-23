using System.Data;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Storage support for Global audit-owner discovery, without audit content or deletion authority.</summary>
public static class PostgresEventResourceAuditReadPermissions
{
    private const string ReadPolicy = "event_resource_audit_global_read";
    private const string ReadRestriction = "event_resource_audit_global_boundary";
    private const string Drift = "event_resource_audit_read_permissions_drift";
    private const string NotReady = "event_resource_audit_read_permissions_not_ready";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var audit = Audit(context);
        var tenant = Map<Tenant>(context, nameof(Tenant.Id));
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var physical = transaction.GetDbTransaction();
        var connection = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.ContextId != contextId || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || !connection.Owns(physical))
                throw new UnauthorizedAccessException("event_resource_audit_installer_owner_changed");
        }
        async Task<bool> CheckAsync(string predicate)
        {
            RequireOriginal();
            bool result = await IsTrueAsync(context, predicate, cancellationToken);
            RequireOriginal();
            return result;
        }
        async Task ExecuteAsync(string sql)
        {
            RequireOriginal();
            await using var command = connection.Connection.CreateCommand();
            command.Transaction = physical;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
            RequireOriginal();
        }
        connection.StateChange += Changed;
        string savepoint = "audit_read_permissions_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await CheckAsync(Identity(enrollment.MigratorLogin)))
                    throw new UnauthorizedAccessException("event_resource_audit_installer_wrong_identity");
                bool complete = await CheckAsync(Manifest(audit, tenant, enrollment, complete: true));
                if (!complete)
                {
                    if (!await CheckAsync($"({Manifest(audit, tenant, enrollment, complete: false)}) AND ({Absent(audit, enrollment)})"))
                        throw new InvalidOperationException(Drift);
                    var names = context.GetService<ISqlGenerationHelper>();
                    string global = names.DelimitIdentifier(enrollment.GlobalLogin);
                    await ExecuteAsync($"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(audit.Schema)} TO {global}");
                    await ExecuteAsync($"GRANT SELECT ({names.DelimitIdentifier(audit.Column)}) ON {audit.Table} TO {global}");
                    foreach (string policy in new[] { ReadPolicy, ReadRestriction })
                        await ExecuteAsync($"CREATE POLICY {names.DelimitIdentifier(policy)} ON {audit.Table} AS {(policy == ReadPolicy ? "PERMISSIVE" : "RESTRICTIVE")} FOR SELECT TO {global} USING ({PolicySession(enrollment.GlobalLogin)})");
                    if (!await CheckAsync(Manifest(audit, tenant, enrollment, complete: true)))
                        throw new InvalidOperationException(Drift);
                }
            }
            catch
            {
                // Never roll back a replacement renter's transaction.
                if (context.ContextId == contextId && connection.Matches(context)
                    && connection.State == ConnectionState.Open && connection.BackendProcessId == backend
                    && ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                    && connection.Owns(physical))
                {
                    await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                    await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
                }
                throw;
            }
            RequireOriginal();
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
            RequireOriginal();
            if (owned is not null) await owned.CommitAsync(cancellationToken);
        }
        finally { connection.StateChange -= Changed; }
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null)
            throw new UnauthorizedAccessException("event_resource_audit_read_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var tenantIdentity = context.TenantContext;
        var contextId = context.ContextId;
        var connection = RelationalConnectionIdentity.Capture(context);
        var physical = context.Database.CurrentTransaction?.GetDbTransaction();
        int? backend = connection.State == ConnectionState.Open ? connection.BackendProcessId : null;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || !ReferenceEquals(tenantIdentity, context.TenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || physical is not null && !connection.Owns(physical))
                throw new UnauthorizedAccessException("event_resource_audit_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            // Reject identity/session substitution before evaluating protected relation metadata.
            if (!await IsTrueAsync(context, SealedSession(enrollment.GlobalLogin), cancellationToken))
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            if (!await IsTrueAsync(context, Manifest(Audit(context), Map<Tenant>(context, nameof(Tenant.Id)),
                    enrollment, complete: true), cancellationToken))
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    /// <summary>Pure policy description for the existing Global connection admission; no grants or session acquisition.</summary>
    internal static string GlobalPolicyException(ExploreDbContext context, string enrolledGlobalLogin)
    {
        var audit = Audit(context);
        return $"COALESCE((session_user={Text(enrolledGlobalLogin)} AND current_user=session_user AND n.nspname={Text(audit.Schema)} AND c.relname={Text(audit.Name)} AND ({PolicyPredicate(audit, enrolledGlobalLogin, ReadPolicy)})),false)";
    }

    private static string Manifest(Source audit, Source tenant, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string global = Text(enrollment.GlobalLogin), migrator = Text(enrollment.MigratorLogin);
        string relation = Text(audit.Table);
        string policies = $"({PolicyPredicate(audit, enrollment.GlobalLogin, ReadPolicy)}) OR ({PolicyPredicate(audit, enrollment.GlobalLogin, ReadRestriction)})";
        string required = complete ? $"""
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({relation}) AND a.attname={Text(audit.Column)}
                    AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                    AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={migrator})
                    AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
            AND pg_catalog.has_schema_privilege({global},{Text(audit.Schema)},'USAGE')
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(audit, enrollment.GlobalLogin, ReadPolicy)})
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(audit, enrollment.GlobalLogin, ReadRestriction)})
            """ : "";
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={global} AND rolcanlogin)
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={migrator} AND rolcanlogin)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                WHERE roleid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global}))
            AND ({Shape(audit, enrollment.MigratorLogin)}) AND ({Shape(tenant, enrollment.MigratorLogin)})
            AND pg_catalog.has_column_privilege({global},{Text(tenant.Table)},{Text(tenant.Column)},'SELECT')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR r.rolname={migrator}
                        OR pg_catalog.has_schema_privilege(r.oid,{Text(audit.Schema)},'CREATE')
                        OR pg_catalog.has_table_privilege(r.oid,{relation},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            WHERE a.attrelid=pg_catalog.to_regclass({relation}) AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'INSERT,UPDATE,REFERENCES')
                                    OR pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT WITH GRANT OPTION')
                                    OR ((r.rolname<>{global} OR a.attname<>{Text(audit.Column)})
                                        AND pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT'))))))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(t.relacl,pg_catalog.acldefault('r',t.relowner))) acl
                WHERE t.oid=pg_catalog.to_regclass({relation}) AND acl.grantee=0)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({relation}) AND a.attnum<>0 AND NOT a.attisdropped
                    AND (acl.grantee=0 OR (acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                        AND (a.attname<>{Text(audit.Column)} OR acl.privilege_type<>'SELECT'
                            OR acl.is_grantable OR acl.grantor<>(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={migrator})))))
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({relation})
                AND c.relrowsecurity AND c.relforcerowsecurity)
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({relation})
                AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(audit.TenantFingerprint!)}
                    || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({relation})
                AND p.polname<>'tenant_isolation'
                AND (p.polname IN ({Text(ReadPolicy)},{Text(ReadRestriction)}) OR 0=ANY(p.polroles)
                    OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                        WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER') AND r.oid=ANY(p.polroles)))
                AND NOT ({policies}))
            {required}
            """;
    }

    private static string Absent(Source audit, PostgresNativeQueueEnrollment enrollment) => $"""
        NOT pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},{Text(audit.Table)},'SELECT,INSERT,UPDATE,REFERENCES')
        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(audit.Table)})
            AND p.polname IN ({Text(ReadPolicy)},{Text(ReadRestriction)}))
        """;

    private static string Shape(Source source, string migrator) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)})
            AND c.relkind='r' AND NOT c.relispartition
            AND c.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(migrator)})
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid
                AND a.attname={Text(source.Column)} AND a.attnum>0 AND NOT a.attisdropped
                AND a.atttypid='pg_catalog.uuid'::regtype AND a.attnotnull)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid=c.oid OR i.inhparent=c.oid)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))
        """;

    private static string PolicyPredicate(Source audit, string login, string policy)
    {
        string role = "'" + login.Replace("'", "''", StringComparison.Ordinal) + "'";
        string expression = $"((SESSION_USER = CURRENT_USER) AND (CURRENT_USER = {role}::name) AND (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text) IS NULL))";
        return $"""
            COALESCE((p.polrelid=pg_catalog.to_regclass({Text(audit.Table)}) AND p.polname={Text(policy)}
            AND p.polcmd='r' AND p.polpermissive={(policy == ReadPolicy ? "true" : "false")}
            AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
            AND p.polqual IS NOT NULL AND p.polwithcheck IS NULL
            AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={Text(expression)}),false)
            """;
    }

    private static string Identity(string login) => $"session_user=current_user AND current_user={Text(login)}::name AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string SealedSession(string login) => $"{Identity(login)} AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";
    private static string PolicySession(string login) => $"SESSION_USER=CURRENT_USER AND CURRENT_USER={Text(login)}::name AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";

    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(token) is true;
    }

    private static Source Audit(ExploreDbContext context)
    {
        var source = Map<EventResourceAuditEntry>(context, nameof(EventResourceAuditEntry.TenantId));
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Single(table => table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        if (metadata.AllowsNullTenant || metadata.ParentJoin is not null)
            throw new InvalidOperationException("event_resource_audit_tenant_model_invalid");
        return source with { TenantFingerprint = PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, source.Schema) };
    }

    private static Source Map<T>(ExploreDbContext context, string propertyName)
    {
        var entity = context.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException("event_resource_audit_model_missing");
        string name = entity.GetTableName() ?? throw new InvalidOperationException("event_resource_audit_table_missing");
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        var property = entity.FindProperty(propertyName) ?? throw new InvalidOperationException("event_resource_audit_property_missing");
        if (property.IsShadowProperty() || property.ClrType != typeof(Guid) || property.IsNullable
            || entity.BaseType is not null || entity.GetDerivedTypes().Any())
            throw new InvalidOperationException("event_resource_audit_model_invalid");
        string column = property.GetColumnName(StoreObjectIdentifier.Table(name, entity.GetSchema()))!;
        return new Source(name, schema, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema), column, null);
    }

    private sealed record Source(string Name, string Schema, string Table, string Column, string? TenantFingerprint);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
