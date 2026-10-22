using System.Data;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class SupportAdministrationNativeContractTests
{
    [Test]
    public async Task CompleteMembershipProfileCoexistsWithSupportWriterButExtraColumnAndPartialProfileDoNot()
    {
        await using var migrator = MembershipMigrator();
        await migrator.Database.OpenConnectionAsync();
        await PostgresTenantMembershipMutationPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment);
        var globalFactory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using (var global = await globalFactory.CreateDbContextAsync())
        {
            await global.Database.OpenConnectionAsync();
            await PostgresTenantMembershipMutationPermissions.RequireCurrentAsync(global, fixture.NativeQueueEnrollment);
        }
        await SaveTenantAsync(rejected: false);

        var names = new HumanPrincipalSourceSql(migrator);
        var sql = migrator.GetService<ISqlGenerationHelper>();
        string table = names.Table(typeof(TenantUser));
        string runtime = sql.DelimitIdentifier(fixture.NativeQueueEnrollment.RuntimeLogin);
        string note = sql.DelimitIdentifier(names.ColumnName(typeof(TenantUser), nameof(TenantUser.ModerationNote)));
        string status = sql.DelimitIdentifier(names.ColumnName(typeof(TenantUser), nameof(TenantUser.StatusId)));
        await RejectWithoutRepairAsync($"GRANT SELECT ({note}) ON {table} TO {runtime}",
            $"REVOKE SELECT ({note}) ON {table} FROM {runtime}");
        await RejectWithoutRepairAsync($"REVOKE UPDATE ({status}) ON {table} FROM {runtime}",
            $"GRANT UPDATE ({status}) ON {table} TO {runtime}");

        async Task RejectWithoutRepairAsync(string attack, string restore)
        {
            await using (var change = MembershipCommand(migrator, attack))
                await change.ExecuteNonQueryAsync();
            try
            {
                string attacked = await MembershipAclImageAsync(migrator, table);
                await SaveTenantAsync(rejected: true);
                await Assert.That(await MembershipAclImageAsync(migrator, table)).IsEqualTo(attacked);
            }
            finally
            {
                await using var change = MembershipCommand(migrator, restore);
                await change.ExecuteNonQueryAsync(CancellationToken.None);
            }
            await SaveTenantAsync(rejected: false);
        }

        async Task SaveTenantAsync(bool rejected)
        {
            Guid id = Guid.CreateVersion7();
            await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var owner = MembershipMigrator();
                await using var humanGate = new HumanSecuritySourceGate(owner);
                await using var human = await humanGate.AcquireAsync();
                await using var discoveryGate = new EventDiscoveryEnrollmentGate(owner);
                await using var discovery = await discoveryGate.AcquireAsync();
                await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var connection = (NpgsqlConnection)owner.Database.GetDbConnection();
                int backend = connection.ProcessID;
                owner.Tenants.Add(new Tenant
                {
                    Id = id,
                    FullName = "Membership support coexistence",
                    Slug = $"membership-support-{id:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
                if (rejected)
                {
                    var failure = await Assert.ThrowsAsync<DbUpdateException>(() => owner.SaveChangesAsync());
                    await Assert.That(failure.InnerException is PostgresException).IsTrue();
                    var postgres = (PostgresException)failure.InnerException!;
                    await Assert.That(postgres.SqlState).IsEqualTo("55000");
                    await Assert.That(postgres.MessageText).IsEqualTo("support_administration_ambient_scalar_authority");
                }
                else
                {
                    await owner.SaveChangesAsync();
                    await Assert.That(await owner.Tenants.AsNoTracking().AnyAsync(row => row.Id == id)).IsTrue();
                }
                await Assert.That(owner.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(owner.Database.GetDbConnection()).IsSameReferenceAs(connection);
                await Assert.That(connection.ProcessID).IsEqualTo(backend);
                await transaction.RollbackAsync(CancellationToken.None);
            });
            await Assert.That(await migrator.Tenants.AsNoTracking().AnyAsync(row => row.Id == id)).IsFalse();
        }
    }

    private ExploreDbContext MembershipMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static NpgsqlCommand MembershipCommand(ExploreDbContext context, string sql) =>
        new(sql, (NpgsqlConnection)context.Database.GetDbConnection(),
            (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());

    private static async Task<string> MembershipAclImageAsync(ExploreDbContext context, string table)
    {
        await using var command = MembershipCommand(context, """
            SELECT jsonb_agg(jsonb_build_array(attnum,attacl::text) ORDER BY attnum)::text
            FROM pg_catalog.pg_attribute WHERE attrelid=pg_catalog.to_regclass(@table) AND attnum<>0
            """);
        command.Parameters.AddWithValue("table", table);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
