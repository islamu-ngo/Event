using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel("AdmissionAuthorityProviderDb")]
[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
public sealed class HumanSupportAccessPortableSourceFenceTests(AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task MissingSessionInsertIsExcludedAndOwnPersistedSessionIsFresh(PrimaryDatabaseProvider provider)
    {
        // A trusted forwarding observation is input, not authority: this provider deliberately
        // has no matching session at acquisition. Only its actual persisted rows are facts.
        await using var ingress = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var session = await HumanSupportAccessSourceFenceTests.SeedAsync(ingress);
        var (subject, forwarding, _) = await HumanSupportAccessSourceFenceTests.CaptureAsync(ingress, session.Id.ToString());
        bool sqlServer = provider == PrimaryDatabaseProvider.SqlServer;
        await using var setup = Open(provider, ingress.TenantId);
        if (sqlServer)
            await setup.Database.ExecuteSqlRawAsync(
                "IF SCHEMA_ID(N'islamu_event') IS NULL EXEC(N'CREATE SCHEMA [islamu_event]')");
        Type[] types = [typeof(SupportAccessSession), typeof(SystemSetting), typeof(TenantSetting), typeof(User),
            typeof(UserExternalLogin), typeof(PlatformUserRole), typeof(TenantUser), typeof(TenantUserRoleGrant),
            typeof(OrganizationMember), typeof(GroupMember), typeof(Role), typeof(RolePermission), typeof(Permission),
            typeof(Tenant), typeof(TenantStatus), typeof(OrganizationTenant), typeof(Organization),
            typeof(GroupTenant), typeof(Group)];
        var helper = setup.GetService<ISqlGenerationHelper>();
        foreach (Type type in types)
        {
            var entity = setup.Model.FindEntityType(type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string columns = string.Join(",", entity.GetProperties().Where(property => !property.IsShadowProperty())
                .Select(property => $"{helper.DelimitIdentifier(property.GetColumnName(store)!)} "
                    + $"{property.GetRelationalTypeMapping().StoreType} NULL"));
            string table = Table(setup, type);
            await setup.Database.ExecuteSqlRawAsync(sqlServer
                ? $"IF OBJECT_ID(N'{table}', N'U') IS NULL CREATE TABLE {table} ({columns})"
                : $"CREATE TABLE IF NOT EXISTS {table} ({columns}) ENGINE=InnoDB");
        }
        await setup.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Table(setup, typeof(User))} (id,is_deleted) VALUES ({{0}},{{1}})", subject.PrincipalId, false);
        await using var context = Open(provider, ingress.TenantId);
        var source = new HumanSupportAccessSourceFence(context, subject, forwarding, PolicySourceSetPurpose.RuntimeTenant);
        var principal = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var setLease = await sets.AcquireAsync();
        var held = await source.AcquireAsync(setLease, await principal.AcquireAsync(setLease));
        await Assert.That(held.Facts.Sessions.Length).IsEqualTo(0);
        await using var writer = Open(provider, ingress.TenantId);
        await writer.Database.OpenConnectionAsync();
        await writer.Database.ExecuteSqlRawAsync(sqlServer ? "SET LOCK_TIMEOUT 0" : "SET SESSION innodb_lock_wait_timeout = 1");
        string insert = $"INSERT INTO {Table(writer, typeof(SupportAccessSession))} "
            + "(id,actor_user_id,target_tenant_id,status_id,mode_id,started_at_utc,expires_at_utc,reason_code,ticket_reference,concurrency_stamp) "
            + "VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9})";
        object[] values = [session.Id, subject.PrincipalId, ingress.TenantId, session.StatusId, session.ModeId,
            sqlServer ? session.StartedAtUtc : session.StartedAtUtc.UtcDateTime,
            sqlServer ? session.ExpiresAtUtc : session.ExpiresAtUtc.UtcDateTime,
            session.ReasonCode, session.TicketReference, Guid.CreateVersion7()];
        await using (var attempt = await writer.Database.BeginTransactionAsync())
        {
            Exception? failure = null;
            try { await writer.Database.ExecuteSqlRawAsync(insert, values); }
            catch (Exception error) { failure = error; }
            await Assert.That(failure is SqlException { Number: 1222 } or MySqlException { Number: 1205 }).IsTrue();
            await attempt.RollbackAsync();
        }
        await context.Database.ExecuteSqlRawAsync(insert, values);
        await Assert.That((await held.ValidateAsync()).Sessions.Single().Id).IsEqualTo(session.Id);
        await Assert.That(held.Facts.Sessions.Length).IsEqualTo(0);
        await owner.RollbackAsync();
    }

    private ExploreDbContext Open(PrimaryDatabaseProvider provider, Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.CreateOptions(provider));
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }
    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
