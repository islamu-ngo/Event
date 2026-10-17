using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantPreferenceContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SameBodyWithReversedUuidArgumentNamesCannotPassReadiness(bool image)
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string function = image ? PostgresTenantPreferenceContract.ImageFunctionSql(context)
            : PostgresTenantPreferenceContract.FunctionSql(context);
        string signature = function + "(uuid,uuid,timestamp with time zone)";
        await using var source = context.Database.GetDbConnection().CreateCommand();
        source.Transaction = transaction.GetDbTransaction();
        source.CommandText = "SELECT prosrc FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure(@signature)";
        source.Parameters.Add(new NpgsqlParameter("signature", signature));
        string originalBody = (string)(await source.ExecuteScalarAsync())!;
        string schema = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(
            context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
                ?? context.Model.FindEntityType(typeof(User))!.GetSchema() ?? "public");
        await context.Database.ExecuteSqlRawAsync($"""
            GRANT CREATE ON SCHEMA {schema} TO {PostgresTenantPreferenceContract.OwnerRole};
            SET LOCAL ROLE {PostgresTenantPreferenceContract.OwnerRole};
            DROP FUNCTION {signature};
            CREATE FUNCTION {function}(p_tenant uuid, p_user uuid, p_updated_at timestamp with time zone)
            RETURNS boolean LANGUAGE plpgsql {(image ? "STABLE" : "VOLATILE")}
            SECURITY DEFINER SET search_path = pg_catalog AS $body${originalBody}$body$;
            REVOKE ALL ON FUNCTION {signature} FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION {signature} TO {PostgresTenantPreferenceContract.GlobalRole};
            RESET ROLE;
            REVOKE CREATE ON SCHEMA {schema} FROM {PostgresTenantPreferenceContract.OwnerRole};
            """);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await PostgresTenantPreferenceContract.RequireReadyAsync(context, fixture.NativeQueueEnrollment, default));
    }

    [Test]
    [Arguments("executor")]
    [Arguments("runtime-via-login")]
    [Arguments("global-column")]
    [Arguments("runtime-column")]
    [Arguments("owner-column")]
    public async Task EffectivePrivilegeDriftFailsEntryAndInstallerWithoutRepair(string drift)
    {
        await using var context = Open();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var enrollment = fixture.NativeQueueEnrollment;
        var entity = context.Model.FindEntityType(typeof(User))!;
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = context.GetService<IDbContextOptions>().FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
            ?? entity.GetSchema() ?? "public";
        string table = sql.DelimitIdentifier(entity.GetTableName()!, schema);
        string property = drift == "owner-column" ? nameof(User.IsDeleted) : nameof(User.LastActiveTenantId);
        string column = sql.DelimitIdentifier(entity.FindProperty(property)!
            .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        string recipient = drift switch
        {
            "global-column" => enrollment.GlobalLogin,
            "owner-column" => PostgresTenantPreferenceContract.OwnerRole,
            _ => enrollment.RuntimeLogin
        };
        string parent = drift == "runtime-via-login" ? sql.DelimitIdentifier(enrollment.GlobalLogin) : PostgresTenantPreferenceContract.GlobalRole;
        string grant = drift is "executor" or "runtime-via-login"
            ? $"GRANT {parent} TO {sql.DelimitIdentifier(recipient)} WITH INHERIT {(drift == "executor" ? "TRUE" : "FALSE")}, SET {(drift == "executor" ? "FALSE" : "TRUE")}, ADMIN FALSE"
            : $"GRANT UPDATE ({column}) ON {table} TO {sql.DelimitIdentifier(recipient)}";
        await context.Database.ExecuteSqlRawAsync(grant);
        await transaction.CreateSavepointAsync("before_entry");
        await using (var entry = context.Database.GetDbConnection().CreateCommand())
        {
            entry.Transaction = transaction.GetDbTransaction();
            entry.CommandText = $"SELECT {PostgresTenantPreferenceContract.FunctionSql(context)}(@subject,@tenant,@now)";
            entry.Parameters.Add(new NpgsqlParameter("subject", Guid.CreateVersion7()));
            entry.Parameters.Add(new NpgsqlParameter("tenant", Guid.CreateVersion7()));
            entry.Parameters.Add(new NpgsqlParameter("now", DateTime.UtcNow));
            var denied = await Assert.ThrowsAsync<PostgresException>(async () => { await entry.ExecuteScalarAsync(); });
            await Assert.That(denied.SqlState).IsEqualTo("55000");
            await Assert.That(denied.MessageText is "native_privilege_guard_not_ready"
                or "native_tenant_preference_effective_privilege_drift").IsTrue();
        }
        await transaction.RollbackToSavepointAsync("before_entry");
        await transaction.CreateSavepointAsync("before_install");
        var install = await Assert.ThrowsAsync<PostgresException>(async () =>
            await PostgresTenantPreferenceContract.ApplyAsync(context, enrollment));
        await Assert.That(install.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("before_install");
        await using var remaining = context.Database.GetDbConnection().CreateCommand();
        remaining.Transaction = transaction.GetDbTransaction();
        remaining.CommandText = drift is "executor" or "runtime-via-login"
            ? "SELECT pg_catalog.pg_has_role(@role,@parent,'MEMBER')"
            : "SELECT pg_catalog.has_column_privilege(@role,@table,@column,'UPDATE')";
        remaining.Parameters.Add(new NpgsqlParameter("role", recipient));
        if (drift is "executor" or "runtime-via-login")
            remaining.Parameters.Add(new NpgsqlParameter("parent", drift == "executor"
                ? PostgresTenantPreferenceContract.GlobalRole : enrollment.GlobalLogin));
        else
        {
            remaining.Parameters.Add(new NpgsqlParameter("table", table));
            remaining.Parameters.Add(new NpgsqlParameter("column", entity.FindProperty(property)!
                .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!));
        }
        await Assert.That(await remaining.ExecuteScalarAsync()).IsEqualTo(true);
    }

    private ExploreDbContext Open()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        return new ExploreDbContext(options.Options);
    }
}
