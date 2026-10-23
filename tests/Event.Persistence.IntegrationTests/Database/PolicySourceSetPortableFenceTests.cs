using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel("AdmissionAuthorityProviderDb")]
[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
public sealed class PolicySourceSetPortableFenceTests(AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "role")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "system")]
    [Arguments(PrimaryDatabaseProvider.SqlServer, "tenant")]
    [Arguments(PrimaryDatabaseProvider.MySql, "role")]
    [Arguments(PrimaryDatabaseProvider.MySql, "system")]
    [Arguments(PrimaryDatabaseProvider.MySql, "tenant")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "role")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "system")]
    [Arguments(PrimaryDatabaseProvider.MariaDb, "tenant")]
    public async Task EmptyPermissionAndAbsentRouteInsertionsAreExcludedByNativeLocks(
        PrimaryDatabaseProvider provider, string source)
    {
        await using var setup = Open(provider);
        bool sqlServer = provider == PrimaryDatabaseProvider.SqlServer;
        if (sqlServer)
            await setup.Database.ExecuteSqlRawAsync(
                "IF SCHEMA_ID(N'islamu_event') IS NULL EXEC(N'CREATE SCHEMA [islamu_event]')");
        foreach (Type type in new[] { typeof(RolePermission), typeof(SystemSetting), typeof(TenantSetting) })
        {
            string table = Table(setup, type);
            string columns = type == typeof(RolePermission)
                ? "role_id int NOT NULL, permission_id int NOT NULL, PRIMARY KEY (role_id, permission_id)"
                : "setting_key varchar(200) NOT NULL, tenant_id varchar(36) NOT NULL, PRIMARY KEY (setting_key, tenant_id)";
            await setup.Database.ExecuteSqlRawAsync(sqlServer
                ? $"IF OBJECT_ID(N'{table}', N'U') IS NULL CREATE TABLE {table} ({columns})"
                : $"CREATE TABLE IF NOT EXISTS {table} ({columns}) ENGINE=InnoDB");
            await setup.Database.ExecuteSqlRawAsync($"DELETE FROM {table}");
        }
        await using var owner = Open(provider);
        var fence = new PolicySourceSetFence(owner, PolicySourceSetPurpose.Global);
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await fence.AcquireAsync();
        await using var writer = Open(provider);
        await writer.Database.OpenConnectionAsync();
        await writer.Database.ExecuteSqlRawAsync(sqlServer
            ? "SET LOCK_TIMEOUT 0" : "SET SESSION innodb_lock_wait_timeout = 1");
        bool route = source != "role";
        string insertion = route
            ? $"INSERT INTO {Table(writer, source == "tenant" ? typeof(TenantSetting) : typeof(SystemSetting))} "
                + "(setting_key, tenant_id) VALUES ({0}, 'absent')"
            : $"INSERT INTO {Table(writer, typeof(RolePermission))} (role_id, permission_id) VALUES (123, 456)";
        object[] parameters = route ? [GovernanceSettingKeys.Cerbos.Mode] : [];
        await using (var attempt = await writer.Database.BeginTransactionAsync())
        {
            Exception? failure = null;
            try { await writer.Database.ExecuteSqlRawAsync(insertion, parameters); }
            catch (Exception error) { failure = error; }
            await Assert.That(failure is SqlException { Number: 1222 }
                or MySqlException { Number: 1205 }).IsTrue();
            await attempt.RollbackAsync();
        }
        await lease.ValidateAsync();
        await transaction.RollbackAsync();
        await writer.Database.ExecuteSqlRawAsync(insertion, parameters);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
    }

    private ExploreDbContext Open(PrimaryDatabaseProvider provider)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.CreateOptions(provider));
        return new ExploreDbContext(options.Options);
    }

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
}

public sealed class PolicySourceSetSqliteFenceTests
{
    [Test]
    public async Task SqliteWriteReservationExcludesAbsentRouteAndRollbackReleasesIt()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        await context.RolePermissions.ExecuteDeleteAsync();
        await context.SystemSettings.Where(setting => setting.SettingKey == GovernanceSettingKeys.Cerbos.Mode)
            .ExecuteDeleteAsync();
        var fence = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        await context.Database.OpenConnectionAsync();
        await using var native = ((SqliteConnection)context.Database.GetDbConnection())
            .BeginTransaction(IsolationLevel.Serializable, deferred: true);
        await using var transaction = await context.Database.UseTransactionAsync(native)
            ?? throw new InvalidOperationException("The test requires the original borrowed transaction.");
        await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.DatabasePath,
            Pooling = false,
            DefaultTimeout = 1
        }.ToString());
        await writer.OpenAsync();
        // A deferred owner alone allows the independent writer. The primitive must change that.
        await Assert.That(SQLitePCL.raw.sqlite3_exec(writer.Handle, "BEGIN IMMEDIATE")).IsEqualTo(SQLitePCL.raw.SQLITE_OK);
        await Assert.That(SQLitePCL.raw.sqlite3_exec(writer.Handle, "ROLLBACK")).IsEqualTo(SQLitePCL.raw.SQLITE_OK);
        var lease = await fence.AcquireAsync();
        var entity = context.Model.FindEntityType(typeof(SystemSetting))!;
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!);
        await using var command = writer.CreateCommand();
        command.CommandText = $"INSERT INTO {table} "
            + "(id, setting_key, value, setting_value_type_id, is_locked, display_order, created_at) "
            + "VALUES (@id, @key, @value, 0, 0, 0, @created)";
        command.Parameters.AddWithValue("@id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("@key", GovernanceSettingKeys.Cerbos.Mode);
        command.Parameters.AddWithValue("@value", "\"custom_endpoint\"");
        command.Parameters.AddWithValue("@created", DateTime.UtcNow);
        var blocked = await Assert.ThrowsAsync<SqliteException>(async () => await command.ExecuteNonQueryAsync());
        await Assert.That(blocked!.SqliteErrorCode).IsEqualTo(5);
        await lease.ValidateAsync();
        await transaction.RollbackAsync();
        await command.ExecuteNonQueryAsync();
        await Assert.That(await context.SystemSettings.AnyAsync(
            setting => setting.SettingKey == GovernanceSettingKeys.Cerbos.Mode)).IsTrue();
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
    }
}
