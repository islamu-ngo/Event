using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class UserTenantNavigationContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EnrolledReadReturnsOnlySelectedLiveUserAndPreference()
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        Guid subject = Guid.CreateVersion7(), deleted = Guid.CreateVersion7(), other = Guid.CreateVersion7();
        Guid preference = Guid.CreateVersion7();
        var mapping = Mapping(context);
        await using (var seed = context.Database.GetDbConnection().CreateCommand())
        {
            seed.Transaction = transaction.GetDbTransaction();
            seed.CommandText = $"""
                INSERT INTO {mapping.Table} ({mapping.Id}, {mapping.Preference}, {mapping.Deleted},
                    {mapping.Column(nameof(User.CreatedAt))}, {mapping.Column(nameof(User.ConcurrencyStamp))})
                VALUES (@subject,@preference,false,CURRENT_TIMESTAMP,@subject),
                    (@deleted,@preference,true,CURRENT_TIMESTAMP,@deleted),
                    (@other,NULL,false,CURRENT_TIMESTAMP,@other)
                """;
            seed.Parameters.Add(new NpgsqlParameter("subject", subject));
            seed.Parameters.Add(new NpgsqlParameter("deleted", deleted));
            seed.Parameters.Add(new NpgsqlParameter("other", other));
            seed.Parameters.Add(new NpgsqlParameter("preference", preference));
            await seed.ExecuteNonQueryAsync();
        }
        await PostgresUserTenantNavigationContract.RequireReadyAsync(context, fixture.NativeQueueEnrollment, default);
        await context.Database.ExecuteSqlRawAsync(
            $"SET LOCAL SESSION AUTHORIZATION {Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
        foreach (var expected in new[]
        {
            (Id: subject, Preference: (Guid?)preference, Exists: true),
            (Id: other, Preference: (Guid?)null, Exists: true),
            (Id: deleted, Preference: (Guid?)null, Exists: false),
            (Id: Guid.CreateVersion7(), Preference: (Guid?)null, Exists: false)
        })
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT * FROM {PostgresUserTenantNavigationContract.FunctionSql(context)}(@subject)";
            command.Parameters.Add(new NpgsqlParameter("subject", expected.Id));
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(reader.FieldCount).IsEqualTo(2);
            await Assert.That(await reader.ReadAsync()).IsEqualTo(expected.Exists);
            if (expected.Exists)
            {
                await Assert.That(reader.GetGuid(0)).IsEqualTo(expected.Id);
                await Assert.That(reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1)).IsEqualTo(expected.Preference);
                await Assert.That(await reader.ReadAsync()).IsFalse();
            }
        }
    }

    [Test]
    public async Task RepositoryUsesSeparateEnrolledGlobalRead()
    {
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var services = new ServiceCollection().AddSingleton(fixture.NativeQueueEnrollment).BuildServiceProvider();
        var repository = new UserTenantNavigationRepository(factory, services);
        await Assert.That(await repository.GetNavigationUserAsync(Guid.CreateVersion7(), default)).IsNull();
    }

    [Test]
    public async Task ReadinessRequiresExactDeploymentEnrollment()
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await PostgresUserTenantNavigationContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        var changed = fixture.NativeQueueEnrollment with
        {
            GlobalLogin = fixture.NativeQueueEnrollment.EmailInstanceMutationLogin,
            EmailInstanceMutationLogin = fixture.NativeQueueEnrollment.GlobalLogin
        };
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresUserTenantNavigationContract.RequireReadyAsync(context, changed, default));
        await Assert.That(denied.SqlState).IsEqualTo("55000");
    }

    [Test]
    [Arguments("executor")]
    [Arguments("runtime-via-login")]
    [Arguments("global-column")]
    [Arguments("runtime-column")]
    [Arguments("owner-write")]
    [Arguments("owner-extra-column")]
    [Arguments("role-flags")]
    [Arguments("membership-options")]
    [Arguments("public-execute")]
    public async Task AuthorityDriftFailsEntryAndInstallerWithoutRepair(string drift)
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var enrollment = fixture.NativeQueueEnrollment;
        var mapping = Mapping(context);
        string signature = PostgresUserTenantNavigationContract.FunctionSql(context) + "(uuid)";
        string grant = drift switch
        {
            "executor" => $"GRANT {PostgresUserTenantNavigationContract.GlobalRole} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
            "runtime-via-login" => $"GRANT {Quote(enrollment.GlobalLogin)} TO {Quote(enrollment.RuntimeLogin)} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
            "global-column" => $"GRANT SELECT ({mapping.Id}) ON {mapping.Table} TO {Quote(enrollment.GlobalLogin)}",
            "runtime-column" => $"GRANT SELECT ({mapping.Id}) ON {mapping.Table} TO {Quote(enrollment.RuntimeLogin)}",
            "owner-write" => $"GRANT UPDATE ({mapping.Preference}) ON {mapping.Table} TO {PostgresUserTenantNavigationContract.OwnerRole}",
            "owner-extra-column" => $"GRANT SELECT ({mapping.Column(nameof(User.CreatedAt))}) ON {mapping.Table} TO {PostgresUserTenantNavigationContract.OwnerRole}",
            "role-flags" => $"ALTER ROLE {PostgresUserTenantNavigationContract.OwnerRole} BYPASSRLS",
            "membership-options" => $"GRANT {PostgresUserTenantNavigationContract.GlobalRole} TO {Quote(enrollment.GlobalLogin)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await context.Database.ExecuteSqlRawAsync(grant);
        await transaction.CreateSavepointAsync("before_entry");
        await context.Database.ExecuteSqlRawAsync(
            $"SET LOCAL SESSION AUTHORIZATION {Quote(enrollment.GlobalLogin)}");
        await using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"SELECT * FROM {PostgresUserTenantNavigationContract.FunctionSql(context)}(@subject)";
            command.Parameters.Add(new NpgsqlParameter("subject", Guid.CreateVersion7()));
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteNonQueryAsync(); });
            await Assert.That(denied.SqlState).IsEqualTo("55000");
        }
        await transaction.RollbackToSavepointAsync("before_entry");
        await transaction.CreateSavepointAsync("before_install");
        var install = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresUserTenantNavigationContract.ApplyAsync(context, enrollment));
        await Assert.That(install.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("before_install");
        // Drift remains visible: installation must fail rather than normalize it away.
        await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresUserTenantNavigationContract.RequireReadyAsync(context, enrollment, default));
    }

    [Test]
    [Arguments("body")]
    [Arguments("argument-name")]
    [Arguments("output-name")]
    [Arguments("volatility")]
    [Arguments("strict")]
    [Arguments("search-path")]
    [Arguments("owner")]
    public async Task RoutineAttestationRejectsChangedDefinition(string drift)
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        string function = PostgresUserTenantNavigationContract.FunctionSql(context);
        string signature = function + "(uuid)";
        if (drift is "body" or "argument-name" or "output-name")
        {
            await using var source = context.Database.GetDbConnection().CreateCommand();
            source.Transaction = transaction.GetDbTransaction();
            source.CommandText = "SELECT prosrc FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure(@signature)";
            source.Parameters.Add(new NpgsqlParameter("signature", signature));
            string body = drift == "body" ? "BEGIN RETURN; END;" : (string)(await source.ExecuteScalarAsync())!;
            string schema = Mapping(context).Schema;
            await context.Database.ExecuteSqlRawAsync($"""
                GRANT CREATE ON SCHEMA {schema} TO {PostgresUserTenantNavigationContract.OwnerRole};
                SET LOCAL ROLE {PostgresUserTenantNavigationContract.OwnerRole};
                DROP FUNCTION {signature};
                CREATE FUNCTION {function}({(drift == "argument-name" ? "p_selector" : "p_user")} uuid)
                RETURNS TABLE({(drift == "output-name" ? "selected_user_id" : "user_id")} uuid, last_active_tenant_id uuid)
                LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $body${body}$body$;
                REVOKE ALL ON FUNCTION {signature} FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION {signature} TO {PostgresUserTenantNavigationContract.GlobalRole};
                RESET ROLE;
                REVOKE CREATE ON SCHEMA {schema} FROM {PostgresUserTenantNavigationContract.OwnerRole};
                """);
        }
        else
        {
            string alteration = drift switch
            {
                "volatility" => "VOLATILE",
                "strict" => "STRICT",
                "search-path" => "SET search_path = public",
                "owner" => $"OWNER TO {Quote(fixture.NativeQueueEnrollment.MigratorLogin)}",
                _ => throw new ArgumentOutOfRangeException(nameof(drift))
            };
            await context.Database.ExecuteSqlRawAsync($"ALTER FUNCTION {signature} {alteration}");
        }
        await transaction.CreateSavepointAsync("before_readiness");
        if (drift is "owner" or "search-path")
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresUserTenantNavigationContract.RequireReadyAsync(context, fixture.NativeQueueEnrollment, default));
            await Assert.That(denied.SqlState).IsEqualTo("55000");
        }
        else
        {
            var denied = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresUserTenantNavigationContract.RequireReadyAsync(context, fixture.NativeQueueEnrollment, default));
            await Assert.That(denied.Message).IsEqualTo("native_user_navigation_contract_drift");
        }
        await transaction.RollbackToSavepointAsync("before_readiness");
        if (drift is "owner" or "search-path")
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PostgresUserTenantNavigationContract.ApplyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.That(denied.SqlState).IsEqualTo("55000");
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresUserTenantNavigationContract.ApplyAsync(context, fixture.NativeQueueEnrollment));
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task UnenrolledSessionCannotExecute(PrimaryDatabaseRole role)
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        string login = fixture.GetNativeQueueOptions(role).Username!;
        await context.Database.ExecuteSqlRawAsync($"SET LOCAL SESSION AUTHORIZATION {Quote(login)}");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = $"SELECT * FROM {PostgresUserTenantNavigationContract.FunctionSql(context)}(@subject)";
        command.Parameters.Add(new NpgsqlParameter("subject", Guid.CreateVersion7()));
        var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteNonQueryAsync(); });
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }

    private ExploreDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        return new ExploreDbContext(options.Options);
    }

    private static (string Schema, string Table, string Id, string Preference, string Deleted, Func<string, string> Column)
        Mapping(ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(User))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        string Column(string name) => sql.DelimitIdentifier(entity.FindProperty(name)!
            .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        return (sql.DelimitIdentifier(schema), sql.DelimitIdentifier(entity.GetTableName()!, schema),
            Column(nameof(User.Id)), Column(nameof(User.LastActiveTenantId)), Column(nameof(User.IsDeleted)), Column);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
