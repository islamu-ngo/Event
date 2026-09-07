namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using TUnit.Core;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationManifestExportTenantRepositoryTests(
    PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ActiveExportReadBypassesAmbientTenantAndStopsAtRequestedMaximum()
    {
        await fixture.ResetAsync();
        Tenant[] active =
        [
            Tenant("export-d", TenantStatusEnum.Active),
            Tenant("export-b", TenantStatusEnum.Active),
            Tenant("export-a", TenantStatusEnum.Active),
            Tenant("export-c", TenantStatusEnum.Active)
        ];
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Tenants.AddRange(active);
            seed.Tenants.Add(Tenant("export-inactive", TenantStatusEnum.Suspended));
            await seed.SaveChangesAsync();
        }

        await using var context = fixture.CreateTenantFilteredDbContext(
            new TestTenantContext(active[3].Id));
        var repository = new TenantRepository(context);

        IReadOnlyList<Tenant> result =
            await repository.GetAllActiveForConfigurationManifestExportAsync(
                3,
                CancellationToken.None);

        await Assert.That(result.Select(tenant => tenant.Slug).ToArray())
            .IsEquivalentTo(["export-a", "export-b", "export-c"]);
        await Assert.That(result.Count).IsEqualTo(3);
    }

    private static Tenant Tenant(string slug, TenantStatusEnum status) => new()
    {
        FullName = slug,
        Slug = slug,
        TenantStatusId = (int)status,
        TenantStatus = null!
    };

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
