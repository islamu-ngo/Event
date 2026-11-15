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

/// <summary>Fixed Global columns for operational effect aggregates, excluding payloads and mutation authority.</summary>
public static class PostgresEffectAggregateHealthReadPermissions
{
    private const string ReadPolicy = "effect_health_global_read";
    private const string ReadRestriction = "effect_health_global_boundary";
    private const string Drift = "effect_health_read_permissions_drift";
    private const string NotReady = "effect_health_read_permissions_not_ready";

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        Source[] sources = Sources(context);
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
                throw new UnauthorizedAccessException("effect_health_installer_owner_changed");
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
        string savepoint = "effect_health_read_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await CheckAsync(Identity(enrollment.MigratorLogin)))
                    throw new UnauthorizedAccessException("effect_health_installer_wrong_identity");
                if (!await CheckAsync(Manifest(sources, enrollment, complete: true)))
                {
                    if (!await CheckAsync($"({Manifest(sources, enrollment, complete: false)}) AND ({Absent(sources, enrollment)})"))
                        throw new InvalidOperationException(Drift);
                    var names = context.GetService<ISqlGenerationHelper>();
                    string global = names.DelimitIdentifier(enrollment.GlobalLogin);
                    foreach (Source source in sources)
                    {
                        await ExecuteAsync($"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {global}");
                        string columns = string.Join(",", source.Columns.Select(column => names.DelimitIdentifier(column.Name)));
                        await ExecuteAsync($"GRANT SELECT ({columns}) ON {source.Table} TO {global}");
                        foreach (string policy in new[] { ReadPolicy, ReadRestriction })
                            await ExecuteAsync($"CREATE POLICY {names.DelimitIdentifier(policy)} ON {source.Table} AS {(policy == ReadPolicy ? "PERMISSIVE" : "RESTRICTIVE")} FOR SELECT TO {global} USING ({PolicySession(enrollment.GlobalLogin)})");
                    }
                    if (!await CheckAsync(Manifest(sources, enrollment, complete: true)))
                        throw new InvalidOperationException(Drift);
                }
            }
            catch
            {
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
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("effect_health_instance_owner_required");
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null)
            throw new UnauthorizedAccessException("effect_health_read_enrollment_required");
        enrollment.Validate();
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
                throw new UnauthorizedAccessException("effect_health_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            if (!await IsTrueAsync(context, SealedSession(enrollment.GlobalLogin), cancellationToken))
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
            if (!await IsTrueAsync(context, Manifest(Sources(context), enrollment, complete: true), cancellationToken))
                throw new UnauthorizedAccessException(NotReady);
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    internal static string GlobalPolicyException(ExploreDbContext context, string enrolledGlobalLogin) =>
        "(" + string.Join(" OR ", Sources(context).Select(source =>
            $"COALESCE((session_user={Text(enrolledGlobalLogin)} AND current_user=session_user AND n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({PolicyPredicate(source, enrolledGlobalLogin, ReadPolicy)})),false)")) + ")";

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Manifest(Sources(context), enrollment, complete: true);

    internal static IReadOnlyList<string> GlobalCatalogueRelations(ExploreDbContext context) =>
        Sources(context).Select(source => source.Table).ToArray();

    private static string Manifest(Source[] sources, PostgresNativeQueueEnrollment enrollment, bool complete) =>
        string.Join(" AND ", sources.Select(source => $"({RelationManifest(source, enrollment, complete)})"));

    private static string RelationManifest(Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string global = Text(enrollment.GlobalLogin), migrator = Text(enrollment.MigratorLogin);
        string relation = Text(source.Table);
        string columns = string.Join(",", source.Columns.Select(column => Text(column.Name)));
        string policies = $"({PolicyPredicate(source, enrollment.GlobalLogin, ReadPolicy)}) OR ({PolicyPredicate(source, enrollment.GlobalLogin, ReadRestriction)})";
        string required = complete ? $"""
            AND ({string.Join(" AND ", source.Columns.Select(column => $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({relation}) AND a.attname={Text(column.Name)}
                        AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                        AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={migrator})
                        AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                """))})
            AND pg_catalog.has_schema_privilege({global},{Text(source.Schema)},'USAGE')
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(source, enrollment.GlobalLogin, ReadPolicy)})
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(source, enrollment.GlobalLogin, ReadRestriction)})
            """ : "";
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={global} AND rolcanlogin)
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={migrator} AND rolcanlogin)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                WHERE roleid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global}))
            AND ({Shape(source, enrollment.MigratorLogin)})
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR r.rolname={migrator}
                        OR pg_catalog.has_schema_privilege(r.oid,{Text(source.Schema)},'CREATE')
                        OR (pg_catalog.has_table_privilege(r.oid,{relation},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", relation, false)})
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            WHERE a.attrelid=pg_catalog.to_regclass({relation}) AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'INSERT,UPDATE,REFERENCES')
                                    OR pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT WITH GRANT OPTION')
                                    OR ((r.rolname<>{global} OR a.attname NOT IN ({columns}))
                                        AND pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT'))))))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(t.relacl,pg_catalog.acldefault('r',t.relowner))) acl
                WHERE t.oid=pg_catalog.to_regclass({relation}) AND acl.grantee=0)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({relation}) AND a.attnum<>0 AND NOT a.attisdropped
                    AND (acl.grantee=0 OR (acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                        AND (a.attname NOT IN ({columns}) OR acl.privilege_type<>'SELECT'
                            OR acl.is_grantable OR acl.grantor<>(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={migrator})))))
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({relation})
                AND c.relrowsecurity AND c.relforcerowsecurity)
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({relation})
                AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(source.TenantFingerprint)}
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

    private static string Absent(Source[] sources, PostgresNativeQueueEnrollment enrollment) =>
        string.Join(" AND ", sources.Select(source => $"""
            (NOT pg_catalog.has_any_column_privilege({Text(enrollment.GlobalLogin)},{Text(source.Table)},'SELECT,INSERT,UPDATE,REFERENCES')
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND p.polname IN ({Text(ReadPolicy)},{Text(ReadRestriction)})))
            """));

    private static string Shape(Source source, string migrator) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)})
            AND c.relkind='r' AND NOT c.relispartition
            AND c.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(migrator)})
            AND ({string.Join(" AND ", source.Columns.Select(column => $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid
                    AND a.attname={Text(column.Name)} AND a.attnum>0 AND NOT a.attisdropped
                    AND a.atttypid=pg_catalog.to_regtype({Text(column.StoreType)})
                    AND a.attnotnull={(column.Nullable ? "false" : "true")})
                """))})
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid=c.oid OR i.inhparent=c.oid)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))
        """;

    private static string PolicyPredicate(Source source, string login, string policy)
    {
        string role = "'" + login.Replace("'", "''", StringComparison.Ordinal) + "'";
        string expression = $"((SESSION_USER = CURRENT_USER) AND (CURRENT_USER = {role}::name) AND (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text) IS NULL))";
        return $"""
            COALESCE((p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policy)}
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

    private static Source[] Sources(ExploreDbContext context) =>
    [
        Map<IncomingWebhookEffectOutbox>(context, nameof(IncomingWebhookEffectOutbox.Status),
            nameof(IncomingWebhookEffectOutbox.NextAttemptAt), nameof(IncomingWebhookEffectOutbox.ProcessingLeaseExpiresAt)),
        Map<FairReturnOrchestrationEffect>(context, nameof(FairReturnOrchestrationEffect.StatusId),
            nameof(FairReturnOrchestrationEffect.LastFailureCode), nameof(FairReturnOrchestrationEffect.CreatedAt))
    ];

    private static Source Map<T>(ExploreDbContext context, params string[] properties)
    {
        var entity = context.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException("effect_health_model_missing");
        string name = entity.GetTableName() ?? throw new InvalidOperationException("effect_health_table_missing");
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any())
            throw new InvalidOperationException("effect_health_model_invalid");
        var table = StoreObjectIdentifier.Table(name, entity.GetSchema());
        Column[] columns = properties.Select(propertyName =>
        {
            var property = entity.FindProperty(propertyName) ?? throw new InvalidOperationException("effect_health_property_missing");
            if (property.IsShadowProperty()) throw new InvalidOperationException("effect_health_model_invalid");
            return new Column(property.GetColumnName(table)!, property.GetRelationalTypeMapping().StoreType, property.IsNullable);
        }).ToArray();
        var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Single(candidate => candidate.TableName == name && (candidate.Schema ?? schema) == schema);
        if (tenant.AllowsNullTenant || tenant.ParentJoin is not null)
            throw new InvalidOperationException("effect_health_tenant_model_invalid");
        return new Source(name, schema, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema),
            columns, PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, schema));
    }

    private sealed record Column(string Name, string StoreType, bool Nullable);
    private sealed record Source(string Name, string Schema, string Table, Column[] Columns, string TenantFingerprint);
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
