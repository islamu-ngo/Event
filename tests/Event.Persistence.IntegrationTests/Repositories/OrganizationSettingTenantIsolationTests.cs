// ABOUTME: Proves organization setting operations use tenant participation plus global organization identity.
// ABOUTME: Runs with tenant filters bypassed so explicit repository predicates are the tested authority.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class OrganizationSettingTenantIsolationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Guid TenantA = Id(201);
    private static readonly Guid TenantB = Id(202);
    private static readonly Guid OrganizationId = Id(203);

    [Test]
    public async Task PostgreSqlRepositoryFiltersSameGlobalOrganizationByExplicitTenantPair()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        await AssertPairIsolationAsync(context);
    }

    [Test]
    public async Task SqliteRepositoryFiltersSameGlobalOrganizationByExplicitTenantPair()
    {
        string path = Path.Combine(Path.GetTempPath(), "organization-setting-pair.db");
        DeleteSqliteFiles(path);
        try
        {
            await using ExploreDbContext context = CreateSqliteContext(path);
            await context.Database.EnsureCreatedAsync();
            context.EnableTenantFilterBypass("Test explicit organization setting tenant predicates.");
            await LookupTableSeeder.SeedAsync(context);
            await AssertPairIsolationAsync(context);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    [Test]
    public async Task RepositoryRejectsEmptyTenantOrOrganizationIdentity()
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new OrganizationSettingRepository(context);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAllForOrganization(
            Guid.Empty,
            OrganizationId));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAllForOrganization(
            TenantA,
            Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SetValueAsync(
            Guid.Empty,
            OrganizationId,
            "address_governance.organization_creation_grant",
            "true",
            Id(209)));
    }

    private static async Task AssertPairIsolationAsync(ExploreDbContext context)
    {
        TenantStatus active = await context.TenantStatuses.SingleAsync(
            status => status.Id == (int)TenantStatusEnum.Active);
        ApprovalStatus approved = await context.ApprovalStatuses.SingleAsync(
            status => status.Id == (int)ApprovalStatusEnum.Approved);
        Tenant tenantA = Tenant(TenantA, "organization-setting-a", active);
        Tenant tenantB = Tenant(TenantB, "organization-setting-b", active);
        var organization = new Organization
        {
            Id = OrganizationId,
            Pii = new OrganizationPii { FullName = "Synthetic shared organization" },
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Id(204)
        };
        OrganizationTenant participationA = Participation(Id(205), tenantA, organization, approved);
        OrganizationTenant participationB = Participation(Id(206), tenantB, organization, approved);
        OrganizationSetting settingA = Setting(Id(207), tenantA, participationA, "true");
        OrganizationSetting settingB = Setting(Id(208), tenantB, participationB, "false");
        context.AddRange(tenantA, tenantB, organization, participationA, participationB, settingA, settingB);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new OrganizationSettingRepository(context);
        List<OrganizationSetting> tenantAResults = await repository.GetAllForOrganization(
            TenantA,
            OrganizationId);
        List<OrganizationSetting> tenantBResults = await repository.GetAllForOrganization(
            TenantB,
            OrganizationId);
        OrganizationSetting? tenantAResult = await repository.GetByOrganizationAndKey(
            TenantA,
            OrganizationId,
            "address_governance.organization_creation_grant");

        await Assert.That(tenantAResults).HasSingleItem();
        await Assert.That(tenantAResults[0].TenantId).IsEqualTo(TenantA);
        await Assert.That(tenantAResults[0].Value).IsEqualTo("true");
        await Assert.That(tenantBResults).HasSingleItem();
        await Assert.That(tenantBResults[0].TenantId).IsEqualTo(TenantB);
        await Assert.That(tenantBResults[0].Value).IsEqualTo("false");
        await Assert.That(tenantAResult?.TenantId).IsEqualTo(TenantA);

        await repository.SetValueAsync(
            TenantA,
            OrganizationId,
            "address_governance.organization_creation_grant",
            "updated-a",
            Id(209));
        OrganizationSetting? updatedTenantA = await repository.GetByOrganizationAndKey(
            TenantA,
            OrganizationId,
            "address_governance.organization_creation_grant");
        OrganizationSetting? unchangedTenantB = await repository.GetByOrganizationAndKey(
            TenantB,
            OrganizationId,
            "address_governance.organization_creation_grant");
        await Assert.That(updatedTenantA?.Value).IsEqualTo("updated-a");
        await Assert.That(unchangedTenantB?.Value).IsEqualTo("false");

        await Assert.That(await repository.RemoveOverride(
            TenantA,
            OrganizationId,
            "address_governance.organization_creation_grant")).IsTrue();
        await Assert.That(await repository.GetByOrganizationAndKey(
            TenantA,
            OrganizationId,
            "address_governance.organization_creation_grant")).IsNull();
        await Assert.That((await repository.GetByOrganizationAndKey(
            TenantB,
            OrganizationId,
            "address_governance.organization_creation_grant"))?.Value).IsEqualTo("false");
    }

    private static Tenant Tenant(Guid id, string slug, TenantStatus status) => new()
    {
        Id = id,
        FullName = "Synthetic settings tenant",
        Slug = slug,
        TenantStatusId = status.Id,
        TenantStatus = status,
        CreatedAt = DateTime.UnixEpoch
    };

    private static OrganizationTenant Participation(
        Guid id,
        Tenant tenant,
        Organization organization,
        ApprovalStatus approved) => new()
    {
        Id = id,
        TenantId = tenant.Id,
        Tenant = tenant,
        OrganizationId = organization.Id,
        Organization = organization,
        ApprovalStatusId = approved.Id,
        ApprovalStatus = approved,
        CreatedAt = DateTime.UnixEpoch,
        ConcurrencyStamp = Id(220 + id.ToByteArray()[15])
    };

    private static OrganizationSetting Setting(
        Guid id,
        Tenant tenant,
        OrganizationTenant participation,
        string value) => new()
    {
        Id = id,
        TenantId = tenant.Id,
        Tenant = tenant,
        OrganizationTenantId = participation.Id,
        OrganizationTenant = participation,
        SettingKey = "address_governance.organization_creation_grant",
        Value = value,
        CreatedAt = DateTime.UnixEpoch
    };

    private static ExploreDbContext CreateSqliteContext(string path)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true
            }.ToString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private static void DeleteSqliteFiles(string path)
    {
        File.Delete(path);
        File.Delete(path + "-shm");
        File.Delete(path + "-wal");
    }

    private static Guid Id(int suffix) =>
        Guid.Parse($"019b0000-0001-7000-8000-{suffix:000000000000}");
}
