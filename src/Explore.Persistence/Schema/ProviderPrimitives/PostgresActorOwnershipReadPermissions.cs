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

/// <summary>Fixed Runtime ownership identifiers, not actor profiles or authorization decisions.</summary>
public static class PostgresActorOwnershipReadPermissions
{
    private const string Drift = "actor_ownership_read_permissions_drift";
    private static readonly (Type Type, string[] Read)[] Inventory =
    [
        (typeof(Actor), [nameof(Actor.Id), nameof(Actor.UserId), nameof(Actor.GroupId), nameof(Actor.OrganizationId), nameof(Actor.IsDeleted)]),
        (typeof(TenantUser), [nameof(TenantUser.TenantId), nameof(TenantUser.ActorId), nameof(TenantUser.IsDeleted)])
    ];

    /// <summary>Pure mapped inventory for overlapping owners; never inferred from installed privileges.</summary>
    internal static bool TryGetRuntimeReadColumns(ExploreDbContext context, Type type, out IReadOnlyList<string> columns)
    {
        var item = Inventory.SingleOrDefault(item => item.Type == type);
        columns = item.Type is null ? [] : Array.AsReadOnly(item.Read.Select(property => Column(context, type, property)).ToArray());
        return item.Type is not null;
    }

    /// <summary>Recognizes only a direct owner-issued fixed read in another owner's column-ACL guard.</summary>
    internal static string RuntimeReadPredicate(ExploreDbContext context, Type type, string runtimeLogin,
        string roleOid, string relationOid, string columnName, string privilege)
    {
        if (!TryGetRuntimeReadColumns(context, type, out var columns)) return "false";
        var source = Sources(context).Single(source => source.Type == type);
        return $"""
            ({roleOid}={Role(runtimeLogin)} AND {relationOid}=pg_catalog.to_regclass({Text(source.Table)})
                AND {privilege}='SELECT' AND {columnName}=ANY(ARRAY[{string.Join(',', columns.Select(Text))}]::text[])
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_attribute owned
                    CROSS JOIN LATERAL pg_catalog.aclexplode(owned.attacl) acl
                    JOIN pg_catalog.pg_class owner_table ON owner_table.oid=owned.attrelid
                    WHERE owned.attrelid={relationOid} AND owned.attname={columnName} AND owned.attnum>0
                        AND acl.grantee={roleOid} AND acl.grantor=owner_table.relowner
                        AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute shared
                    CROSS JOIN LATERAL pg_catalog.aclexplode(shared.attacl) acl
                    WHERE shared.attrelid={relationOid} AND shared.attname={columnName}
                        AND acl.grantee=0 AND acl.privilege_type='SELECT'))
            """;
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var sources = Sources(context);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var connection = RelationalConnectionIdentity.Capture(context);
        var physical = transaction.GetDbTransaction();
        var contextId = context.ContextId;
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            if (context.ContextId != contextId || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction()) || !connection.Owns(physical))
                throw new UnauthorizedAccessException("actor_ownership_installer_owner_changed");
        }
        connection.StateChange += Changed;
        string savepoint = "actor_ownership_permissions_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                if (!await IsTrueAsync(context, Identity(enrollment.MigratorLogin), cancellationToken))
                    throw new UnauthorizedAccessException("actor_ownership_installer_wrong_identity");
                RequireOriginal();
                string fresh = Fresh(sources, enrollment.RuntimeLogin);
                string complete = Catalogue(context, sources, enrollment, complete: true);
                if (!await IsTrueAsync(context, $"({Catalogue(context, sources, enrollment, complete: false)}) AND (({fresh}) OR ({complete}))", cancellationToken))
                    throw new InvalidOperationException(Drift);
                RequireOriginal();
                if (await IsTrueAsync(context, fresh, cancellationToken))
                {
                    RequireOriginal();
                    var names = context.GetService<ISqlGenerationHelper>();
                    foreach (var source in sources)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await ExecuteAsync(context, $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.RuntimeLogin)}; GRANT SELECT ({string.Join(',', source.Read.Select(column => names.DelimitIdentifier(column)))}) ON {source.Table} TO {names.DelimitIdentifier(enrollment.RuntimeLogin)}", cancellationToken);
                        RequireOriginal();
                    }
                }
                if (!await IsTrueAsync(context, complete, cancellationToken)) throw new InvalidOperationException(Drift);
                RequireOriginal();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                RequireOriginal();
                await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                throw;
            }
            finally
            {
                RequireOriginal();
                await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None);
            }
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
        if (enrollment is null) throw new UnauthorizedAccessException("actor_ownership_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        var connection = RelationalConnectionIdentity.Capture(context);
        var physical = context.Database.CurrentTransaction?.GetDbTransaction();
        var contextId = context.ContextId;
        Guid? tenant = context.TenantFilterTenantId;
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
            if (tenant is null || context.TenantFilterTenantId != tenant || context.IsTenantFilterBypassed
                || context.ContextId != contextId || interrupted || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(physical, context.Database.CurrentTransaction?.GetDbTransaction())
                || physical is not null && !connection.Owns(physical))
                throw new UnauthorizedAccessException("actor_ownership_read_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            if (!await IsTrueAsync(context, $"({Identity(enrollment.RuntimeLogin)}) AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')={Text(tenant!.Value.ToString("D"))}", cancellationToken))
                throw new UnauthorizedAccessException("actor_ownership_read_wrong_identity");
            RequireOriginal();
            bool ready = await IsTrueAsync(context, Catalogue(context, Sources(context), enrollment, complete: true), cancellationToken);
            RequireOriginal();
            if (!ready) throw new UnauthorizedAccessException(Drift);
        }
        finally { connection.StateChange -= Changed; }
    }

    private static string Catalogue(ExploreDbContext context, Source[] sources, PostgresNativeQueueEnrollment enrollment, bool complete) =>
        $"({string.Join(" AND ", sources.Select(source => SourcePredicate(context, source, enrollment, complete)))})";

    private static string SourcePredicate(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        PostgresTenantMembershipMutationPermissions.TryGetRuntimeColumns(context, source.Type, out var baselineRead, out var baselineUpdate, out _);
        var allowed = source.Read.Concat(baselineRead).Distinct().Select(column => (Column: column, Privilege: "SELECT"))
            .Concat(baselineUpdate.Select(column => (Column: column, Privilege: "UPDATE"))).ToArray();
        string expected = string.Join(',', allowed.Select(grant => $"({Text(grant.Column)},{Text(grant.Privilege)})"));
        string required = complete ? string.Join(" AND ", source.Read.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND a.attname={Text(column)}
                    AND acl.grantee={Role(enrollment.RuntimeLogin)} AND acl.grantor={Role(enrollment.MigratorLogin)}
                    AND acl.privilege_type='SELECT' AND NOT acl.is_grantable)
            """)) : "true";
        string shape = string.Join(" AND ", source.Columns.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=t.oid AND a.attname={Text(column.Name)}
                AND a.attnum>0 AND NOT a.attisdropped AND a.attgenerated='' AND a.attidentity=''
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)} AND a.attnotnull={(column.Required ? "true" : "false")})
            """));
        string security = source.Type == typeof(Actor) ? "NOT t.relrowsecurity AND NOT t.relforcerowsecurity"
            : TenantPolicy(context, source, enrollment.RuntimeLogin);
        return $"""
            (WITH expected(column_name,privilege) AS (VALUES {expected}), members AS
                (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE oid={Role(enrollment.RuntimeLogin)} AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members WHERE roleid={Role(enrollment.RuntimeLogin)})
                AND NOT pg_catalog.pg_has_role({Role(enrollment.RuntimeLogin)},{Role(enrollment.GlobalLogin)},'MEMBER')
                AND NOT pg_catalog.pg_has_role({Role(enrollment.GlobalLogin)},{Role(enrollment.RuntimeLogin)},'MEMBER')
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_class t JOIN pg_catalog.pg_namespace n ON n.oid=t.relnamespace
                    WHERE t.oid=pg_catalog.to_regclass({Text(source.Table)}) AND t.relowner={Role(enrollment.MigratorLogin)}
                        AND n.nspowner={Role(enrollment.MigratorLogin)} AND t.relkind='r' AND t.relpersistence='p' AND NOT t.relispartition
                        AND ({security}) AND ({shape})
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=t.oid)
                        AND NOT EXISTS (SELECT 1 FROM members r WHERE r.oid=t.relowner OR r.rolsuper OR r.rolbypassrls
                            OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication OR pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE')
                            OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                            OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                                CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                                WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                    AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                        OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                            r.oid={Role(enrollment.RuntimeLogin)} AND a.attnum>0
                                            AND EXISTS (SELECT 1 FROM expected e WHERE e.column_name=a.attname AND e.privilege=permission.privilege)
                                            AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid
                                                AND acl.grantor=t.relowner AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                            AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                        {(complete ? $"AND pg_catalog.has_schema_privilege({Role(enrollment.RuntimeLogin)},n.oid,'USAGE')" : "")})
                AND ({required}))
            """;
    }

    private static string TenantPolicy(ExploreDbContext context, Source source, string login)
    {
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model)
            .Single(table => table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        if (metadata.AllowsNullTenant || metadata.ParentJoin is not null) throw new InvalidOperationException(Drift);
        string column = Column(context, source.Type, nameof(TenantUser.TenantId));
        string condition = $"({Text("(")} || pg_catalog.quote_ident({Text(column)}) || {Text(" = (NULLIF(current_setting('app.current_tenant_id'::text, true), ''::text))::uuid)")})";
        return $"""
            t.relrowsecurity AND t.relforcerowsecurity
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname='tenant_isolation'
                AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[0::oid]
                AND pg_catalog.pg_get_expr(p.polqual,p.polrelid)={condition}
                AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)={condition}
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, source.Schema))}
                    || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend dependency
                    WHERE dependency.classid='pg_catalog.pg_policy'::regclass AND dependency.objid=p.oid
                        AND ((dependency.refclassid='pg_catalog.pg_proc'::regclass AND NOT EXISTS (
                            SELECT 1 FROM pg_catalog.pg_proc routine WHERE routine.oid=dependency.refobjid
                                AND routine.pronamespace='pg_catalog'::regnamespace))
                        OR (dependency.refclassid='pg_catalog.pg_operator'::regclass AND NOT EXISTS (
                            SELECT 1 FROM pg_catalog.pg_operator operation WHERE operation.oid=dependency.refobjid
                                AND operation.oprnamespace='pg_catalog'::regnamespace)))))
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=t.oid AND p.polname<>'tenant_isolation'
                AND (0::oid=ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                    WHERE pg_catalog.pg_has_role({Role(login)},role_id,'MEMBER'))))
            """;
    }

    private static string Fresh(Source[] sources, string runtime) => string.Join(" AND ", sources.Select(source => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
            WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND acl.grantee={Role(runtime)}
                {(source.Type == typeof(TenantUser) ? $"AND a.attname={Text(source.Columns.Single(column => column.Property == nameof(TenantUser.ActorId)).Name)}" : "")})
        """));

    private static Source[] Sources(ExploreDbContext context) => Inventory.Select(item =>
    {
        var entity = context.Model.FindEntityType(item.Type) ?? throw new InvalidOperationException(Drift);
        string name = entity.GetTableName() ?? throw new InvalidOperationException(Drift);
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
            || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id") throw new InvalidOperationException(Drift);
        var columns = item.Read.Select(propertyName =>
        {
            var property = entity.FindProperty(propertyName) ?? throw new InvalidOperationException(Drift);
            if (property.IsShadowProperty()) throw new InvalidOperationException(Drift);
            return new MappedColumn(propertyName, Column(context, item.Type, propertyName), property.GetRelationalTypeMapping().StoreType, !property.IsNullable);
        }).ToArray();
        return new Source(item.Type, schema, name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema),
            columns.Select(column => column.Name).ToArray(), columns);
    }).ToArray();

    private static string Column(ExploreDbContext context, Type type, string property)
    {
        var entity = context.Model.FindEntityType(type) ?? throw new InvalidOperationException(Drift);
        return entity.FindProperty(property)?.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))
            ?? throw new InvalidOperationException(Drift);
    }

    private static string Identity(string login) => $"session_user={Text(login)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)";
    private static string Role(string login) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})";
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
    private static async Task<bool> IsTrueAsync(ExploreDbContext context, string predicate, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT ({predicate})";
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record MappedColumn(string Property, string Name, string StoreType, bool Required);
    private sealed record Source(Type Type, string Schema, string Name, string Table, string[] Read, MappedColumn[] Columns);
}
