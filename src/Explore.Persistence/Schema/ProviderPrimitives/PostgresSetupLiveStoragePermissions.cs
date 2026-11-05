using System.Data;
using Explore.Domain;
using Explore.Domain.SetupLive;
using Explore.Domain.Secrets;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Fixed setup and bootstrap storage rights; admission, lifecycle and dispatch remain application-owned.</summary>
public static class PostgresSetupLiveStoragePermissions
{
    private static readonly string[] BootstrapProperties =
    [
        nameof(InstanceBootstrapState.Id), nameof(InstanceBootstrapState.CreatedAt),
        nameof(InstanceBootstrapState.Mode), nameof(InstanceBootstrapState.DeploymentMode),
        nameof(InstanceBootstrapState.Generation), nameof(InstanceBootstrapState.ProviderKind),
        nameof(InstanceBootstrapState.ConfigurationFingerprint), nameof(InstanceBootstrapState.SelectorFingerprint),
        nameof(InstanceBootstrapState.Status), nameof(InstanceBootstrapState.SupersededAt),
        nameof(InstanceBootstrapState.CompletedAt), nameof(InstanceBootstrapState.CompletedByUserId),
        nameof(InstanceBootstrapState.CompletedIdentityFingerprint)
    ];

    internal static string[] GlobalBootstrapColumns(ExploreDbContext context, Type type)
    {
        if (type != typeof(InstanceBootstrapState)) return [];
        var entity = context.Model.FindEntityType(type)!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return BootstrapProperties.Select(property =>
            entity.FindProperty(property)!.GetColumnName(store)!).ToArray();
    }

