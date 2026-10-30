using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public async Task SelectedOwnerHasOnlyApprovedMetadataColumnsAndNoBindingMutationAuthority(bool runtime)
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
            Column(nameof(SecretBinding.SettingScopeId)), Column(nameof(SecretBinding.ScopeId)),
            .. (runtime ? new[] { Column(nameof(SecretBinding.Id)) } : Array.Empty<string>())
        ];
        await using (var privileges = caller.Database.GetDbConnection().CreateCommand())
        {
            privileges.CommandText = """
                SELECT session_user = @login AND current_user = session_user
                    AND NOT pg_catalog.has_table_privilege(current_user,@relation,
                        'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER,MAINTAIN')
                    AND (SELECT count(*)=@column_count AND bool_and(
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
            privileges.Parameters.Add(new NpgsqlParameter("column_count", runtime ? 5 : 4));
            await Assert.That(await privileges.ExecuteScalarAsync(token)).IsEqualTo(true);
        }
        string key = names.DelimitIdentifier(Column(nameof(SecretBinding.SettingKey)));
        string privateReference = names.DelimitIdentifier(Column(nameof(SecretBinding.EnvironmentVariableName)));
        string[] denied =
        [
            $"SELECT * FROM {relation} WHERE false",
            $"SELECT {privateReference} FROM {relation} WHERE false",
            $"SELECT {names.DelimitIdentifier(Column(nameof(SecretBinding.InfisicalEnvironment)))} FROM {relation} WHERE false",
            $"SELECT {names.DelimitIdentifier(Column(nameof(SecretBinding.InfisicalPath)))} FROM {relation} WHERE false",
            $"SELECT {names.DelimitIdentifier(Column(nameof(SecretBinding.InfisicalKey)))} FROM {relation} WHERE false",
            $"SELECT ctid FROM {relation} WHERE false",
            $"UPDATE {relation} SET {key}={key} WHERE false",
            $"DELETE FROM {relation} WHERE false",
            .. (runtime ? Array.Empty<string>() : new[] { $"SELECT {names.DelimitIdentifier(Column(nameof(SecretBinding.Id)))} FROM {relation} WHERE false" })
        ];
        foreach (string sql in denied)
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
    public async Task ExactFourColumnPredecessorUpgradesOnlyRuntimeIdAndPreservesTransactionOwnership()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        var entity = migrator.Model.FindEntityType(typeof(SecretBinding))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = migrator.GetService<ISqlGenerationHelper>();
        string relation = names.DelimitIdentifier(store.Name, store.Schema);
        string id = entity.FindProperty(nameof(SecretBinding.Id))!.GetColumnName(store)!;
        string privateReference = entity.FindProperty(nameof(SecretBinding.EnvironmentVariableName))!.GetColumnName(store)!;
        string runtime = fixture.NativeQueueEnrollment.RuntimeLogin;
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(token);
            await migrator.Database.ExecuteSqlRawAsync(
                $"REVOKE SELECT ({names.DelimitIdentifier(id)}) ON {relation} FROM {names.DelimitIdentifier(runtime)}", token);
            await transaction.CommitAsync(token);
        });
        try
        {
            await using var caller = await OpenCallerAsync(true, Guid.CreateVersion7(), token);
            await caller.Database.OpenConnectionAsync(token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(caller, fixture.NativeQueueEnrollment, token));

            await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await migrator.Database.BeginTransactionAsync(token);
                var connection = migrator.Database.GetDbConnection();
                int backend = ((NpgsqlConnection)connection).ProcessID;
                await PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
                await PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
                await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(migrator.Database.GetDbConnection()).IsSameReferenceAs(connection);
                await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
                await Assert.That(await HasColumnReadAsync(migrator, runtime, relation, id, token)).IsTrue();
                await Assert.That(await HasColumnReadAsync(migrator, fixture.NativeQueueEnrollment.GlobalLogin, relation, id, token)).IsFalse();
                await transaction.RollbackAsync(token);
            });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(caller, fixture.NativeQueueEnrollment, token));

            await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await migrator.Database.BeginTransactionAsync(token);
                await PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
                await transaction.CommitAsync(token);
            });
            await PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(caller, fixture.NativeQueueEnrollment, token);
            await using (var readId = caller.Database.GetDbConnection().CreateCommand())
            {
                readId.CommandText = $"SELECT {names.DelimitIdentifier(id)} FROM {relation} WHERE false";
                await readId.ExecuteNonQueryAsync(token);
            }
            await using var global = await OpenCallerAsync(false, Guid.CreateVersion7(), token);
            await global.Database.OpenConnectionAsync(token);
            await PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment, token);
            await AssertColumnReadDeniedAsync(global, relation, names.DelimitIdentifier(id), token);
            await AssertColumnReadDeniedAsync(caller, relation, names.DelimitIdentifier(privateReference), token);
            await AssertColumnReadDeniedAsync(global, relation, names.DelimitIdentifier(privateReference), token);
        }
        finally
        {
            await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await migrator.Database.BeginTransactionAsync(CancellationToken.None);
                await migrator.Database.ExecuteSqlRawAsync(
                    $"GRANT SELECT ({names.DelimitIdentifier(id)}) ON {relation} TO {names.DelimitIdentifier(runtime)}", CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
            });
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CorruptedPredecessorIsRejectedWithoutRepair(bool privateGrant)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        var entity = migrator.Model.FindEntityType(typeof(SecretBinding))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = migrator.GetService<ISqlGenerationHelper>();
        string relation = names.DelimitIdentifier(store.Name, store.Schema);
        string Column(string property) => entity.FindProperty(property)!.GetColumnName(store)!;
        string runtime = fixture.NativeQueueEnrollment.RuntimeLogin;
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(token);
            await migrator.Database.ExecuteSqlRawAsync(
                $"REVOKE SELECT ({names.DelimitIdentifier(Column(nameof(SecretBinding.Id)))}) ON {relation} FROM {names.DelimitIdentifier(runtime)}", token);
            string corruption = privateGrant
                ? $"GRANT SELECT ({names.DelimitIdentifier(Column(nameof(SecretBinding.EnvironmentVariableName)))}) ON {relation} TO {names.DelimitIdentifier(runtime)}"
                : $"REVOKE SELECT ({names.DelimitIdentifier(Column(nameof(SecretBinding.Qualifier)))}) ON {relation} FROM {names.DelimitIdentifier(runtime)}";
            await migrator.Database.ExecuteSqlRawAsync(corruption, token);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
            await Assert.That(failure!.Message).IsEqualTo("localization_status_read_permissions_drift");
            await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(await HasColumnReadAsync(migrator, runtime, relation, Column(nameof(SecretBinding.Id)), token)).IsFalse();
            await Assert.That(await HasColumnReadAsync(migrator, runtime, relation, Column(nameof(SecretBinding.EnvironmentVariableName)), token)).IsEqualTo(privateGrant);
            await Assert.That(await HasColumnReadAsync(migrator, runtime, relation, Column(nameof(SecretBinding.Qualifier)), token)).IsEqualTo(privateGrant);
            await transaction.RollbackAsync(token);
        });
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SavepointReleaseCancellationEitherCompletesOrRollsBackWithoutLeakingId(
        bool callerOwned, bool abortRelease)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        var entity = migrator.Model.FindEntityType(typeof(SecretBinding))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = migrator.GetService<ISqlGenerationHelper>();
        string relation = names.DelimitIdentifier(store.Name, store.Schema);
        string id = entity.FindProperty(nameof(SecretBinding.Id))!.GetColumnName(store)!;
        string runtime = fixture.NativeQueueEnrollment.RuntimeLogin;
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var predecessor = await migrator.Database.BeginTransactionAsync(token);
            await migrator.Database.ExecuteSqlRawAsync(
                $"REVOKE SELECT ({names.DelimitIdentifier(id)}) ON {relation} FROM {names.DelimitIdentifier(runtime)}", token);
            await predecessor.CommitAsync(token);
        });
        try
        {
            await migrator.Database.OpenConnectionAsync(token);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var interception = new SavepointReleaseCancellation(cancellation, abortRelease);
            await using var installation = OpenMigrator(interception);
            await installation.Database.OpenConnectionAsync(token);
            await installation.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var callerTransaction = callerOwned
                    ? await installation.Database.BeginTransactionAsync(token) : null;
                var connection = installation.Database.GetDbConnection();
                int backend = ((NpgsqlConnection)connection).ProcessID;
                if (abortRelease)
                {
                    await Assert.ThrowsAsync<OperationCanceledException>(() =>
                        PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(
                            installation, fixture.NativeQueueEnrollment, cancellation.Token));
                }
                else
                {
                    // Completion wins cancellation delivered after the final pre-release admission.
                    await PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(
                        installation, fixture.NativeQueueEnrollment, cancellation.Token);
                }
                await Assert.That(interception.Observed).IsTrue();
                await Assert.That(cancellation.IsCancellationRequested).IsTrue();
                await Assert.That(installation.Database.GetDbConnection()).IsSameReferenceAs(connection);
                await Assert.That(((NpgsqlConnection)connection).ProcessID).IsEqualTo(backend);
                await Assert.That(await HasColumnReadAsync(installation, runtime, relation, id, token)).IsEqualTo(!abortRelease);
                if (callerTransaction is not null)
                {
                    await Assert.That(installation.Database.CurrentTransaction).IsSameReferenceAs(callerTransaction);
                    await callerTransaction.RollbackAsync(token);
                }
                else
                {
                    await Assert.That(installation.Database.CurrentTransaction).IsNull();
                }
            });
            await Assert.That(await HasColumnReadAsync(migrator, runtime, relation, id, token))
                .IsEqualTo(!callerOwned && !abortRelease);
        }
        finally
        {
            await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var cleanup = await migrator.Database.BeginTransactionAsync(CancellationToken.None);
                await migrator.Database.ExecuteSqlRawAsync(
                    $"GRANT SELECT ({names.DelimitIdentifier(id)}) ON {relation} TO {names.DelimitIdentifier(runtime)}", CancellationToken.None);
                await cleanup.CommitAsync(CancellationToken.None);
            });
        }
    }

    [Test]
    public async Task ForeignGrantorAlongsideMigratorGrantRejectsCurrentAndPredecessorWithoutRepair()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        var entity = migrator.Model.FindEntityType(typeof(SecretBinding))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = migrator.GetService<ISqlGenerationHelper>();
        string relation = names.DelimitIdentifier(store.Name, store.Schema);
        string schema = names.DelimitIdentifier(store.Schema ?? "public");
        string key = entity.FindProperty(nameof(SecretBinding.SettingKey))!.GetColumnName(store)!;
        string id = entity.FindProperty(nameof(SecretBinding.Id))!.GetColumnName(store)!;
        string runtime = fixture.NativeQueueEnrollment.RuntimeLogin;
        string grantor = "localization_grantor_" + Guid.CreateVersion7().ToString("N");
        string delegated = names.DelimitIdentifier(grantor);
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var setup = await migrator.Database.BeginTransactionAsync(token);
            // The fixture's existing postgres migrator delegates one column to a test-owned NOLOGIN role.
            await migrator.Database.ExecuteSqlRawAsync($"""
                CREATE ROLE {delegated} NOLOGIN;
                GRANT USAGE ON SCHEMA {schema} TO {delegated};
                GRANT SELECT ({names.DelimitIdentifier(key)}) ON {relation} TO {delegated} WITH GRANT OPTION;
                SET LOCAL ROLE {delegated};
                GRANT SELECT ({names.DelimitIdentifier(key)}) ON {relation} TO {names.DelimitIdentifier(runtime)};
                RESET ROLE;
                """, token);
            await setup.CommitAsync(token);
        });
        try
        {
            await using var caller = await OpenCallerAsync(true, Guid.CreateVersion7(), token);
            await caller.Database.OpenConnectionAsync(token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(caller, fixture.NativeQueueEnrollment, token));
            await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var predecessor = await migrator.Database.BeginTransactionAsync(token);
                await migrator.Database.ExecuteSqlRawAsync(
                    $"REVOKE SELECT ({names.DelimitIdentifier(id)}) ON {relation} FROM {names.DelimitIdentifier(runtime)}", token);
                await using var origins = migrator.Database.GetDbConnection().CreateCommand();
                origins.Transaction = predecessor.GetDbTransaction();
                origins.CommandText = """
                    SELECT count(*)=2 AND bool_and(acl.privilege_type='SELECT' AND NOT acl.is_grantable)
                        AND count(*) FILTER (WHERE acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@migrator))=1
                        AND count(*) FILTER (WHERE acl.grantor=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@grantor))=1
                    FROM pg_catalog.pg_attribute a CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                    WHERE a.attrelid=pg_catalog.to_regclass(@relation) AND a.attname=@column
                        AND acl.grantee=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=@runtime)
                    """;
                origins.Parameters.Add(new NpgsqlParameter("migrator", fixture.NativeQueueEnrollment.MigratorLogin));
                origins.Parameters.Add(new NpgsqlParameter("grantor", grantor));
                origins.Parameters.Add(new NpgsqlParameter("relation", relation));
                origins.Parameters.Add(new NpgsqlParameter("column", key));
                origins.Parameters.Add(new NpgsqlParameter("runtime", runtime));
                await Assert.That(await origins.ExecuteScalarAsync(token)).IsEqualTo(true);
                var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
                await Assert.That(failure!.Message).IsEqualTo("localization_status_read_permissions_drift");
                await Assert.That(migrator.Database.CurrentTransaction).IsSameReferenceAs(predecessor);
                await Assert.That(await HasColumnReadAsync(migrator, runtime, relation, id, token)).IsFalse();
                await Assert.That(await origins.ExecuteScalarAsync(token)).IsEqualTo(true);
                await predecessor.RollbackAsync(token);
            });
        }
        finally
        {
            await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var cleanup = await migrator.Database.BeginTransactionAsync(CancellationToken.None);
                await migrator.Database.ExecuteSqlRawAsync($"""
                    SET LOCAL ROLE {delegated};
                    REVOKE SELECT ({names.DelimitIdentifier(key)}) ON {relation} FROM {names.DelimitIdentifier(runtime)};
                    RESET ROLE;
                    REVOKE SELECT ({names.DelimitIdentifier(key)}) ON {relation} FROM {delegated};
                    REVOKE USAGE ON SCHEMA {schema} FROM {delegated};
                    DROP ROLE {delegated};
                    """, CancellationToken.None);
                await cleanup.CommitAsync(CancellationToken.None);
            });
        }
    }

    private static async Task<bool> HasColumnReadAsync(
        ExploreDbContext context, string login, string relation, string column, CancellationToken token)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT pg_catalog.has_column_privilege(@login,@relation,@column,'SELECT')";
        command.Parameters.Add(new NpgsqlParameter("login", login));
        command.Parameters.Add(new NpgsqlParameter("relation", relation));
        command.Parameters.Add(new NpgsqlParameter("column", column));
        return await command.ExecuteScalarAsync(token) is true;
    }

    private static async Task AssertColumnReadDeniedAsync(
        ExploreDbContext context, string relation, string column, CancellationToken token)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT {column} FROM {relation} WHERE false";
            await command.ExecuteNonQueryAsync(token);
        });
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
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

    private ExploreDbContext OpenMigrator(params IInterceptor[] interceptors)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        options.AddInterceptors(interceptors);
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

    private sealed class SavepointReleaseCancellation(
        CancellationTokenSource cancellation, bool abortRelease) : DbTransactionInterceptor
    {
        public bool Observed { get; private set; }

        public override ValueTask<InterceptionResult> ReleasingSavepointAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (abortRelease && !Observed)
            {
                Observed = true;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return ValueTask.FromResult(result);
        }

        public override Task ReleasedSavepointAsync(
            DbTransaction transaction, TransactionEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!abortRelease && !Observed)
            {
                Observed = true;
                cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    /// <summary>Separate fixture identity prevents retirement from affecting other native test classes.</summary>
    public sealed class LocalizationFixture : PostgreSqlContainerFixture;
}
