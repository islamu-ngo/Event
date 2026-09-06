// ABOUTME: Persistence integration tests for LocationRoomRepository verifying CRUD and location-scoped queries.
// ABOUTME: Uses Testcontainers PostgreSQL with real schema via MigrateAsync and Respawn reset.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class LocationRoomRepositoryTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public LocationRoomRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task Create_ShouldPersistLocationRoom()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, location) = await SetupLocationAsync(context);
        var repository = new LocationRoomRepository(context);

        var room = new LocationRoom
        {
            LocationId = location.Id,
            Location = location,
            Name = "Main Hall",
            Slug = "main-hall",
            Capacity = 200,
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = tenant
        };

        // Act
        var result = await repository.Create(room);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);

        using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.LocationRooms.FindAsync(result.Id);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Name).IsEqualTo("Main Hall");
        await Assert.That(saved.Capacity).IsEqualTo(200);
    }

    [Test]
    public async Task GetByLocationAsync_ShouldReturnOrderedRooms()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, location) = await SetupLocationAsync(context);
        var repository = new LocationRoomRepository(context);

        context.LocationRooms.AddRange(
            new LocationRoom { LocationId = location.Id, Location = location, Name = "Room C", SortOrder = 3, TenantId = tenant.Id, Tenant = tenant },
            new LocationRoom { LocationId = location.Id, Location = location, Name = "Room A", SortOrder = 1, TenantId = tenant.Id, Tenant = tenant },
            new LocationRoom { LocationId = location.Id, Location = location, Name = "Room B", SortOrder = 2, TenantId = tenant.Id, Tenant = tenant }
        );
        await context.SaveChangesAsync();

        // Act
        var result = await repository.GetByLocationAsync(location.Id, CancellationToken.None);

        // Assert
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].SortOrder).IsEqualTo(1);
        await Assert.That(result[1].SortOrder).IsEqualTo(2);
        await Assert.That(result[2].SortOrder).IsEqualTo(3);
    }

    [Test]
    public async Task GetByLocationAsync_ShouldReturnEmpty_WhenNoRoomsExist()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (_, location) = await SetupLocationAsync(context);
        var repository = new LocationRoomRepository(context);

        // Act
        var result = await repository.GetByLocationAsync(location.Id, CancellationToken.None);

        // Assert
        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetByLocationAsync_ShouldNotReturnRoomsFromOtherLocations()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();
        var (tenant, location1) = await SetupLocationAsync(context);

        var location2 = new Location
        {
            FullName = "Other Venue",
            Country = "UK",
            City = "Manchester",
            TenantId = tenant.Id,
            Tenant = tenant
        };
        location2.SetManualAddress("456 Other St", "M1 2AB");
        context.Locations.Add(location2);
        await context.SaveChangesAsync();

        context.LocationRooms.Add(new LocationRoom { LocationId = location1.Id, Location = location1, Name = "Room X", SortOrder = 1, TenantId = tenant.Id, Tenant = tenant });
        context.LocationRooms.Add(new LocationRoom { LocationId = location2.Id, Location = location2, Name = "Room Y", SortOrder = 1, TenantId = tenant.Id, Tenant = tenant });
        await context.SaveChangesAsync();

        var repository = new LocationRoomRepository(context);

        // Act
        var result = await repository.GetByLocationAsync(location1.Id, CancellationToken.None);

        // Assert
        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Name).IsEqualTo("Room X");
    }

    private static async Task<(Tenant tenant, Location location)> SetupLocationAsync(ExploreDbContext context)
    {
        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Test Tenant",
            Slug = "test-tenant-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var location = new Location
        {
            FullName = "Test Conference Center",
            Country = "FR",
            City = "Paris",
            TenantId = tenant.Id,
            Tenant = tenant
        };
        location.SetManualAddress("123 Test Blvd", "75001");
        context.Locations.Add(location);
        await context.SaveChangesAsync();

        return (tenant, location);
    }
}
