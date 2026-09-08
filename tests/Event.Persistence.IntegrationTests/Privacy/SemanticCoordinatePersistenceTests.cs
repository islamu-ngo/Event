using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<RecipientDeliveryMigrationContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RecipientDeliveryMigrationDb")]
[Category("EventLocationPrivacy")]
public sealed class SemanticCoordinatePersistenceTests(RecipientDeliveryMigrationContainerFixture fixture)
{
    private const string CoordinateCheck = "ck_location_pii_coordinate_shape";

    [Test]
    public async Task ExactAndAbsentCoordinates_RoundTripThroughLocationPiiWithTenantVisibility()
    {
        await PrepareDatabaseAsync();
        Tenant tenantA = CreateTenant("coordinate-a");
        Tenant tenantB = CreateTenant("coordinate-b");
        Location exact = CreateLocation(tenantA.Id, "Exact coordinate");
        Location absent = CreateLocation(tenantA.Id, "Absent coordinate");
        Location otherTenant = CreateLocation(tenantB.Id, "Other coordinate");
        exact.SetProviderAddress("Exact address", "1000", GeoCoordinate.Create(50.84673, 4.35247));
        absent.SetManualAddress("Absent address", "2000");
        otherTenant.SetProviderAddress("Other address", "3000", GeoCoordinate.Create(51.21945, 4.40246));

        await using (ExploreDbContext seed = CreateSystemContext())
        {
            seed.AddRange(tenantA, tenantB, exact, absent, otherTenant);
            await seed.SaveChangesAsync();
        }

        await using (ExploreDbContext context = TenantContext(tenantA.Id))
        {
            Location[] visible = await context.Locations.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
            Location exactRoundTrip = visible.Single(x => x.Id == exact.Id);
            Location absentRoundTrip = visible.Single(x => x.Id == absent.Id);
            GeoCoordinate coordinate = exactRoundTrip.GetCoordinate()!;
            await Assert.That(visible.Select(x => x.Id)).IsEquivalentTo([exact.Id, absent.Id]);
            await Assert.That(coordinate.Latitude).IsEqualTo(50.84673);
            await Assert.That(coordinate.Longitude).IsEqualTo(4.35247);
            await Assert.That(exactRoundTrip.Pii!.Latitude).IsEqualTo(50.84673);
            await Assert.That(exactRoundTrip.Pii.Longitude).IsEqualTo(4.35247);
            await Assert.That(absentRoundTrip.GetCoordinate()).IsNull();
            await Assert.That(absentRoundTrip.Pii!.Latitude).IsNull();
            await Assert.That(absentRoundTrip.Pii.Longitude).IsNull();
            await Assert.That(await context.Set<LocationPii>().CountAsync()).IsEqualTo(2);
        }

        await using (ExploreDbContext context = TenantContext(tenantB.Id))
        {
            Location visible = await context.Locations.AsNoTracking().SingleAsync();
            await Assert.That(visible.Id).IsEqualTo(otherTenant.Id);
            await Assert.That(visible.GetCoordinate()!.Latitude).IsEqualTo(51.21945);
            await Assert.That(await context.Set<LocationPii>().CountAsync()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task PrivateHomeErasure_DeletesOnlyTargetLocationPiiIncludingCoordinates()
    {
        await PrepareDatabaseAsync();
        Tenant tenant = CreateTenant("coordinate-erasure");
        User owner = CreateUser();
        Location target = CreatePrivateHome(tenant.Id, owner.Id, "Target home", GeoCoordinate.Create(50.8503, 4.3517));
        Location retained = CreatePrivateHome(tenant.Id, owner.Id, "Retained home", GeoCoordinate.Create(51.0543, 3.7174));
        await using (ExploreDbContext seed = CreateSystemContext())
        {
            seed.AddRange(tenant, owner, target, retained);
            await seed.SaveChangesAsync();
        }

        await using (ExploreDbContext context = TenantContext(tenant.Id))
        {
            Location loaded = await context.Locations.SingleAsync(x => x.Id == target.Id);
            loaded.EraseOwnedPii(
                new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc),
                LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
            await context.SaveChangesAsync();
        }

        await using (ExploreDbContext context = TenantContext(tenant.Id))
        {
            Location erased = await context.Locations.AsNoTracking().SingleAsync(x => x.Id == target.Id);
            Location untouched = await context.Locations.AsNoTracking().SingleAsync(x => x.Id == retained.Id);
            await Assert.That(erased.Pii).IsNull();
            await Assert.That(erased.GetCoordinate()).IsNull();
            await Assert.That(erased.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Erased);
            await Assert.That(untouched.Pii).IsNotNull();
            await Assert.That(untouched.GetCoordinate()!.Latitude).IsEqualTo(51.0543);
            await Assert.That(await context.Set<LocationPii>().AnyAsync(x => x.LocationId == target.Id)).IsFalse();
            await Assert.That(await context.Set<LocationPii>().AnyAsync(x => x.LocationId == retained.Id)).IsTrue();
        }
    }

    [Test]
    public async Task RelationalMetadata_KeepsCoordinatesOnlyInScalarLocationPiiColumns()
    {
        await PrepareDatabaseAsync();
        await using ExploreDbContext context = TenantContext(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        IEntityType locationType = context.Model.FindEntityType(typeof(Location))!;
        IEntityType piiType = context.Model.FindEntityType(typeof(LocationPii))!;
        IProperty latitude = piiType.FindProperty(nameof(LocationPii.Latitude))!;
        IProperty longitude = piiType.FindProperty(nameof(LocationPii.Longitude))!;
        IProperty[] coordinateProperties = context.Model.GetEntityTypes().SelectMany(x => x.GetProperties())
            .Where(x => x.Name is nameof(LocationPii.Latitude) or nameof(LocationPii.Longitude)).ToArray();

        await Assert.That(locationType.FindProperty(nameof(LocationPii.Latitude))).IsNull();
        await Assert.That(locationType.FindProperty(nameof(LocationPii.Longitude))).IsNull();
        await Assert.That(piiType.GetTableName()).IsEqualTo("location_pii");
        await Assert.That(piiType.IsOwned()).IsFalse();
        await Assert.That(context.Model.FindEntityType(typeof(GeoCoordinate))).IsNull();
        await Assert.That(context.Model.GetEntityTypes().SelectMany(x => x.GetComplexProperties())
            .Any(x => x.ClrType == typeof(GeoCoordinate))).IsFalse();
        await Assert.That(coordinateProperties.Length).IsEqualTo(2);
        await Assert.That(coordinateProperties.All(x => x.DeclaringType.ClrType == typeof(LocationPii))).IsTrue();
        await Assert.That(latitude.ClrType).IsEqualTo(typeof(double?));
        await Assert.That(longitude.ClrType).IsEqualTo(typeof(double?));
        await Assert.That(latitude.IsShadowProperty()).IsFalse();
        await Assert.That(longitude.IsShadowProperty()).IsFalse();
        await Assert.That(latitude.GetRelationalTypeMapping().StoreType).IsEqualTo("double precision");
        await Assert.That(longitude.GetRelationalTypeMapping().StoreType).IsEqualTo("double precision");

        await using var connection = new NpgsqlConnection(CreateComposedConnectionString(PrimaryDatabaseRole.Runtime));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name || '.' || column_name || ':' || data_type
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND column_name IN ('latitude', 'longitude')
              AND table_name IN ('locations', 'location_pii')
            ORDER BY table_name, column_name
            """, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        await Assert.That(columns).IsEquivalentTo(
        [
            "location_pii.latitude:double precision",
            "location_pii.longitude:double precision"
        ]);
    }

    [Test]
    public async Task DirectSql_RejectsEveryInvalidCoordinateShapeWithNamedCheckViolation()
    {
        await PrepareDatabaseAsync();
        Tenant tenant = CreateTenant("coordinate-constraint");
        InvalidCoordinate[] invalidCoordinates =
        [
            new(null, 4.0, "partial-latitude"),
            new(50.0, null, "partial-longitude"),
            new(90.000001, 4.0, "latitude-high"),
            new(-90.000001, 4.0, "latitude-low"),
            new(50.0, 180.000001, "longitude-high"),
            new(50.0, -180.000001, "longitude-low"),
            new(double.NaN, 4.0, "latitude-nan"),
            new(50.0, double.NaN, "longitude-nan"),
            new(double.PositiveInfinity, 4.0, "latitude-infinity"),
            new(50.0, double.NegativeInfinity, "longitude-infinity")
        ];
        Location[] locations = invalidCoordinates.Select(x => CreateLocation(tenant.Id, x.Case)).ToArray();
        await using (ExploreDbContext seed = CreateSystemContext())
        {
            seed.Add(tenant);
            seed.AddRange(locations);
            await seed.SaveChangesAsync();
        }

        for (var index = 0; index < invalidCoordinates.Length; index++)
        {
            InvalidCoordinate invalid = invalidCoordinates[index];
            await using var connection = new NpgsqlConnection(CreateComposedConnectionString(PrimaryDatabaseRole.Runtime));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO location_pii (location_id, address, postcode, latitude, longitude,
                                         address_substring_key, address_substring_key_version)
                VALUES (@location_id, @address, @postcode, @latitude, @longitude, 'INVARIANT-BREAKER', 2)
                """, connection);
            command.Parameters.AddWithValue("location_id", NpgsqlDbType.Uuid, locations[index].Id);
            command.Parameters.AddWithValue("address", NpgsqlDbType.Text, "invariant-breaker");
            command.Parameters.AddWithValue("postcode", NpgsqlDbType.Text, "0000");
            command.Parameters.Add("latitude", NpgsqlDbType.Double).Value = invalid.Latitude ?? (object)DBNull.Value;
            command.Parameters.Add("longitude", NpgsqlDbType.Double).Value = invalid.Longitude ?? (object)DBNull.Value;
            PostgresException? exception = await Assert.That(async () => await command.ExecuteNonQueryAsync())
                .Throws<PostgresException>();
            await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
            await Assert.That(exception.ConstraintName).IsEqualTo(CoordinateCheck);
        }
    }

    [Test]
    public async Task FailedLocationPiiWrite_WithSensitiveLoggingDisabled_EmitsZeroPiiDiagnostics()
    {
        await PrepareDatabaseAsync();
        const string sentinelAddress = "PII-ADDRESS-Q7V4";
        const string sentinelPostcode = "PII-POSTCODE-X9K2";
        const string sentinelCoordinate = "50.123456789";
        Guid tenantId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        Guid locationId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        Tenant tenant = CreateTenant("coordinate-diagnostics", tenantId);
        Location location = CreateLocation(tenant.Id, "Diagnostic location", locationId);
        await using (ExploreDbContext seed = CreateSystemContext())
        {
            seed.AddRange(tenant, location);
            await seed.SaveChangesAsync();
        }

        var diagnostics = new CompleteDiagnostics();
        DbContextOptionsBuilder<ExploreDbContext> options = CreateContextOptions(PrimaryDatabaseRole.Runtime);
        options.EnableSensitiveDataLogging(false).LogTo(diagnostics.Capture, LogLevel.Information);
        await using var context = new ExploreDbContext(options.Options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
        Location loaded = await context.Locations.SingleAsync(x => x.Id == locationId);
        loaded.SetProviderAddress(
            sentinelAddress,
            sentinelPostcode,
            GeoCoordinate.Create(50.123456789, 4));
        // Bypass the aggregate only to exercise the database's malformed-write diagnostic boundary.
        context.Entry(loaded.Pii!).Property(pii => pii.Address).CurrentValue =
            $"{sentinelAddress}{new string('x', 500)}";
        DbUpdateException? exception = await Assert.That(async () => await context.SaveChangesAsync())
            .Throws<DbUpdateException>();
        string[] forbidden =
        [
            sentinelAddress, sentinelPostcode, sentinelCoordinate, tenantId.ToString(), locationId.ToString()
        ];
        foreach (string sentinel in forbidden)
        {
            await Assert.That(diagnostics.Text).DoesNotContain(sentinel);
            await Assert.That(exception!.ToString()).DoesNotContain(sentinel);
        }
    }

    private async Task PrepareDatabaseAsync()
    {
        await fixture.ResetAsync();
        await using (ExploreDbContext migration = CreateMigrationContext())
        {
            await migration.Database.MigrateAsync();
        }

        await using ExploreDbContext seed = CreateSystemContext();
        await LookupTableSeeder.SeedAsync(seed);
    }

    private ExploreDbContext CreateMigrationContext()
    {
        DbContextOptionsBuilder<ExploreDbContext> options = CreateContextOptions(PrimaryDatabaseRole.Migrator);
        options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        return new ExploreDbContext(options.Options);
    }

    private ExploreDbContext CreateSystemContext()
    {
        var context = new ExploreDbContext(CreateContextOptions(PrimaryDatabaseRole.Runtime).Options);
        context.EnableTenantFilterBypass("Semantic coordinate persistence integration test system context.");
        return context;
    }

    private ExploreDbContext TenantContext(Guid tenantId) => new(
        CreateContextOptions(PrimaryDatabaseRole.Runtime).Options)
    {
        TenantContext = new TestTenantContext(tenantId)
    };

    private DbContextOptionsBuilder<ExploreDbContext> CreateContextOptions(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, CreateDatabaseOptions(role));
        return options;
    }

    private string CreateComposedConnectionString(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        return PrimaryDatabaseProviderComposition.ConfigureApplication(options, CreateDatabaseOptions(role))
            .ConnectionString;
    }

    private PrimaryDatabaseConnectionOptions CreateDatabaseOptions(PrimaryDatabaseRole role)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        return new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Username = connection.Username,
            Password = connection.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
    }

    private static Tenant CreateTenant(string slug, Guid? id = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        FullName = slug,
        Slug = $"{slug}-{(id ?? Guid.CreateVersion7()):N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static User CreateUser() => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new UserPii
        {
            Email = $"coordinate-owner-{Guid.CreateVersion7():N}@example.invalid",
            FirstName = "Coordinate",
            LastName = "Owner"
        },
        EmailVerified = true,
        ConcurrencyStamp = Guid.CreateVersion7(),
        CreatedAt = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc)
    };

    private static Location CreateLocation(Guid tenantId, string name, Guid? id = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        FullName = name,
        Country = "BE",
        City = "Brussels",
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static Location CreatePrivateHome(Guid tenantId, Guid ownerId, string name, GeoCoordinate coordinate)
    {
        Location location = CreateLocation(tenantId, name);
        location.ClassifyAsPrivateHome(ownerId);
        location.SetProviderAddress($"{name} address", "1000", coordinate);
        return location;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
    private sealed record InvalidCoordinate(double? Latitude, double? Longitude, string Case);

    private sealed class CompleteDiagnostics
    {
        private readonly object _gate = new();
        private readonly List<string> _entries = [];
        public string Text
        {
            get
            {
                lock (_gate)
                {
                    return string.Join('\n', _entries);
                }
            }
        }

        public void Capture(string message)
        {
            lock (_gate)
            {
                _entries.Add(message);
            }
        }
    }
}
