using Explore.Domain.SetupLive;
using Explore.Domain.Secrets;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>Fixed EF storage rights; enrollment authorization and mutation settlement remain application owned.</summary>
public static class PostgresSetupLiveStoragePermissions
{
    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Database.IsNpgsql()) return;
        enrollment.Validate();
        var names = context.GetService<ISqlGenerationHelper>();
        string runtime = names.DelimitIdentifier(enrollment.RuntimeLogin);
        string migrator = enrollment.MigratorLogin.Replace("'", "''", StringComparison.Ordinal);
        Type[] entities = [typeof(SetupTargetEnrollment), typeof(SetupEnrollmentIssuanceClaim), typeof(SetupSecretBindingOperation)];
        string grants = string.Join("\n", entities.Select(type =>
        {
            var entity = context.Model.FindEntityType(type)
                ?? throw new InvalidOperationException("Setup storage mapping is required.");
            string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            string schema = entity.GetSchema() ?? context.Model.GetDefaultSchema()
                ?? throw new InvalidOperationException("Setup storage schema is required.");
            return $"GRANT USAGE ON SCHEMA {names.DelimitIdentifier(schema)} TO {runtime}; GRANT SELECT, INSERT, UPDATE ON {table} TO {runtime};";
        }));
        var binding = context.Model.FindEntityType(typeof(SecretBinding))
            ?? throw new InvalidOperationException("Secret binding metadata mapping is required.");
        string bindingTable = names.DelimitIdentifier(binding.GetTableName()!, binding.GetSchema());
        await context.Database.ExecuteSqlRawAsync($"""
            DO $setup_storage$
            BEGIN
                IF session_user <> '{migrator}' OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none', session_user::text)
                THEN RAISE EXCEPTION 'setup_storage_migrator_required' USING ERRCODE = '42501'; END IF;
                {grants}
                GRANT SELECT ON {bindingTable} TO {runtime};
            END $setup_storage$;
            """, cancellationToken);
    }
}
