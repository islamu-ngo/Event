using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Explore.Persistence.Schema;

/// <summary>
/// Fixed storage permissions for tenant creation. These are shared Global credential rights,
/// not human authorization; the original creator separately admits its exact new graph.
/// Fixed membership INSERT rights share the Global credential; private identity reads remain absent.
/// </summary>
public static class PostgresTenantCreationPermissions
{
    private const string InsertPolicy = "tenant_creation_global_insert";
    private const string ReadPolicy = "tenant_creation_global_read";
    private const string DocumentInsertPolicy = "tenant_creation_document_keys";
    private const string Drift = "tenant_creation_permissions_drift";
    private static readonly (Type Type, string[] Read, string[] Insert)[] Inventory =
    [
        (typeof(Tenant), ["Id", "Slug", "TenantStatusId"],
            ["Id", "FullName", "Slug", "Description", "TenantStatusId", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"]),
        (typeof(TenantSettingsDocument), [],
            ["Id", "TenantId", "DocumentKey", "SchemaVersion", "DefaultsVersion", "PayloadJson", "ConcurrencyStamp",
                "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"]),
        (typeof(TenantStatus), ["Id", "IsActiveState"], []),
        (typeof(InstanceBootstrapState), ["Id", "Status", "DeploymentMode", "Generation", "CreatedAt"], []),
        (typeof(ManagedTenantProvisioningOperation), ["Id", "Status"], []),
        (typeof(TenantUser), [],
            ["Id", "TenantId", "UserId", "ActorId", "StatusId", "JoinedAt", "SuspendedAt", "SuspendedBy",
                "BanExpiresAt", "RemovedAt", "RemovedBy", "ModerationNote", "CreatedAt", "CreatedBy", "UpdatedAt",
                "UpdatedBy", "IsDeleted", "DeletedAt", "DeletedBy"]),
        (typeof(TenantUserRoleGrant), [],
            ["Id", "TenantId", "TenantUserId", "RoleId", "RoleScopeId", "GrantedAt", "GrantedBy", "RevokedAt",
                "RevokedBy", "RevocationReason", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"]),
        (typeof(Role), ["Id", "MasterCode", "RoleScopeId"], [])
    ];

    /// <summary>Installs only a wholly absent creator addition or verifies its complete existing image.</summary>
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        IdentityDatabaseTopology identityTopology, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var sources = Sources(context);
        var mechanics = ProvisioningSharedWriterAssertions.TenantCreationInventory(context, enrollment, identityTopology);
        string fresh = FreshPredicate(context, sources, enrollment.GlobalLogin);
        string complete = ManifestPredicate(context, sources, enrollment, true);
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        string savepoint = "tenant_creation_permissions_" + Guid.CreateVersion7().ToString("N");
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            if (await ScalarAsync(context, $"SELECT session_user={Text(enrollment.MigratorLogin)} AND current_user=session_user AND pg_catalog.current_setting('role') IN ('none',session_user::text)", cancellationToken) is not true)
                throw new UnauthorizedAccessException("tenant_creation_installer_wrong_identity");
            if (await ScalarAsync(context, $"SELECT ({ManifestPredicate(context, sources, enrollment, false)}) AND (({fresh}) OR ({complete})) AND ({ExecutableSourcePredicate(context, sources, mechanics.Writers)})", cancellationToken) is not true)
                throw new InvalidOperationException(Drift);
            await ExecuteAsync(context, ExecutableAssertions(context, mechanics), cancellationToken);
            if (await ScalarAsync(context, "SELECT " + fresh, cancellationToken) is true)
            {
                var names = context.GetService<ISqlGenerationHelper>();
                foreach (var source in sources)
                {
                    await ExecuteAsync(context, $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(source.Schema)} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};", cancellationToken);
                    foreach (var (properties, privilege) in new[] { (source.Read, "SELECT"), (source.Insert, "INSERT") })
                        if (properties.Length != 0)
                            await ExecuteAsync(context, $"GRANT {privilege} ({string.Join(',', properties.Select(property => names.DelimitIdentifier(source.Columns[property].Name)))}) ON {source.Table} TO {names.DelimitIdentifier(enrollment.GlobalLogin)};", cancellationToken);
                    foreach (string policy in Policies(source))
                    {
                        string clause = policy == ReadPolicy ? "FOR SELECT" : "FOR INSERT";
                        string predicate = policy == ReadPolicy ? "USING" : "WITH CHECK";
                        string restriction = policy == DocumentInsertPolicy ? "AS RESTRICTIVE" : "AS PERMISSIVE";
                        string condition = SealedSession(enrollment.GlobalLogin);
                        if (policy == DocumentInsertPolicy)
                        {
                            string key = names.DelimitIdentifier(source.Columns[nameof(TenantSettingsDocument.DocumentKey)].Name);
                            condition += $" AND ({key}::text = {Text(SettingsDocumentKeys.Tenant.Branding)}::text OR {key}::text = {Text(SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity)}::text)";
                        }
                        await ExecuteAsync(context, $"CREATE POLICY {names.DelimitIdentifier(policy)} ON {source.Table} {restriction} {clause} TO {names.DelimitIdentifier(enrollment.GlobalLogin)} {predicate} ({condition});", cancellationToken);
                        string hash = await ScalarAsync(context, $"SELECT COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'') FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policy)}", cancellationToken) as string
                            ?? throw new InvalidOperationException(Drift);
                        await ExecuteAsync(context, $"COMMENT ON POLICY {names.DelimitIdentifier(policy)} ON {source.Table} IS {Text(PolicyStamp(source, enrollment.GlobalLogin, policy) + hash)}", cancellationToken);
                    }
                }
                if (await ScalarAsync(context, "SELECT " + complete, cancellationToken) is not true)
                    throw new InvalidOperationException(Drift);
            }
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            throw;
        }
        finally { await transaction.ReleaseSavepointAsync(savepoint, CancellationToken.None); }
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    /// <summary>Attests the existing open Global owner without selecting, opening or replacing it.</summary>
    public static async Task RequireCurrentAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        IdentityDatabaseTopology identityTopology, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(identityTopology)) throw new ArgumentOutOfRangeException(nameof(identityTopology));
        if (!context.Database.IsNpgsql()) return;
        if (enrollment is null) throw new UnauthorizedAccessException("tenant_creation_enrollment_required");
        enrollment.Validate();
        context.RequireBoundTenantLease();
        if (context.Database.GetDbConnection() is not NpgsqlConnection connection || connection.State != ConnectionState.Open)
            throw new UnauthorizedAccessException("tenant_creation_open_owner_required");
        var lease = context.ContextId;
        var tenant = context.TenantContext;
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        int backend = connection.ProcessID;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs args) => interrupted = true;
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != lease || !ReferenceEquals(tenant, context.TenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null
                || interrupted || !ReferenceEquals(connection, context.Database.GetDbConnection())
                || connection.State != ConnectionState.Open || connection.ProcessID != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !ReferenceEquals(transaction.Connection, connection))
                throw new UnauthorizedAccessException("tenant_creation_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            var sources = Sources(context);
            var mechanics = ProvisioningSharedWriterAssertions.TenantCreationInventory(context, enrollment, identityTopology);
            string sql = $"SELECT ({SealedSession(enrollment.GlobalLogin)}) AND pg_catalog.current_setting('role') IN ('none',session_user::text) AND pg_catalog.current_setting('session_replication_role')='origin' AND ({ManifestPredicate(context, sources, enrollment, true)}) AND ({ExecutableSourcePredicate(context, sources, mechanics.Writers)})";
            if (await ScalarAsync(context, sql, cancellationToken) is not true)
                throw new UnauthorizedAccessException("tenant_creation_permissions_not_ready");
            RequireOriginal();
            await ExecuteAsync(context, ExecutableAssertions(context, mechanics), cancellationToken);
            RequireOriginal();
        }
        finally { connection.StateChange -= Changed; }
    }

    /// <summary>Source-declared column inventory for existing owners' intersecting catalog proofs.</summary>
    internal static bool TryGetGlobalColumns(ExploreDbContext context, Type type,
        out IReadOnlyList<string> readColumns, out IReadOnlyList<string> insertColumns)
    {
        var source = Sources(context).SingleOrDefault(source => source.Type == type);
        readColumns = source is null ? [] : Array.AsReadOnly(source.Read.Select(property => source.Columns[property].Name).ToArray());
        insertColumns = source is null ? [] : Array.AsReadOnly(source.Insert.Select(property => source.Columns[property].Name).ToArray());
        return source is not null;
    }

    /// <summary>Pure source-owned policy descriptors; no caller or composed catalogue admission.</summary>
    internal static IReadOnlyList<string> GlobalSourcePolicies(ExploreDbContext context, Type type, string login)
    {
        var source = Sources(context).SingleOrDefault(value => value.Type == type);
        return source is null ? [] : Array.AsReadOnly(Policies(source)
            .Select(policy => PolicyPredicate(source, login, policy)).ToArray());
    }

    /// <summary>Recognizes only an absent or complete fixed addition; no caller identity or executable protocol is inferred.</summary>
    internal static string OptionalGlobalCataloguePredicate(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        var sources = Sources(context);
        return $"(({ManifestPredicate(context, sources, enrollment, false)}) AND (({FreshPredicate(context, sources, enrollment.GlobalLogin)}) OR ({ManifestPredicate(context, sources, enrollment, true)})))";
    }

    /// <summary>Exact policy recognition for the readiness query's p/c/n aliases; never a new grant.</summary>
    internal static string GlobalPolicyException(ExploreDbContext context, string globalLogin)
    {
        string policies = string.Join(" OR ", Sources(context).SelectMany(source => Policies(source).Select(policy =>
            $"(n.nspname={Text(source.Schema)} AND c.relname={Text(source.Name)} AND ({PolicyPredicate(source, globalLogin, policy)}))")));
        return $"COALESCE((session_user={Text(globalLogin)} AND current_user=session_user AND ({(policies.Length == 0 ? "false" : policies)})),false)";
    }

    private static string ManifestPredicate(ExploreDbContext context, Source[] sources,
        PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string global = Text(enrollment.GlobalLogin), runtime = Text(enrollment.RuntimeLogin);
        var globalGrants = sources.SelectMany(source =>
            source.Read.Select(property => (property, Privilege: "SELECT"))
                .Concat(source.Insert.Select(property => (property, Privilege: "INSERT")))
                .Select(grant => $"({global},{Text(source.Table)},{Text(source.Columns[grant.property].Name)},{Text(grant.Privilege)},true)"));
        var workerGrants = sources.SelectMany(source =>
        {
            if (!PostgresManagedTenantWorkerCreationPermissions.TryGetGlobalColumns(context, source.Type,
                out var read, out var insert, out var update)) return Array.Empty<string>();
            return read.Select(column => (column, Privilege: "SELECT"))
                .Concat(insert.Select(column => (column, Privilege: "INSERT")))
                .Concat(update.Select(column => (column, Privilege: "UPDATE")))
                .Select(grant => $"({global},{Text(source.Table)},{Text(grant.column)},{Text(grant.Privilege)},false)");
        });
        var membershipReads = sources.SelectMany(source =>
        {
            PostgresUserTenantMembershipReadPermissions.TryGetGlobalReadColumns(context, source.Type, out var columns);
            return columns.Select(column => $"({global},{Text(source.Table)},{Text(column)},'SELECT',false)");
        });
        var membershipMutations = sources.SelectMany(source =>
        {
            PostgresTenantMembershipMutationPermissions.TryGetRuntimeColumns(context, source.Type,
                out var read, out var update, out _);
            return read.Select(column => $"({runtime},{Text(source.Table)},{Text(column)},'SELECT',false)")
                .Concat(update.Select(column => $"({runtime},{Text(source.Table)},{Text(column)},'UPDATE',false)"))
                .Concat(PostgresTenantMembershipMutationPermissions.GlobalUpdateColumns(context, source.Type)
                    .Select(column => $"({global},{Text(source.Table)},{Text(column)},'UPDATE',false)"));
        });
        var tenantKeyParents = sources.SelectMany(source =>
            PostgresTenantApiKeyStoragePermissions.RuntimeParentReadColumns(context, source.Type)
                .Select(column => $"({runtime},{Text(source.Table)},{Text(column)},'SELECT',false)"));
        var actorOwnershipReads = sources.SelectMany(source =>
        {
            PostgresActorOwnershipReadPermissions.TryGetRuntimeReadColumns(context, source.Type, out var columns);
            return columns.Select(column => $"({runtime},{Text(source.Table)},{Text(column)},'SELECT',false)");
        });
        // Role already belongs to Runtime's fixed public catalogue. Recognize that direct
        // source intersection without installing it or admitting an inherited role union.
        var role = sources.Single(source => source.Type == typeof(Role));
        PostgresLookupCatalogueReadPermissions.TryGetRuntimeReadColumns(context, typeof(Role), out var runtimeRoleColumns);
        string expected = "VALUES " + string.Join(',', globalGrants.Concat(workerGrants).Concat(membershipReads).Concat(membershipMutations).Concat(tenantKeyParents).Concat(actorOwnershipReads).Concat(runtimeRoleColumns.Select(column =>
            $"({runtime},{Text(role.Table)},{Text(column)},'SELECT',false)")));
        string tables = "ARRAY[" + string.Join(',', sources.Select(source => Text(source.Table))) + "]::text[]";
        string required = complete ? $"""
            AND NOT EXISTS (SELECT 1 FROM expected e WHERE e.login={global} AND e.required AND NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass(e.relation) AND a.attname=e.column_name
                    AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                    AND acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)})
                    AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
            """ : "";
        string sourceProofs = string.Join(" AND ", sources.Select(source => SourcePredicate(context, source, enrollment, complete)));
        return $"""
            (WITH expected(login,relation,column_name,privilege,required) AS ({expected}), callers(login) AS (VALUES ({global}),({runtime})),
            members AS (SELECT callers.login,r.* FROM callers CROSS JOIN pg_catalog.pg_roles r
                WHERE pg_catalog.pg_has_role(callers.login,r.oid,'MEMBER'))
            SELECT (SELECT count(*)=2 FROM pg_catalog.pg_roles WHERE rolname IN ({global},{runtime}) AND rolcanlogin)
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_auth_members
                    WHERE roleid=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global}))
                AND NOT EXISTS (SELECT 1 FROM members r WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                    OR EXISTS (SELECT 1 FROM unnest(ARRAY[{string.Join(',', sources.Select(source => Text(source.Schema)).Distinct())}]::text[]) s(name)
                        WHERE pg_catalog.has_schema_privilege(r.oid,s.name,'CREATE')))
                AND NOT EXISTS (SELECT 1 FROM unnest({tables}) source(relation)
                    JOIN pg_catalog.pg_class t ON t.oid=pg_catalog.to_regclass(source.relation) CROSS JOIN members r
                    WHERE r.oid=t.relowner OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                        OR (pg_catalog.has_table_privilege(r.oid,t.oid,'DELETE') AND NOT
                            ({PostgresTenantMembershipMutationPermissions.RuntimeDeletePredicate(context, enrollment, "r.oid", "t.oid")}))
                        OR EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                            CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                            WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                                AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                    OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege) AND NOT (
                                        (a.attnum>0 OR (source.relation={Text(sources.Single(source => source.Type == typeof(ManagedTenantProvisioningOperation)).Table)}
                                            AND a.attnum=-2 AND a.attname='xmin' AND permission.privilege='SELECT'))
                                        AND r.rolname=r.login
                                        AND EXISTS (SELECT 1 FROM expected e WHERE e.login=r.login AND e.relation=source.relation AND e.column_name=a.attname AND e.privilege=permission.privilege)
                                        AND EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=r.oid
                                            AND acl.grantor=t.relowner AND acl.privilege_type=permission.privilege AND NOT acl.is_grantable)
                                        AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(a.attacl) acl WHERE acl.grantee=0 AND acl.privilege_type=permission.privilege))))))
                AND ({sourceProofs}) AND ({PostgresManagedTenantWorkerCreationPermissions.OptionalGlobalCataloguePredicate(context, enrollment)})
                AND ({PostgresTenantMembershipMutationPermissions.OptionalCataloguePredicate(context, enrollment)}) {required})
            """;
    }

    private static string SourcePredicate(ExploreDbContext context, Source source, PostgresNativeQueueEnrollment enrollment, bool complete)
    {
        string columns = string.Join(" AND ", source.Columns.Values.Select(column => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND a.attname={Text(column.Name)} AND a.attnum>0 AND NOT a.attisdropped
                AND pg_catalog.format_type(a.atttypid,a.atttypmod)={Text(column.StoreType)}
                AND a.attnotnull={(column.Required ? "true" : "false")} AND a.attidentity='' AND a.attgenerated='')
            """));
        var metadata = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model).SingleOrDefault(table =>
            table.TableName == source.Name && (table.Schema ?? source.Schema) == source.Schema);
        string basePolicy = metadata is null ? "NOT c.relrowsecurity AND NOT c.relforcerowsecurity" : $"""
            c.relrowsecurity AND c.relforcerowsecurity AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                WHERE p.polrelid=c.oid AND p.polname='tenant_isolation' AND p.polcmd='*' AND p.polpermissive
                    AND p.polroles=ARRAY[0::oid] AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL
                    AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PostgresTenantRowLevelSecurityModel.BuildPolicyFingerprintPrefix(metadata, source.Schema))}
                        || pg_catalog.md5(p.polqual::text) || ':' || pg_catalog.md5(p.polwithcheck::text))
            """;
        string ownedPolicies = string.Join(" OR ", Policies(source).Select(policy => "(" + PolicyPredicate(source, enrollment.GlobalLogin, policy) + ")"));
        string recognizedPolicies = string.Join(" OR ", Policies(source)
            .Select(policy => PolicyPredicate(source, enrollment.GlobalLogin, policy))
            .Concat(PostgresUserTenantMembershipReadPermissions.GlobalSourcePolicies(context, source.Type, enrollment.GlobalLogin))
            .Select(predicate => "(" + predicate + ")"));
        string policies = $"NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname IN ({Text(ReadPolicy)},{Text(InsertPolicy)},{Text(DocumentInsertPolicy)}) AND NOT ({(ownedPolicies.Length == 0 ? "false" : ownedPolicies)}))";
        if (complete)
        {
            policies += string.Concat(Policies(source).Select(policy =>
                $" AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE {PolicyPredicate(source, enrollment.GlobalLogin, policy)})"));
            policies += $" AND pg_catalog.has_schema_privilege({Text(enrollment.GlobalLogin)},{Text(source.Schema)},'USAGE')";
        }
        return $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({Text(source.Table)})
                AND c.relkind='r' AND c.relpersistence='p'
                AND c.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(enrollment.MigratorLogin)}) AND ({basePolicy}))
            AND ({columns}) AND ({policies}) AND ({ForeignKeysPredicate(context, source)})
            AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                AND p.polname<>'tenant_isolation' AND NOT ({(recognizedPolicies.Length == 0 ? "false" : recognizedPolicies)})
                AND (0::oid=ANY(p.polroles) OR EXISTS (SELECT 1 FROM unnest(p.polroles) role_id
                    WHERE pg_catalog.pg_has_role({Text(enrollment.GlobalLogin)},role_id,'MEMBER')
                        OR pg_catalog.pg_has_role({Text(enrollment.RuntimeLogin)},role_id,'MEMBER'))))
            """;
    }

    private static string ExecutableSourcePredicate(ExploreDbContext context, Source[] sources,
        ProvisioningSharedWriterAssertions.Writer[] writers)
    {
        var names = new HumanPrincipalSourceSql(context);
        return string.Join(" AND ", sources.Select(source =>
        {
            string allowed = string.Join(" OR ", writers.Where(writer => writer.Targets.Any(target => target.Type == source.Type))
                .Select(writer => $"(t.tgname={Text(writer.Name)} AND t.tgfoid=pg_catalog.to_regprocedure({Text(names.Name(writer.Function) + "()")}))"));
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_rewrite WHERE ev_class=pg_catalog.to_regclass({Text(source.Table)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits
                    WHERE inhrelid=pg_catalog.to_regclass({Text(source.Table)}) OR inhparent=pg_catalog.to_regclass({Text(source.Table)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t
                    WHERE t.tgrelid=pg_catalog.to_regclass({Text(source.Table)}) AND NOT t.tgisinternal
                        AND NOT ({(allowed.Length == 0 ? "false" : allowed)}))
                AND ({InsertSurfacePredicate(context, source)})
                """;
        }));
    }

    private static string InsertSurfacePredicate(ExploreDbContext context, Source source)
    {
        if (source.Insert.Length == 0) return "true";
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(source.Type)
            ?? throw new InvalidOperationException("tenant_creation_model_missing");
        var properties = entity.GetProperties().ToArray();
        if (!properties.Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(source.Insert.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("tenant_creation_insert_model_drift");
        var checks = InsertChecks(source);
        var declared = entity.GetCheckConstraints().ToArray();
        if (declared.Length != checks.Length || declared.Any(check => !checks.Any(expected =>
                check.Name == expected.Name && string.Equals(check.Sql, expected.ModelSql, StringComparison.Ordinal))))
            throw new InvalidOperationException("tenant_creation_check_model_drift");

        string defaults = string.Join(" AND ", properties.Select(property =>
        {
            string? expected = InsertDefault(source.Type, property);
            string defaultProof = expected is null
                ? "NOT a.atthasdef AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d WHERE d.adrelid=a.attrelid AND d.adnum=a.attnum)"
                : $"a.atthasdef AND EXISTS (SELECT 1 FROM pg_catalog.pg_attrdef d WHERE d.adrelid=a.attrelid AND d.adnum=a.attnum AND pg_catalog.pg_get_expr(d.adbin,d.adrelid)={Text(expected)} AND ({BuiltinExpressionDependencies("pg_attrdef", "d.oid")}))";
            return $"""
                EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                        AND a.attnum>0 AND NOT a.attisdropped AND a.attname={Text(source.Columns[property.Name].Name)}
                        AND a.attgenerated='' AND a.attidentity='' AND ({defaultProof}))
                """;
        }));
        string requiredChecks = string.Concat(checks.Select(check => $"""
            AND EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c
                WHERE c.conrelid=pg_catalog.to_regclass({Text(source.Table)}) AND c.contype='c'
                    AND c.conname={Text(check.Name)} AND c.conenforced AND c.convalidated AND c.conislocal AND c.coninhcount=0
                    AND NOT c.connoinherit AND NOT c.condeferrable AND NOT c.condeferred
                    AND pg_catalog.pg_get_expr(c.conbin,c.conrelid)=({check.Expression})
                    AND ({BuiltinExpressionDependencies("pg_constraint", "c.oid")}))
            """));
        // The grant inventory already proves every expected column. Equal physical
        // cardinality additionally excludes omitted columns with executable defaults.
        return $"""
            (SELECT count(*)={properties.Length} FROM pg_catalog.pg_attribute
                WHERE attrelid=pg_catalog.to_regclass({Text(source.Table)}) AND attnum>0 AND NOT attisdropped)
            AND ({defaults})
            AND (SELECT count(*)={checks.Length} FROM pg_catalog.pg_constraint
                WHERE conrelid=pg_catalog.to_regclass({Text(source.Table)}) AND contype='c')
            {requiredChecks}
            """;
    }

    internal static string DeclaredInsertSurfacePredicate(ExploreDbContext context, Type type) =>
        InsertSurfacePredicate(context, Sources(context).Single(source => source.Type == type));

    private static (string Name, string ModelSql, string Expression)[] InsertChecks(Source source)
    {
        string Column(string property) => source.Columns[property].Name;
        string Expression(string property, string prefix, string suffix) =>
            $"{Text(prefix)} || pg_catalog.quote_ident({Text(Column(property))}) || {Text(suffix)}";
        if (source.Type == typeof(Tenant)) return [];
        if (source.Type == typeof(TenantSettingsDocument)) return
        [
            ("ck_tenant_settings_documents_schema_version_positive", $"{Column("SchemaVersion")} > 0",
                Expression("SchemaVersion", "(", " > 0)")),
            ("ck_tenant_settings_documents_document_key_not_blank", $"length(trim({Column("DocumentKey")})) > 0",
                Expression("DocumentKey", "(length(TRIM(BOTH FROM ", ")) > 0)")),
            ("ck_tenant_settings_documents_payload_object", $"jsonb_typeof({Column("PayloadJson")}) = 'object'",
                Expression("PayloadJson", "(jsonb_typeof(", ") = 'object'::text)"))
        ];
        if (source.Type == typeof(TenantUser))
        {
            string statuses = string.Join(", ", new[] { TenantUserStatusEnum.Active, TenantUserStatusEnum.Suspended,
                TenantUserStatusEnum.Banned, TenantUserStatusEnum.Removed }
                .Select(status => ((int)status).ToString(CultureInfo.InvariantCulture)));
            return [("ck_tenant_users_status", $"{Column("StatusId")} IN ({statuses})",
                Expression("StatusId", "(", $" = ANY (ARRAY[{statuses}]))"))];
        }
        if (source.Type == typeof(TenantUserRoleGrant))
        {
            string scope = ((int)RoleScopeEnum.Tenant).ToString(CultureInfo.InvariantCulture);
            return [("ck_tenant_user_role_grants_role_scope", $"{Column("RoleScopeId")} = {scope}",
                Expression("RoleScopeId", "(", $" = {scope})"))];
        }
        throw new InvalidOperationException("tenant_creation_insert_type_not_declared");
    }

    private static string? InsertDefault(Type type, IProperty property)
    {
        string? sql = property.GetDefaultValueSql();
        var literal = property.FindAnnotation(RelationalAnnotationNames.DefaultValue);
        if (sql is null && literal is null) return null;
        if (literal is null && string.Equals(sql, "NOW()", StringComparison.OrdinalIgnoreCase)
            && property.ClrType == typeof(DateTime)
            && ((type == typeof(TenantSettingsDocument) || type == typeof(TenantUser) || type == typeof(TenantUserRoleGrant))
                    && property.Name == "CreatedAt" || type == typeof(TenantUserRoleGrant) && property.Name == "GrantedAt"))
            return "now()";
        if (sql is null && literal?.Value is int value
            && (type == typeof(TenantUser) && property.Name == "StatusId" && value == (int)TenantUserStatusEnum.Active
                || type == typeof(TenantUserRoleGrant) && property.Name == "RoleScopeId" && value == (int)RoleScopeEnum.Tenant))
            return value.ToString(CultureInfo.InvariantCulture);
        throw new InvalidOperationException("tenant_creation_default_model_drift");
    }

    // Canonical text alone must not admit a same-named schema function/operator.
    // Dependencies on pinned PostgreSQL built-ins may legitimately be absent.
    private static string BuiltinExpressionDependencies(string catalog, string objectId) => $"""
        NOT EXISTS (SELECT 1 FROM pg_catalog.pg_depend dependency
            WHERE dependency.classid='pg_catalog.{catalog}'::regclass AND dependency.objid={objectId}
                AND ((dependency.refclassid='pg_catalog.pg_proc'::regclass AND NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_proc routine WHERE routine.oid=dependency.refobjid
                            AND routine.pronamespace='pg_catalog'::regnamespace))
                    OR (dependency.refclassid='pg_catalog.pg_operator'::regclass AND NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_operator operation WHERE operation.oid=dependency.refobjid
                            AND operation.oprnamespace='pg_catalog'::regnamespace))))
        """;

    private static string ExecutableAssertions(ExploreDbContext context,
        (ProvisioningSharedWriterAssertions.Writer[] Writers, string UndeclaredAssertion) mechanics) => $"""
        DO $creator_mechanics$ BEGIN
            {mechanics.UndeclaredAssertion}
            {string.Join("\n", mechanics.Writers.Select(writer => writer.AssertInstalled(context)))}
        END $creator_mechanics$;
        """;

    private static string ForeignKeysPredicate(ExploreDbContext context, Source source)
        => DeclaredForeignKeysPredicate(context, source.Type, source.Table);

    internal static string DeclaredForeignKeysPredicate(ExploreDbContext context, Type type, string table)
    {
        var names = new HumanPrincipalSourceSql(context);
        string KeyColumns(Type type, IEnumerable<IProperty> properties) => "ARRAY["
            + string.Join(",", properties.Select(property => Text(names.ColumnName(type, property.Name)))) + "]::name[]";
        string[] required = context.Model.FindEntityType(type)!.GetForeignKeys().Select(key => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_constraint f
                WHERE f.conrelid=pg_catalog.to_regclass({Text(table)}) AND f.contype='f'
                    AND f.confrelid=pg_catalog.to_regclass({Text(names.Table(key.PrincipalEntityType.ClrType))})
                    AND f.convalidated AND NOT f.condeferrable AND NOT f.condeferred
                    AND f.confmatchtype='s' AND f.confupdtype='a'
                    AND f.confdeltype='{(key.DeleteBehavior == DeleteBehavior.Cascade ? "c"
                        : key.DeleteBehavior == DeleteBehavior.SetNull ? "n" : key.DeleteBehavior == DeleteBehavior.Restrict ? "r" : "a")}'
                    AND ARRAY(SELECT a.attname FROM unnest(f.conkey) WITH ORDINALITY k(id,ord)
                        JOIN pg_catalog.pg_attribute a ON a.attrelid=f.conrelid AND a.attnum=k.id ORDER BY k.ord)
                        = {KeyColumns(type, key.Properties)}
                    AND ARRAY(SELECT a.attname FROM unnest(f.confkey) WITH ORDINALITY k(id,ord)
                        JOIN pg_catalog.pg_attribute a ON a.attrelid=f.confrelid AND a.attnum=k.id ORDER BY k.ord)
                        = {KeyColumns(key.PrincipalEntityType.ClrType, key.PrincipalKey.Properties)}
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint=f.oid AND t.tgisinternal)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_trigger t WHERE t.tgconstraint=f.oid AND t.tgenabled NOT IN ('O','A')))
            """).ToArray();
        return required.Length == 0 ? "true" : string.Join(" AND ", required);
    }

    private static string FreshPredicate(ExploreDbContext context, Source[] sources, string global) =>
        string.Join(" AND ", sources.Select(source =>
        {
            PostgresUserTenantMembershipReadPermissions.TryGetGlobalReadColumns(context, source.Type, out var membershipColumns);
            var preexistingReads = membershipColumns.Concat(source.Type == typeof(Tenant)
                ? new[] { source.Columns["Id"].Name } : []).Distinct(StringComparer.Ordinal);
            string baseline = string.Join(" OR ", preexistingReads.Select(column =>
                    $"a.attname={Text(column)} AND acl.privilege_type='SELECT'")
                .Concat(PostgresTenantMembershipMutationPermissions.GlobalUpdateColumns(context, source.Type)
                    .Select(column => $"a.attname={Text(column)} AND acl.privilege_type='UPDATE'")));
            return $"""
                NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass({Text(source.Table)})
                        AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(global)})
                        AND NOT ({(baseline.Length == 0 ? "false" : baseline)}))
                AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(source.Table)})
                    AND p.polname IN ({Text(ReadPolicy)},{Text(InsertPolicy)},{Text(DocumentInsertPolicy)}))
                """;
        }));

    private static string[] Policies(Source source) => !source.TenantScoped ? []
        : [.. (source.Read.Length == 0 ? Array.Empty<string>() : [ReadPolicy]),
            .. (source.Insert.Length == 0 ? Array.Empty<string>() : [InsertPolicy]),
            .. (source.Type == typeof(TenantSettingsDocument) ? new[] { DocumentInsertPolicy } : [])];

    private static string SealedSession(string login) => $"SESSION_USER = CURRENT_USER AND CURRENT_USER = {Text(login)}::name AND NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'') IS NULL";

    private static string PolicyPredicate(Source source, string login, string policy)
    {
        string sessionExpression = $"'((SESSION_USER = CURRENT_USER) AND (CURRENT_USER = ' || pg_catalog.quote_literal({Text(login)}) || '::name) AND (NULLIF(current_setting(''app.current_tenant_id''::text, true), ''''::text) IS NULL)'";
        string expression = sessionExpression + " || ')'";
        if (policy == DocumentInsertPolicy)
        {
            string key = $"pg_catalog.quote_ident({Text(source.Columns[nameof(TenantSettingsDocument.DocumentKey)].Name)})";
            expression = sessionExpression + $"""
                 || ' AND (((' || {key} || ')::text = ' || pg_catalog.quote_literal({Text(SettingsDocumentKeys.Tenant.Branding)})
                 || '::text) OR ((' || {key} || ')::text = ' || pg_catalog.quote_literal({Text(SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity)}) || '::text)))'
                """;
        }
        return $"""
            p.polrelid=pg_catalog.to_regclass({Text(source.Table)}) AND p.polname={Text(policy)}
            AND p.polcmd='{(policy == ReadPolicy ? "r" : "a")}' AND p.polpermissive={(policy == DocumentInsertPolicy ? "false" : "true")}
            AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(login)})]
            AND {(policy == ReadPolicy ? $"pg_catalog.pg_get_expr(p.polqual,p.polrelid)=({expression}) AND p.polwithcheck IS NULL" : $"p.polqual IS NULL AND pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)=({expression})")}
            AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PolicyStamp(source, login, policy))}
                || COALESCE(pg_catalog.md5(p.polqual::text),'') || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),'')
            """;
    }

    private static string PolicyStamp(Source source, string login, string policy) => "tenant-creation:v1:"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source.Schema}\n{source.Name}\n{login}\n{policy}"))) + ":";

    private static Source[] Sources(ExploreDbContext context)
    {
        var tenantTables = PostgresTenantRowLevelSecurityModel.GetTenantTables(context.Model);
        return Inventory.Select(item =>
        {
            var entity = context.Model.FindEntityType(item.Type) ?? throw new InvalidOperationException("tenant_creation_model_missing");
            string name = entity.GetTableName() ?? throw new InvalidOperationException("tenant_creation_table_missing");
            string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema ?? entity.GetSchema() ?? "public";
            var store = StoreObjectIdentifier.Table(name, entity.GetSchema());
            if (entity.BaseType is not null || entity.GetDerivedTypes().Any() || entity.GetComplexProperties().Any()
                || entity.FindPrimaryKey()?.Properties is not [var key] || key.Name != "Id"
                || item.Insert.Length != 0 && !entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(item.Insert.Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("tenant_creation_model_shape_drift");
            var columns = item.Read.Concat(item.Insert).Distinct(StringComparer.Ordinal).ToDictionary(property => property, property =>
            {
                var mapped = entity.FindProperty(property) ?? throw new InvalidOperationException("tenant_creation_column_missing");
                if (mapped.IsShadowProperty()) throw new InvalidOperationException("tenant_creation_column_drift");
                return new Column(mapped.GetColumnName(store) ?? throw new InvalidOperationException("tenant_creation_column_missing"),
                    mapped.GetRelationalTypeMapping().StoreType, !mapped.IsNullable);
            }, StringComparer.Ordinal);
            return new Source(item.Type, schema, name, context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, schema),
                columns, item.Read, item.Insert, tenantTables.Any(table => table.TableName == name && (table.Schema ?? schema) == schema));
        }).ToArray();
    }

    private sealed record Column(string Name, string StoreType, bool Required);
    private sealed record Source(Type Type, string Schema, string Name, string Table, Dictionary<string, Column> Columns,
        string[] Read, string[] Insert, bool TenantScoped);

    private static DbCommand Command(ExploreDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? 30;
        command.CommandText = sql;
        return command;
    }

    private static async Task<object?> ScalarAsync(ExploreDbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(context, sql);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(ExploreDbContext context, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(context, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