    internal static string GlobalBootstrapCataloguePredicate(
        ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var entity = context.Model.FindEntityType(typeof(InstanceBootstrapState))!;
        string table = context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string global = Text(enrollment.GlobalLogin);
        string migrator = Text(enrollment.MigratorLogin);
        string expected = "VALUES " + string.Join(',',
            GlobalBootstrapColumns(context, typeof(InstanceBootstrapState))
                .SelectMany(column => new[] { "SELECT", "INSERT", "UPDATE" }
                    .Select(privilege => $"({Text(column)},{Text(privilege)})")));
        return $"""
            (WITH expected(column_name,privilege) AS ({expected}),
            members AS (SELECT r.* FROM pg_catalog.pg_roles r WHERE pg_catalog.pg_has_role({global},r.oid,'MEMBER'))
            SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_class t
                WHERE t.oid=pg_catalog.to_regclass({Text(table)})
                    AND t.relowner=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={migrator})
                    AND NOT EXISTS (SELECT 1 FROM members r
                        WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                            OR r.oid=t.relowner
                            OR pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                            OR {PostgresPrivilegeGuard.MaintainPrivilegeSql("r.oid", "t.oid", false)})
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(t.relacl) acl WHERE acl.grantee=0)
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE a.attrelid=t.oid AND acl.grantee=0)
                    AND NOT EXISTS (SELECT 1 FROM expected e WHERE NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                        WHERE a.attrelid=t.oid AND a.attname=e.column_name
                            AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={global})
                            AND acl.grantor=t.relowner AND acl.privilege_type=e.privilege AND NOT acl.is_grantable))
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_attribute a CROSS JOIN members r
                        CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('REFERENCES')) permission(privilege)
                        WHERE a.attrelid=t.oid AND a.attnum<>0 AND NOT a.attisdropped
                            AND (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege || ' WITH GRANT OPTION')
                                OR (pg_catalog.has_column_privilege(r.oid,t.oid,a.attnum,permission.privilege)
                                    AND NOT (r.rolname={global} AND a.attnum>0
                                        AND EXISTS (SELECT 1 FROM expected e
                                            WHERE e.column_name=a.attname AND e.privilege=permission.privilege)))))))
            """;
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = context.Database.CurrentTransaction!;
        var original = RelationalConnectionIdentity.Capture(context);
        var contextId = context.ContextId;
        var backend = original.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            if (context.ContextId != contextId || interrupted || !original.Matches(context)
                || original.State != ConnectionState.Open || original.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction)
                || !original.Owns(transaction.GetDbTransaction()))
                throw new UnauthorizedAccessException("setup_live_storage_installer_owner_changed");
        }

        original.StateChange += Changed;
        string savepoint = "setup_live_storage_" + Guid.CreateVersion7().ToString("N");
        try
        {
            RequireOriginal();
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                await using (var identity = context.Database.GetDbConnection().CreateCommand())
                {
                    identity.Transaction = transaction.GetDbTransaction();
                    identity.CommandText = """
                        SELECT session_user = @migrator AND current_user = session_user
                            AND pg_catalog.current_setting('role') IN ('none', session_user::text)
                        """;
                    var parameter = identity.CreateParameter();
                    parameter.ParameterName = "migrator";
                    parameter.Value = enrollment.MigratorLogin;
                    identity.Parameters.Add(parameter);
                    if (await identity.ExecuteScalarAsync(cancellationToken) is not true)
                        throw new UnauthorizedAccessException("setup_live_storage_installer_wrong_identity");
                }
                RequireOriginal();

                (Type Entity, string[] Updates)[] storage =
                [
                    (typeof(InstanceBootstrapState), BootstrapProperties),
                    (typeof(SetupTargetEnrollment),
                    [
                        nameof(SetupTargetEnrollment.CapabilityDigest), nameof(SetupTargetEnrollment.Generation),
                        nameof(SetupTargetEnrollment.State), nameof(SetupTargetEnrollment.ExpiresAt),
                        nameof(SetupTargetEnrollment.RevokedAt), nameof(SetupTargetEnrollment.ExpiredAt),
                        nameof(SetupTargetEnrollment.ConcurrencyStamp),
                        nameof(SetupTargetEnrollment.UpdatedAt), nameof(SetupTargetEnrollment.UpdatedBy)
                    ]),
                    (typeof(SetupEnrollmentIssuanceClaim), []),
                    (typeof(SetupSecretBindingOperation),
                    [
                        nameof(SetupSecretBindingOperation.State), nameof(SetupSecretBindingOperation.Outcome),
                        nameof(SetupSecretBindingOperation.MutationEvidence),
                        nameof(SetupSecretBindingOperation.SettledAt), nameof(SetupSecretBindingOperation.ConcurrencyStamp),
                        nameof(SetupSecretBindingOperation.UpdatedAt), nameof(SetupSecretBindingOperation.UpdatedBy)
                    ])
                ];
                var names = context.GetService<ISqlGenerationHelper>();
                string grantee = names.DelimitIdentifier(enrollment.RuntimeLogin);
                foreach (var (type, updates) in storage)
                {
                    string target = type == typeof(InstanceBootstrapState)
                        ? names.DelimitIdentifier(enrollment.GlobalLogin) : grantee;
                    var entity = context.Model.FindEntityType(type)!;
                    string schema = entity.GetSchema() ?? context.Model.GetDefaultSchema()!;
                    var store = StoreObjectIdentifier.Table(entity.GetTableName()!, schema);
                    string table = names.DelimitIdentifier(store.Name, schema);
                    string columns = string.Join(',', entity.GetProperties()
                        .Select(property => names.DelimitIdentifier(property.GetColumnName(store)!)).Distinct());
                    string update = updates.Length == 0 ? string.Empty
                        : $"GRANT UPDATE ({string.Join(',', updates.Select(property => names.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!)))}) ON {table} TO {target};";
                    RequireOriginal();
                    await context.Database.ExecuteSqlRawAsync(
                        $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(schema)} TO {target}; GRANT SELECT ({columns}), INSERT ({columns}) ON {table} TO {target}; {update}",
                        cancellationToken);
                    RequireOriginal();
                }
                var binding = context.Model.FindEntityType(typeof(SecretBinding))!;
                string bindingSchema = binding.GetSchema() ?? context.Model.GetDefaultSchema()!;
                var bindingStore = StoreObjectIdentifier.Table(binding.GetTableName()!, bindingSchema);
                string[] identityProperties =
                [
                    nameof(SecretBinding.Id), nameof(SecretBinding.SettingKey), nameof(SecretBinding.Qualifier),
                    nameof(SecretBinding.SettingScopeId), nameof(SecretBinding.ScopeId)
                ];
                string identityColumns = string.Join(',', identityProperties.Select(property =>
                    names.DelimitIdentifier(binding.FindProperty(property)!.GetColumnName(bindingStore)!)));
                RequireOriginal();
                await context.Database.ExecuteSqlRawAsync(
                    $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(bindingSchema)} TO {grantee}; GRANT SELECT ({identityColumns}) ON {names.DelimitIdentifier(bindingStore.Name, bindingSchema)} TO {grantee}",
                    cancellationToken);
                RequireOriginal();
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
        finally { original.StateChange -= Changed; }
    }
}
