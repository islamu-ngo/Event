using System.Data;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
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
public sealed class HumanPrincipalPortableSourceFenceTests(AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task EmptyPrincipalSourceRangesExcludeInsertionUntilOriginalOwnerEnds(PrimaryDatabaseProvider provider)
    {
        await using var setup = Open(provider);
        bool sqlServer = provider == PrimaryDatabaseProvider.SqlServer;
        if (sqlServer)
            await setup.Database.ExecuteSqlRawAsync(
                "IF SCHEMA_ID(N'islamu_event') IS NULL EXEC(N'CREATE SCHEMA [islamu_event]')");
        // These tables exercise physical provider locks; full FK/RLS admission is covered by
        // the PostgreSQL fixture. No credentials or authority-returning mocks are introduced.
        Type[] types = [typeof(User), typeof(UserExternalLogin), typeof(PlatformUserRole), typeof(Role),
            typeof(Tenant), typeof(TenantStatus), typeof(TenantUser), typeof(TenantUserRoleGrant),
            typeof(OrganizationMember), typeof(OrganizationTenant), typeof(Organization),
            typeof(GroupMember), typeof(GroupTenant), typeof(Group), typeof(Permission),
            typeof(RolePermission), typeof(SystemSetting), typeof(TenantSetting)];
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
        var subject = await HumanCommitSubject.ResolveAsync(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())], "fixture")),
            new ResolveCurrentUserIdByIdentityRequestHandler(new ExternalLoginAuthenticationRepository(
                new GlobalExploreDbContextFactory(fixture.CreateOptions(provider, PrimaryDatabaseRole.Global)), null)),
            CancellationToken.None) ?? throw new InvalidOperationException("A human subject is required.");
        await using var context = Open(provider);
        var source = new HumanPrincipalSourceFence(context, subject, PolicySourceSetPurpose.Global);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.Global);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var held = await source.AcquireAsync(await sets.AcquireAsync());
        await using var writer = Open(provider);
        await writer.Database.OpenConnectionAsync();
        await writer.Database.ExecuteSqlRawAsync(sqlServer
            ? "SET LOCK_TIMEOUT 0" : "SET SESSION innodb_lock_wait_timeout = 1");
        string insert = $"INSERT INTO {Table(writer, typeof(User))} (id,is_deleted) VALUES ({{0}},{{1}})";
        await using (var attempt = await writer.Database.BeginTransactionAsync())
        {
            Exception? error = null;
            try { await writer.Database.ExecuteSqlRawAsync(insert, subject.PrincipalId, false); }
            catch (Exception failure) { error = failure; }
            await Assert.That(error is SqlException { Number: 1222 } or MySqlException { Number: 1205 }).IsTrue();
            await attempt.RollbackAsync();
        }
        await held.ValidateAsync();
        await owner.RollbackAsync();
        await writer.Database.ExecuteSqlRawAsync(insert, subject.PrincipalId, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
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
