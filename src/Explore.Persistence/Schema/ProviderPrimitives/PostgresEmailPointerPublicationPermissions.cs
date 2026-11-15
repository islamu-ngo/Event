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

/// <summary>Fixed pointer and pause metadata access; SMTP content, claims and settlement remain excluded.</summary>
public static class PostgresEmailPointerPublicationPermissions
{
    private const string Read = "email_pointer_global_read";
    private const string ReadBoundary = "email_pointer_global_read_boundary";
    private const string Write = "email_pointer_global_marker";
    private const string WriteBoundary = "email_pointer_global_marker_boundary";
    private static readonly string[] PointerProperties =
    [
        nameof(EmailDispatchOutbox.Id), nameof(EmailDispatchOutbox.TenantId),
        nameof(EmailDispatchOutbox.PublishEventId), nameof(EmailDispatchOutbox.Kind),
        nameof(EmailDispatchOutbox.SourceType), nameof(EmailDispatchOutbox.SourceId),
        nameof(EmailDispatchOutbox.EventId), nameof(EmailDispatchOutbox.RegistrationOrderId),
        nameof(EmailDispatchOutbox.Status), nameof(EmailDispatchOutbox.NextAttemptAt),
        nameof(EmailDispatchOutbox.RabbitMqLastPublishAttemptAt), nameof(EmailDispatchOutbox.ContentRedactedAt),
        nameof(EmailDispatchOutbox.CreatedAt), nameof(EmailDispatchOutbox.RabbitMqPublishAttemptCount),
        nameof(EmailDispatchOutbox.IsDeleted)
    ];
    private static readonly string[] MarkerProperties =
    [
        nameof(EmailDispatchOutbox.RabbitMqLastPublishedAt), nameof(EmailDispatchOutbox.RabbitMqLastPublishAttemptAt),
        nameof(EmailDispatchOutbox.RabbitMqPublishAttemptCount), nameof(EmailDispatchOutbox.RabbitMqLastPublishFailureCategory),
        nameof(EmailDispatchOutbox.UpdatedAt)
    ];

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
                throw new UnauthorizedAccessException("email_pointer_installer_owner_changed");
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
        string savepoint = "email_pointer_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await CheckAsync(Identity(enrollment.MigratorLogin)))
                    throw new UnauthorizedAccessException("email_pointer_installer_wrong_identity");
                if (!await CheckAsync(Manifest(context, sources, enrollment, complete: true)))
                {
                    if (!await CheckAsync($"({Manifest(context, sources, enrollment, complete: false)}) AND ({Fresh(context, sources, enrollment)})"))
                        throw new InvalidOperationException("email_pointer_permissions_drift");
                    var names = context.GetService<ISqlGenerationHelper>();
                    string global = names.DelimitIdentifier(enrollment.GlobalLogin);
                    foreach (Source source in sources)
                    {
                        await ExecuteAsync($"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {global}");
                        await ExecuteAsync($"GRANT SELECT ({string.Join(",", source.ReadColumns.Select(column => names.DelimitIdentifier(column.Name)))}) ON {source.Table} TO {global}");
                        if (source.WriteColumns.Length != 0)
                            await ExecuteAsync($"GRANT UPDATE ({string.Join(",", source.WriteColumns.Select(column => names.DelimitIdentifier(column.Name)))}) ON {source.Table} TO {global}");
                        if (source.Tenant is null) continue;
                        foreach (string policy in PolicyNames(source))
                        {
                            bool update = policy is Write or WriteBoundary;
                            string session = PolicySession(enrollment.GlobalLogin);
                            await ExecuteAsync($"CREATE POLICY {names.DelimitIdentifier(policy)} ON {source.Table} AS {(policy is Read or Write ? "PERMISSIVE" : "RESTRICTIVE")} FOR {(update ? "UPDATE" : "SELECT")} TO {global} USING ({session}){(update ? $" WITH CHECK ({session})" : "")}");
                        }
                    }
                    if (!await CheckAsync(Manifest(context, sources, enrollment, complete: true)))
                        throw new InvalidOperationException("email_pointer_permissions_drift");
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
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("email_pointer_original_global_required");
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("email_pointer_enrollment_required");
        enrollment.Validate();
        var contextId = context.ContextId;
        var tenant = context.TenantContext;
        var connection = RelationalConnectionIdentity.Capture(context);
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
            if (context.ContextId != contextId || !ReferenceEquals(tenant, context.TenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
                || System.Transactions.Transaction.Current is not null || interrupted
                || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("email_pointer_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            bool session = await IsTrueAsync(context, $"{Identity(enrollment.GlobalLogin)} AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL", cancellationToken);
            RequireOriginal();
            if (!session) throw new UnauthorizedAccessException("email_pointer_permissions_not_ready");
            bool ready = await IsTrueAsync(context, CompleteGlobalCataloguePredicate(context, enrollment), cancellationToken);
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException("email_pointer_permissions_not_ready");
        }
        finally { connection.StateChange -= Changed; }
    }

    internal static string CompleteGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        Manifest(context, Sources(context), enrollment, complete: true);

    internal static IReadOnlyList<string> GlobalCatalogueRelations(ExploreDbContext context) =>
        Sources(context).Select(source => source.Table).ToArray();

    internal static string GlobalPolicyException(ExploreDbContext context, string login) =>
        "(" + string.Join(" OR ", Sources(context).Where(source => source.Tenant is not null).Select(source =>
            $"(session_user={Text(login)} AND current_user=session_user AND n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({DeclaredPolicyPredicate(source, login)}))")) + ")";

    internal static string DeclaredPolicyPredicate(ExploreDbContext context, string login) =>
        "(" + string.Join(" OR ", Sources(context).Where(source => source.Tenant is not null)
            .Select(source => $"({DeclaredPolicyPredicate(source, login)})")) + ")";

    internal static string DeclaredColumnPrivilegePredicate(ExploreDbContext context,
        string relation, string column, string privilege) =>
        "(" + string.Join(" OR ", Sources(context).Select(source =>
            $"({relation}={Text(source.Table)} AND (({privilege}='SELECT' AND {column} IN ({ColumnNames(source.ReadColumns)}))"
            + (source.WriteColumns.Length == 0 ? "" : $" OR ({privilege}='UPDATE' AND {column} IN ({ColumnNames(source.WriteColumns)}))") + "))")) + ")";

    private static string Manifest(ExploreDbContext context, Source[] sources,
        PostgresNativeQueueEnrollment enrollment, bool complete) =>
        string.Join(" AND ", sources.Select(source => $"({RelationManifest(context, source, enrollment, complete)})"));

    private static string RelationManifest(ExploreDbContext context, Source source,
        PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string global = Text(enrollment.GlobalLogin), migrator = Text(enrollment.MigratorLogin);
        string relation = Text(source.Table);
        string permitted = $"({DeclaredPolicyPredicate(source, enrollment.GlobalLogin)})";
        if (source.Outbox)
            permitted += $" OR ({PostgresEmailRetentionReadPermissions.SharedOutboxPolicyPredicate(context, enrollment.GlobalLogin)})";
        string allowedColumn = $"((permission.privilege='SELECT' AND a.attname IN ({ColumnNames(source.ReadColumns)}))"
            + (source.WriteColumns.Length == 0 ? "" : $" OR (permission.privilege='UPDATE' AND a.attname IN ({ColumnNames(source.WriteColumns)}))") + ")";
        string required = complete
            ? string.Join(" AND ", source.ReadColumns.Select(column => RequiredAcl(source, column, "SELECT", enrollment))
                .Concat(source.WriteColumns.Select(column => RequiredAcl(source, column, "UPDATE", enrollment))))
                + $" AND pg_catalog.has_schema_privilege({global},{Text(source.Schema)},'USAGE')"
            : "true";
        if (source.Tenant is not null)
        {
            if (complete)
                required += " AND " + string.Join(" AND ", PolicyNames(source).Select(policy =>
                    $"EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(source, enrollment.GlobalLogin, policy)})"));
        }
        string rls = source.Tenant is { } tenant ? $"""
            c.relrowsecurity AND c.relforcerowsecurity
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid
                AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(tenant, source.Schema))}
                    || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            """ : "NOT c.relrowsecurity AND NOT c.relforcerowsecurity";
        string retention = source.Outbox
            ? $"AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PostgresEmailRetentionReadPermissions.SharedOutboxPolicyPredicate(context, enrollment.GlobalLogin)})"
            : "";
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={global} AND rolcanlogin)
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={migrator} AND rolcanlogin)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={RoleOid(enrollment.GlobalLogin)})
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=pg_catalog.to_regclass({relation}) AND c.relkind='r' AND c.relpersistence='p' AND NOT c.relispartition
                    AND c.relowner={RoleOid(enrollment.MigratorLogin)} AND n.nspname={Text(source.Schema)}
                    AND n.nspowner={RoleOid(enrollment.MigratorLogin)} AND ({rls})
                    AND ({string.Join(" AND ", source.ReadColumns.Concat(source.WriteColumns).DistinctBy(column => column.Name).Select(column => $"""
                        EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attname={Text(column.Name)}
                            AND a.attnum>0 AND NOT a.attisdropped AND a.attidentity='' AND a.attgenerated=''
                            AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                            AND a.attnotnull={(column.Required ? "true" : "false")})
                        """))})
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits i WHERE i.inhrelid=c.oid OR i.inhparent=c.oid)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                        OR r.rolname={migrator} OR pg_catalog.has_schema_privilege(r.oid,{Text(source.Schema)},'CREATE')
                        OR pg_catalog.has_table_privilege(r.oid,{relation},'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                        OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", relation, false)}
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=pg_catalog.to_regclass({relation}) AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,permission.privilege)
                                        AND (r.rolname<>{global} OR NOT ({allowedColumn})))))))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                CROSS JOIN LATERAL pg_catalog.aclexplode(COALESCE(t.relacl,pg_catalog.acldefault('r',t.relowner))) acl
                WHERE t.oid=pg_catalog.to_regclass({relation}) AND acl.grantee=0)
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({relation}) AND a.attnum<>0 AND NOT a.attisdropped
                    AND (acl.grantee=0 OR (acl.grantee={RoleOid(enrollment.GlobalLogin)}
                        AND (acl.is_grantable OR acl.grantor<>{RoleOid(enrollment.MigratorLogin)}
                            OR NOT ((acl.privilege_type='SELECT' AND a.attname IN ({ColumnNames(source.ReadColumns)}))
                                {(source.WriteColumns.Length == 0 ? "" : $"OR (acl.privilege_type='UPDATE' AND a.attname IN ({ColumnNames(source.WriteColumns)}))")})))))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({relation})
                AND p.polname<>'tenant_isolation'
                AND (p.polname IN ({Text(Read)},{Text(ReadBoundary)},{Text(Write)},{Text(WriteBoundary)})
                    OR 0=ANY(p.polroles) OR EXISTS (SELECT 1 FROM pg_catalog.pg_roles r
                        WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER') AND r.oid=ANY(p.polroles)))
                AND NOT COALESCE(({permitted}),false))
            {retention}
            AND ({required})
            """;
    }

    private static string RequiredAcl(Source source, Column column, string privilege, PostgresNativeQueueEnrollment enrollment) => $"""
        EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(column.Name)}
                AND acl.grantee={RoleOid(enrollment.GlobalLogin)} AND acl.grantor={RoleOid(enrollment.MigratorLogin)}
                AND acl.privilege_type={Text(privilege)} AND NOT acl.is_grantable)
        """;

    private static string Fresh(ExploreDbContext context, Source[] sources, PostgresNativeQueueEnrollment enrollment)
    {
        string retentionColumns = string.Join(",", PostgresEmailRetentionReadPermissions.ReadProperties.Select(property =>
            Text(context.Model.FindEntityType(typeof(EmailDispatchOutbox))!.FindProperty(property)!
                .GetColumnName(StoreObjectIdentifier.Table(sources[0].Name, context.Model.FindEntityType(typeof(EmailDispatchOutbox))!.GetSchema()))!)));
        return string.Join(" AND ", sources.Select(source => $"""
            (NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee={RoleOid(enrollment.GlobalLogin)}
                    AND NOT ({(source.Outbox ? $"acl.privilege_type='SELECT' AND a.attname IN ({retentionColumns})" : "false")}))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND p.polname IN ({Text(Read)},{Text(ReadBoundary)},{Text(Write)},{Text(WriteBoundary)})))
            """));
    }

    private static string DeclaredPolicyPredicate(Source source, string login) =>
        source.Tenant is null ? "false" : "(" + string.Join(" OR ", PolicyNames(source).Select(policy => PolicyPredicate(source, login, policy))) + ")";

    private static IEnumerable<string> PolicyNames(Source source) =>
        source.WriteColumns.Length == 0 ? [Read, ReadBoundary] : [Read, ReadBoundary, Write, WriteBoundary];

    private static string PolicyPredicate(Source source, string login, string policy)
    {
        bool update = policy is Write or WriteBoundary;
        string literal = "'" + login.Replace("'", "''", StringComparison.Ordinal) + "'";
        string expression = $"((SESSION_USER = CURRENT_USER) AND (CURRENT_USER = {literal}::name) AND (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text) IS NULL))";
        return $"""
            (p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policy)}
            AND p.polcmd='{(update ? "w" : "r")}' AND p.polpermissive={(policy is Read or Write ? "true" : "false")}
            AND p.polroles=ARRAY[{RoleOid(login)}] AND p.polqual IS NOT NULL
            AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={Text(expression)}
            AND {(update ? $"p.polwithcheck IS NOT NULL AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)={Text(expression)}" : "p.polwithcheck IS NULL")})
            """;
    }

    private static Source[] Sources(ExploreDbContext context) =>
    [
        Map<EmailDispatchOutbox>(context, PointerProperties.Concat(PostgresEmailRetentionReadPermissions.ReadProperties).Distinct().ToArray(), MarkerProperties, outbox: true),
        Map<EmailDispatchTenantControl>(context, [nameof(EmailDispatchTenantControl.TenantId), nameof(EmailDispatchTenantControl.IsPaused)], []),
        Map<EmailDispatchProcessorState>(context, [nameof(EmailDispatchProcessorState.ProcessorCode), nameof(EmailDispatchProcessorState.IsPaused)], [])
    ];

    private static Source Map<T>(ExploreDbContext context, string[] reads, string[] writes, bool outbox = false)
    {
        var entity = context.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException("email_pointer_model_missing");
        string name = entity.GetTableName() ?? throw new InvalidOperationException("email_pointer_table_missing");
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id")
            throw new InvalidOperationException("email_pointer_model_shape_drift");
        var table = StoreObjectIdentifier.Table(name, entity.GetSchema());
        Column[] Columns(string[] properties) => properties.Select(propertyName =>
        {
            var property = entity.FindProperty(propertyName) ?? throw new InvalidOperationException("email_pointer_property_missing");
            if (property.IsShadowProperty()) throw new InvalidOperationException("email_pointer_property_shape_drift");
            return new Column(property.GetColumnName(table)!, property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
        }).ToArray();
        var tenant = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .SingleOrDefault(value => value.TableName == name && (value.Schema ?? schema) == schema);
        if (typeof(T) != typeof(EmailDispatchProcessorState)
            && (tenant is null || tenant.AllowsNullTenant || tenant.ParentJoin is not null)
            || typeof(T) == typeof(EmailDispatchProcessorState) && tenant is not null)
            throw new InvalidOperationException("email_pointer_tenant_shape_drift");
        return new Source(name, schema, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema),
            Columns(reads), Columns(writes), tenant, outbox);
    }

    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(token) is true;
    }

    private static string ColumnNames(Column[] columns) => string.Join(",", columns.Select(column => Text(column.Name)));
    private static string Identity(string login) => $"session_user=current_user AND current_user={Text(login)}::name AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string PolicySession(string login) => $"SESSION_USER=CURRENT_USER AND CURRENT_USER={Text(login)}::name AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";
    private static string RoleOid(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(string Name, string Schema, string Table, Column[] ReadColumns,
        Column[] WriteColumns, TenantTableMetadata? Tenant, bool Outbox);
}
