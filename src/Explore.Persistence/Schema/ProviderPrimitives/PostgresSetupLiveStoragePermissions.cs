using System.Data;
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

/// <summary>Fixed setup-live storage rights; admission, lifecycle and dispatch remain application-owned.</summary>
public static class PostgresSetupLiveStoragePermissions
{
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
                        nameof(SetupSecretBindingOperation.SettledAt), nameof(SetupSecretBindingOperation.ConcurrencyStamp),
                        nameof(SetupSecretBindingOperation.UpdatedAt), nameof(SetupSecretBindingOperation.UpdatedBy)
                    ])
                ];
                var names = context.GetService<ISqlGenerationHelper>();
                string grantee = names.DelimitIdentifier(enrollment.RuntimeLogin);
                foreach (var (type, updates) in storage)
                {
                    var entity = context.Model.FindEntityType(type)!;
                    string schema = entity.GetSchema() ?? context.Model.GetDefaultSchema()!;
                    var store = StoreObjectIdentifier.Table(entity.GetTableName()!, schema);
                    string table = names.DelimitIdentifier(store.Name, schema);
                    string columns = string.Join(',', entity.GetProperties()
                        .Select(property => names.DelimitIdentifier(property.GetColumnName(store)!)).Distinct());
                    string update = updates.Length == 0 ? string.Empty
                        : $"GRANT UPDATE ({string.Join(',', updates.Select(property => names.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!)))}) ON {table} TO {grantee};";
                    RequireOriginal();
                    await context.Database.ExecuteSqlRawAsync(
                        $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(schema)} TO {grantee}; GRANT SELECT ({columns}), INSERT ({columns}) ON {table} TO {grantee}; {update}",
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
