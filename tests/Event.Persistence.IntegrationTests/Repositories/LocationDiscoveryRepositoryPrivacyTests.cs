// ABOUTME: PostgreSQL query-shape tests for tenant location validation used by anonymous discovery.
// ABOUTME: Proves exact LocationPii columns are never materialized by the area-validation read path.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
[Category("EventLocationPrivacy")]
public sealed class LocationDiscoveryRepositoryPrivacyTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task DiscoveryLocationIdValidationQueryDoesNotLoadLocationPii()
    {
        await fixture.ResetAsync();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Discovery privacy tenant",
            Slug = $"discovery-privacy-{Guid.NewGuid():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var foreignTenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Foreign discovery privacy tenant",
            Slug = $"foreign-discovery-privacy-{Guid.NewGuid():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            FullName = "Private exact venue",
            Country = "BE",
            City = "Brussels"
        };
        location.SetProviderAddress(
            "Exact private address",
            "1000",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(50.8466, 4.3528));
        var foreignLocation = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = foreignTenant.Id,
            Tenant = foreignTenant,
            FullName = "Foreign private exact venue",
            Country = "BE",
            City = "Antwerp"
        };
        foreignLocation.SetProviderAddress(
            "Foreign exact private address",
            "2000",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(51.2194, 4.4025));

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Locations.AddRange(location, foreignLocation);
            await seedContext.SaveChangesAsync();
        }

        var interceptor = new SelectCommandInterceptor();
        await using var context = CreateTenantContext(tenant.Id, interceptor);
        var repository = new LocationRepository(context);

        var locationIds = await repository.GetExistingTenantLocationIdsAsync(
            tenant.Id,
            [location.Id, foreignLocation.Id, Guid.Empty, location.Id],
            CancellationToken.None);

        await Assert.That(locationIds).IsEquivalentTo([location.Id]);
        await Assert.That(interceptor.SelectCommands).HasSingleItem();
        var sql = interceptor.SelectCommands.Single();
        await Assert.That(sql).Contains("locations");
        await Assert.That(sql).DoesNotContain("location_pii");
        await Assert.That(sql).DoesNotContain("address");
        await Assert.That(sql).DoesNotContain("postcode");
        await Assert.That(sql).DoesNotContain("latitude");
        await Assert.That(sql).DoesNotContain("longitude");
        await Assert.That(context.ChangeTracker.Entries<Location>()).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries<LocationPii>()).IsEmpty();
    }

    [Test]
    public async Task EmptyAndMalformedLocationIdsReturnWithoutDatabaseQuery()
    {
        var interceptor = new SelectCommandInterceptor();
        var tenantId = Guid.CreateVersion7();
        await using var context = CreateTenantContext(tenantId, interceptor);
        var repository = new LocationRepository(context);

        var locationIds = await repository.GetExistingTenantLocationIdsAsync(
            tenantId,
            [Guid.Empty, Guid.Empty],
            CancellationToken.None);

        await Assert.That(locationIds).IsEmpty();
        await Assert.That(interceptor.SelectCommands).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    public async Task DiscoveryLocationIdValidationHonorsCallerCancellation()
    {
        var tenantId = Guid.CreateVersion7();
        await using var context = CreateTenantContext(tenantId, new SelectCommandInterceptor());
        var repository = new LocationRepository(context);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repository.GetExistingTenantLocationIdsAsync(
                tenantId,
                [Guid.CreateVersion7()],
                cancellation.Token));
    }

    private ExploreDbContext CreateTenantContext(Guid tenantId, DbCommandInterceptor interceptor)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(interceptor)
            .Options;
        return new ExploreDbContext(options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class SelectCommandInterceptor : DbCommandInterceptor
    {
        public List<string> SelectCommands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                SelectCommands.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }
}
