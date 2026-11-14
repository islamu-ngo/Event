using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Installs fixed storage rights for Global recognition recovery and retention,
/// the Global replay checkpoint chain, and Runtime readiness reads.
/// Tenant policies and erasure authority remain separate.
/// </summary>
public static class PostgresPrivacyReplayStoragePermissions
{
    private static readonly (Type Entity, string Operations)[] Sources =
    [
        (typeof(ModerationRecognitionPending), "SELECT, INSERT, UPDATE, DELETE"),
        (typeof(ModerationRecognitionCaptureItem), "SELECT, INSERT, UPDATE, DELETE"),
        (typeof(ModerationRecognitionDisposition), "SELECT, INSERT, UPDATE"),
        (typeof(ModerationRecognitionActionRecovery), "SELECT, INSERT, UPDATE, DELETE"),
        (typeof(ModerationRecognitionContinuityBinding), "SELECT, INSERT, UPDATE, DELETE"),
        (typeof(ModerationRecognitionKeyLifecyclePurge), "SELECT, INSERT, DELETE"),
        (typeof(ModerationRecognitionKeyLifecycle), "SELECT, INSERT, UPDATE"),
        (typeof(ModerationRecognitionKeyLifecycleKey), "SELECT, INSERT, UPDATE"),
        (typeof(KeyObligation), "SELECT, INSERT, UPDATE, DELETE"),
        (typeof(KeyReplica), "SELECT, INSERT"),
        (typeof(ModerationHostBinding), "SELECT"),
        (typeof(ModerationRecognitionPrivacySource), "SELECT"),
        (typeof(ModerationRecognitionPrivacyRetention), "SELECT")
    ];

    public static async Task ApplyAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var identityQuery = context.Database.GetDbConnection().CreateCommand();
        identityQuery.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        identityQuery.CommandText = "SELECT current_user = @migrator AND session_user = @migrator";
        var migratorParameter = identityQuery.CreateParameter();
        migratorParameter.ParameterName = "migrator";
        migratorParameter.Value = enrollment.MigratorLogin;
        identityQuery.Parameters.Add(migratorParameter);
        bool migrator = await identityQuery.ExecuteScalarAsync(cancellationToken) is true;
        if (!migrator)
            throw new UnauthorizedAccessException("recognition_privacy_installer_wrong_identity");

        var names = context.GetService<ISqlGenerationHelper>();
        string login = names.DelimitIdentifier(enrollment.GlobalLogin);
        var grants = new List<string>();
        foreach (var source in Sources)
        {
            var entity = context.Model.FindEntityType(source.Entity)
                ?? throw new InvalidOperationException("recognition_privacy_storage_mapping_missing");
            string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            grants.Add($"GRANT {source.Operations} ON TABLE {table} TO {login};");
        }

        var checkpoint = context.Model.FindEntityType(typeof(PrivacyErasureReplayCheckpoint))
            ?? throw new InvalidOperationException("privacy_replay_checkpoint_storage_mapping_missing");
        string checkpointTable = names.DelimitIdentifier(checkpoint.GetTableName()!, checkpoint.GetSchema());
        string runtime = names.DelimitIdentifier(enrollment.RuntimeLogin);
        grants.Add($"GRANT SELECT, INSERT ON TABLE {checkpointTable} TO {login};");
        grants.Add($"REVOKE INSERT ON TABLE {checkpointTable} FROM {runtime};");
        grants.Add($"GRANT SELECT ON TABLE {checkpointTable} TO {runtime};");

        (Type Entity, string[] Properties)[] healthSources =
        [
            (typeof(PrivacyErasureProviderWork),
                [nameof(PrivacyErasureProviderWork.Status), nameof(PrivacyErasureProviderWork.NextAttemptAtUtc)]),
            (typeof(OutboxMessage), [nameof(OutboxMessage.EventType), nameof(OutboxMessage.Status)])
        ];
        foreach (var (type, properties) in healthSources)
        {
            var entity = context.Model.FindEntityType(type)!;
            var healthStore = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string table = names.DelimitIdentifier(healthStore.Name, healthStore.Schema);
            string columns = string.Join(',', properties.Select(property =>
                names.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(healthStore)!)));
            grants.Add($"GRANT SELECT ({columns}) ON TABLE {table} TO {runtime};");
        }

        // PostgreSQL row fences require UPDATE on the exact identity column.
        var host = context.Model.FindEntityType(typeof(ModerationHostBinding))!;
        var store = StoreObjectIdentifier.Table(host.GetTableName()!, host.GetSchema());
        string hostTable = names.DelimitIdentifier(host.GetTableName()!, host.GetSchema());
        string identity = names.DelimitIdentifier(
            host.FindProperty(nameof(ModerationHostBinding.Id))!.GetColumnName(store)!);
        grants.Add($"GRANT UPDATE ({identity}) ON TABLE {hostTable} TO {login};");
        // EF has no permission DDL API. Operations are source-defined; every
        // relation, column and login identifier is provider-quoted.
        await context.Database.ExecuteSqlRawAsync(string.Join('\n', grants), cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }
}
