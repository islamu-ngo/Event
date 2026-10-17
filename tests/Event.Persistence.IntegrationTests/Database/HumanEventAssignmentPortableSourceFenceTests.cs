using System.Data;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace Event.Persistence.IntegrationTests.Database;

[NotInParallel("AdmissionAuthorityProviderDb")]
[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
public sealed class HumanEventAssignmentPortableSourceFenceTests(AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task MissingAssignmentRangeRejectsInsertAndFinalReadSeesPersistedOwnInsert(PrimaryDatabaseProvider provider)
    {
        Guid tenant = Guid.CreateVersion7(), user = Guid.CreateVersion7(), eventId = Guid.CreateVersion7();
        bool sqlServer = provider == PrimaryDatabaseProvider.SqlServer;
        await using var setup = Open(provider, tenant);
        if (sqlServer)
            await setup.Database.ExecuteSqlRawAsync(
                "IF SCHEMA_ID(N'islamu_event') IS NULL EXEC(N'CREATE SCHEMA [islamu_event]')");
        Type[] types = [typeof(Explore.Domain.Event), typeof(EventRoleAssignment), typeof(EventProvenanceType),
            typeof(Actor), typeof(TenantUser), typeof(OrganizationMember), typeof(OrganizationTenant),
            typeof(GroupMember), typeof(GroupTenant), typeof(Role), typeof(RolePermission), typeof(Permission),
            typeof(SystemSetting), typeof(TenantSetting), typeof(UserExternalLogin),
            typeof(User), typeof(UserPii)];
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
        // Schema-only fixtures isolate physical locks. Full domain/FK/native admission uses PostgreSQL.
        var subject = await HumanCommitSubject.ResolveAsync(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "fixture")),
            new ResolveCurrentUserIdByIdentityRequestHandler(new ExternalLoginAuthenticationRepository(
                new GlobalExploreDbContextFactory(fixture.CreateOptions(provider, PrimaryDatabaseRole.Global)))), default)
            ?? throw new InvalidOperationException("A human subject is required.");
        await using var context = Open(provider, tenant);
        var source = new HumanEventAssignmentSourceFence(context, subject,
            PolicySourceSetPurpose.RuntimeTenant, tenant, [eventId]);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var held = await source.AcquireAsync(await sets.AcquireAsync());
        await using var writer = Open(provider, tenant);
        await writer.Database.OpenConnectionAsync();
        await writer.Database.ExecuteSqlRawAsync(sqlServer ? "SET LOCK_TIMEOUT 0" : "SET SESSION innodb_lock_wait_timeout = 1");
        string insert = $"INSERT INTO {Table(writer, typeof(EventRoleAssignment))} "
            + "(id,tenant_id,event_id,user_id,role_id,status,starts_at_utc,version) VALUES ({0},{1},{2},{3},{4},{5},{6},{7})";
        object[] values = [Guid.CreateVersion7(), tenant, eventId, user, 987654,
            1, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1L];
        await using (var attempt = await writer.Database.BeginTransactionAsync())
        {
            Exception? failure = null;
            try { await writer.Database.ExecuteSqlRawAsync(insert, values); }
            catch (Exception error) { failure = error; }
            await Assert.That(failure is SqlException { Number: 1222 } or MySqlException { Number: 1205 }).IsTrue();
            await attempt.RollbackAsync();
        }
        await context.Database.ExecuteSqlRawAsync(insert, values);
        await Assert.That((await held.ValidateAsync()).Assignments.Single().Id).IsEqualTo((Guid)values[0]);
        await Assert.That(held.Facts.Assignments.Length).IsEqualTo(0);
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
