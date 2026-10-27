using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

/// <summary>Exercises only metadata visibility; no secret source is resolved or decrypted.</summary>
[NotInParallel]
[ClassDataSource<LocalizationSecretStatusEfTests.LocalizationFixture>(Shared = SharedType.PerAssembly)]
public sealed class LocalizationSecretStatusEfTests(LocalizationSecretStatusEfTests.LocalizationFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedOwnerReadsBooleanWithoutTheRetiredFunction(bool runtime)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        await using var migrator = OpenMigrator();
        await RemoveRetiredFunctionAsync(migrator, token);
        Guid selected = Guid.CreateVersion7();
        Guid foreign = Guid.CreateVersion7();
        migrator.SecretBindings.AddRange(
            Binding(SecretScope.Tenant, foreign),
            Binding(SecretScope.Tenant, selected, "qualified"));
        await migrator.SaveChangesAsync(token);

        await using var caller = await OpenCallerAsync(runtime, selected, token);
        await caller.Database.OpenConnectionAsync(token);
        var connection = caller.Database.GetDbConnection();
        var contextId = caller.ContextId;
        int backend = ((NpgsqlConnection)connection).ProcessID;
        using var services = new ServiceCollection()
            .AddSingleton(caller)
            .AddSingleton(fixture.NativeQueueEnrollment)
            .BuildServiceProvider();
        var reader = ActivatorUtilities.CreateInstance<LocalizationSecretStatusReader>(services);

        await Assert.That(await reader.IsConfiguredAsync(token)).IsFalse();
        var own = Binding(SecretScope.Tenant, selected);
        migrator.SecretBindings.Add(own);
        await migrator.SaveChangesAsync(token);
        await Assert.That(await reader.IsConfiguredAsync(token)).IsEqualTo(runtime);
        migrator.SecretBindings.Remove(own);
        await migrator.SaveChangesAsync(token);
        await Assert.That(await reader.IsConfiguredAsync(token)).IsFalse();
        migrator.SecretBindings.Add(Binding(SecretScope.Instance, null));
        await migrator.SaveChangesAsync(token);

        await caller.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await caller.Database.BeginTransactionAsync(token);
            await Assert.That(await reader.IsConfiguredAsync(token)).IsTrue();
            await Assert.That(caller.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync(token);
        });
        await Assert.That(caller.ContextId).IsEqualTo(contextId);
        await Assert.That(caller.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
        await Assert.That(caller.TenantFilterTenantId).IsEqualTo(runtime ? (Guid?)selected : null);
        await Assert.That(caller.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedOwnerHasOnlyFourMetadataColumnsAndNoBindingMutationAuthority(bool runtime)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var caller = await OpenCallerAsync(runtime, Guid.CreateVersion7(), token);
        await caller.Database.OpenConnectionAsync(token);
        var entity = caller.Model.FindEntityType(typeof(SecretBinding))!;
        string tableName = entity.GetTableName()!;
        var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var names = caller.GetService<ISqlGenerationHelper>();
        string relation = names.DelimitIdentifier(tableName, entity.GetSchema());
        string Column(string property) => entity.FindProperty(property)!.GetColumnName(store)!;
        string[] permitted =
        [
            Column(nameof(SecretBinding.SettingKey)), Column(nameof(SecretBinding.Qualifier)),
            Column(nameof(SecretBinding.SettingScopeId)), Column(nameof(SecretBinding.ScopeId))
        ];
        await using (var privileges = caller.Database.GetDbConnection().CreateCommand())
        {
            privileges.CommandText = """
                SELECT session_user = @login AND current_user = session_user
                    AND NOT pg_catalog.has_table_privilege(current_user,@relation,
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                    AND (SELECT count(*)=4 AND bool_and(
                        pg_catalog.has_column_privilege(current_user,@relation,column_name,'SELECT'))
                        FROM unnest(@columns::text[]) expected(column_name))
                    AND NOT EXISTS (
                        SELECT 1 FROM pg_catalog.pg_attribute a
                        WHERE a.attrelid=pg_catalog.to_regclass(@relation)
                            AND a.attnum<>0 AND NOT a.attisdropped
                            AND ((a.attname<>ALL(@columns::text[])
                                AND pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,'SELECT'))
                                OR pg_catalog.has_column_privilege(current_user,a.attrelid,a.attnum,
                                    'SELECT WITH GRANT OPTION,INSERT,UPDATE,REFERENCES')))
                """;
            privileges.Parameters.Add(new NpgsqlParameter("login", runtime
                ? fixture.NativeQueueEnrollment.RuntimeLogin : fixture.NativeQueueEnrollment.GlobalLogin));
            privileges.Parameters.Add(new NpgsqlParameter("relation", relation));
            privileges.Parameters.Add(new NpgsqlParameter("columns", permitted));
            await Assert.That(await privileges.ExecuteScalarAsync(token)).IsEqualTo(true);
        }
        string key = names.DelimitIdentifier(Column(nameof(SecretBinding.SettingKey)));
        string privateReference = names.DelimitIdentifier(Column(nameof(SecretBinding.EnvironmentVariableName)));
        foreach (string sql in new[]
        {
            $"SELECT * FROM {relation} WHERE false",
            $"SELECT {privateReference} FROM {relation} WHERE false",
            $"SELECT ctid FROM {relation} WHERE false",
            $"UPDATE {relation} SET {key}={key} WHERE false",
            $"DELETE FROM {relation} WHERE false"
        })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var command = caller.Database.GetDbConnection().CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(token);
            });
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Test]
    public async Task DormantSourcePolicySurvivesReapplicationButEnabledRlsIsRejectedWithoutRepair()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        var entity = migrator.Model.FindEntityType(typeof(SecretBinding))!;
        string relation = migrator.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(token);
            string before = await DormantPolicyImageAsync(migrator, relation, token);
            await PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(await DormantPolicyImageAsync(migrator, relation, token)).IsEqualTo(before);

            await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {relation} ENABLE ROW LEVEL SECURITY", token);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
            await Assert.That(failure!.Message).IsEqualTo("localization_status_read_permissions_drift");
            await using var state = migrator.Database.GetDbConnection().CreateCommand();
            state.Transaction = transaction.GetDbTransaction();
            state.CommandText = "SELECT relrowsecurity FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass(@relation)";
            state.Parameters.Add(new NpgsqlParameter("relation", relation));
            await Assert.That(await state.ExecuteScalarAsync(token)).IsEqualTo(true);
            await transaction.RollbackAsync(token);
        });
    }

    private static async Task<string> DormantPolicyImageAsync(
        ExploreDbContext context, string relation, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT (SELECT jsonb_agg(to_jsonb(p) ORDER BY p.oid)::text
                FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid)
            FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass(@relation)
                AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p
                    WHERE p.polrelid=c.oid AND p.polname='human_event_provider_source_read')
            """;
        command.Parameters.Add(new NpgsqlParameter("relation", relation));
        var image = await command.ExecuteScalarAsync(token);
        await Assert.That(image is string).IsTrue();
        return (string)image!;
    }

    private ExploreDbContext OpenMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private async Task<ExploreDbContext> OpenCallerAsync(bool runtime, Guid tenant, CancellationToken token)
    {
        if (runtime)
            return fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        return await factory.CreateDbContextAsync(token);
    }

    private static async Task RemoveRetiredFunctionAsync(ExploreDbContext migrator, CancellationToken token)
    {
        var entity = migrator.Model.FindEntityType(typeof(SecretBinding))!;
        string routine = migrator.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier("localization_secret_status", entity.GetSchema()) + "(uuid)";
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(token);
            // This class owns its disposable database; no business-function body is recreated.
            await migrator.Database.ExecuteSqlRawAsync($"DROP FUNCTION IF EXISTS {routine}", token);
            await transaction.CommitAsync(token);
        });
        await migrator.Database.OpenConnectionAsync(token);
        await using var absent = migrator.Database.GetDbConnection().CreateCommand();
        absent.CommandText = "SELECT pg_catalog.to_regprocedure(@routine) IS NULL";
        absent.Parameters.Add(new NpgsqlParameter("routine", routine));
        await Assert.That(await absent.ExecuteScalarAsync(token)).IsEqualTo(true);
    }

    private static SecretBinding Binding(SecretScope scope, Guid? tenant, string qualifier = "") => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = SecretDefinitionRegistry.Keys.Localization.TmsApiKey,
        Scope = scope,
        ScopeId = tenant,
        Qualifier = qualifier,
        SourceType = SecretSourceType.EnvironmentVariable,
        EnvironmentVariableName = "LOCALIZATION_TMS_API_KEY",
        LastValidationResult = SecretValidationResult.NotValidated
    };

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    /// <summary>Separate fixture identity prevents retirement from affecting other native test classes.</summary>
    public sealed class LocalizationFixture : PostgreSqlContainerFixture;
}
