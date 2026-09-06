// ABOUTME: Proves inventory hold transitions use provider metadata and aggregate-owned portable mutations.
// ABOUTME: Executes provider mapping plus contended expiry, consume, and release paths against SQLite.

using Event.Persistence.IntegrationTests.Database;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("PersistenceDb")]
public sealed class RegistrationInventoryHoldPortabilityTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("PostgreSql", "islamu_event.registration_inventory_holds", "islamu_event.registration_orders")]
    [Arguments("Sqlite", "\"ie_registration_inventory_holds\"", "\"ie_registration_orders\"")]
    [Arguments("SqlServer", "[islamu_event].[registration_inventory_holds]", "[islamu_event].[registration_orders]")]
    [Arguments("MariaDb", "`ie_registration_inventory_holds`", "`ie_registration_orders`")]
    [Arguments("MySql", "`ie_registration_inventory_holds`", "`ie_registration_orders`")]
    public async Task ProviderModel_UsesMappedDelimitedIdentifiers(
        string provider,
        string expectedHolds,
        string expectedOrders)
    {
        await using ExploreDbContext context = CreateProviderContext(provider);
        ISqlGenerationHelper sql = context.GetService<ISqlGenerationHelper>();
        IEntityType hold = context.Model.FindEntityType(typeof(RegistrationInventoryHold))!;
        IEntityType order = context.Model.FindEntityType(typeof(RegistrationOrder))!;
        string holds = sql.DelimitIdentifier(hold.GetTableName()!, hold.GetSchema());
        string orders = sql.DelimitIdentifier(order.GetTableName()!, order.GetSchema());

        await Assert.That(holds).IsEqualTo(expectedHolds);
        await Assert.That(orders).IsEqualTo(expectedOrders);
    }

    [Test]
    public async Task InventoryTransitions_UseNoProviderSpecificSqlCommand()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "Explore.Persistence",
            "Repositories",
            "RegistrationInventoryRepository.cs"));

        await Assert.That(source).DoesNotContain("BuildPostgreSqlHoldExpiryCommand");
        await Assert.That(source).DoesNotContain("ExecuteSqlRawAsync");
        await Assert.That(source).DoesNotContain("UPDATE registration_inventory_holds");
    }

    [Test]
    public async Task FileBackedSqlite_ConcurrentExpiryHasOneWinnerAndReleasesCapacity()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"registration-hold-expiry-{Guid.NewGuid():N}.db");
        try
        {
            (Guid tenantId, Guid orderId, Guid holdId, Guid poolId) = await SeedSqliteAsync(databasePath);

            bool[] results = await Task.WhenAll(
                ExpireAsync(databasePath, holdId),
                ExpireAsync(databasePath, holdId));

            await Assert.That(results.Count(result => result)).IsEqualTo(1);
            await using ExploreDbContext verification = CreateSqliteContext(databasePath, tenantId);
            RegistrationInventoryHold persistedHold = await verification.RegistrationInventoryHolds
                .AsNoTracking()
                .SingleAsync(hold => hold.Id == holdId);
            RegistrationOrder persistedOrder = await verification.RegistrationOrders
                .AsNoTracking()
                .SingleAsync(order => order.Id == orderId);
            var repository = new RegistrationInventoryRepository(verification);

            await Assert.That(persistedHold.RegistrationInventoryHoldStatusId)
                .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Expired);
            await Assert.That(persistedHold.ReleasedAt).IsEqualTo(UtcNow);
            await Assert.That(persistedOrder.RegistrationOrderStatusId)
                .IsEqualTo((int)RegistrationOrderStatusEnum.NeedsReconciliation);
            await Assert.That(await repository.GetAllocatedQuantityAsync(poolId, tenantId, CancellationToken.None))
                .IsEqualTo(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task FileBackedSqlite_ConcurrentConsumeHasOneWinnerAndPreservesAllocation()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"registration-hold-consume-{Guid.NewGuid():N}.db");
        try
        {
            (Guid tenantId, _, Guid holdId, Guid poolId) = await SeedSqliteAsync(
                databasePath,
                UtcNow.AddMinutes(5));

            bool[] results = await Task.WhenAll(
                ConsumeAsync(databasePath, holdId),
                ConsumeAsync(databasePath, holdId));

            await Assert.That(results.Count(result => result)).IsEqualTo(1);
            await using ExploreDbContext verification = CreateSqliteContext(databasePath, tenantId);
            RegistrationInventoryHold persisted = await verification.RegistrationInventoryHolds
                .AsNoTracking()
                .SingleAsync(hold => hold.Id == holdId);
            var repository = new RegistrationInventoryRepository(verification);
            await Assert.That(persisted.RegistrationInventoryHoldStatusId)
                .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Consumed);
            await Assert.That(persisted.ConsumedAt).IsEqualTo(UtcNow);
            await Assert.That(await repository.GetAllocatedQuantityAsync(poolId, tenantId, CancellationToken.None))
                .IsEqualTo(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [Test]
    public async Task FileBackedSqlite_ConcurrentReleaseHasOneWinnerAndReleasesAllocation()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"registration-hold-release-{Guid.NewGuid():N}.db");
        try
        {
            (Guid tenantId, _, Guid holdId, Guid poolId) = await SeedSqliteAsync(
                databasePath,
                UtcNow.AddMinutes(5));

            bool[] results = await Task.WhenAll(
                ReleaseAsync(databasePath, holdId),
                ReleaseAsync(databasePath, holdId));

            await Assert.That(results.Count(result => result)).IsEqualTo(1);
            await using ExploreDbContext verification = CreateSqliteContext(databasePath, tenantId);
            RegistrationInventoryHold persisted = await verification.RegistrationInventoryHolds
                .AsNoTracking()
                .SingleAsync(hold => hold.Id == holdId);
            var repository = new RegistrationInventoryRepository(verification);
            await Assert.That(persisted.RegistrationInventoryHoldStatusId)
                .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Released);
            await Assert.That(persisted.ReleasedAt).IsEqualTo(UtcNow);
            await Assert.That(await repository.GetAllocatedQuantityAsync(poolId, tenantId, CancellationToken.None))
                .IsEqualTo(0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private static async Task<bool> ExpireAsync(string databasePath, Guid holdId)
    {
        await using ExploreDbContext context = CreateSqliteContext(databasePath, tenantId: null);
        var repository = new RegistrationInventoryRepository(context);
        return await repository.TryExpireDueHoldAsync(holdId, UtcNow, CancellationToken.None);
    }

    private static async Task<bool> ConsumeAsync(string databasePath, Guid holdId)
    {
        await using ExploreDbContext context = CreateSqliteContext(databasePath, tenantId: null);
        return await new RegistrationInventoryRepository(context)
            .TryConsumeActiveHoldAsync(holdId, UtcNow, CancellationToken.None);
    }

    private static async Task<bool> ReleaseAsync(string databasePath, Guid holdId)
    {
        await using ExploreDbContext context = CreateSqliteContext(databasePath, tenantId: null);
        return await new RegistrationInventoryRepository(context)
            .TryReleaseActiveHoldAsync(
                holdId,
                RegistrationInventoryHoldStatusEnum.Released,
                UtcNow,
                CancellationToken.None);
    }

    private static async Task<(Guid TenantId, Guid OrderId, Guid HoldId, Guid PoolId)> SeedSqliteAsync(
        string databasePath,
        DateTime? expiresAt = null)
    {
        await using ExploreDbContext context = CreateSqliteContext(databasePath, tenantId: null);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await LookupTableSeeder.SeedAsync(context);
        TenantStatus activeStatus = await context.TenantStatuses
            .SingleAsync(status => status.Id == (int)TenantStatusEnum.Active);

        var tenant = new Tenant
        {
            FullName = "SQLite inventory expiry tenant",
            Slug = $"sqlite-inventory-expiry-{Guid.NewGuid():N}",
            TenantStatusId = activeStatus.Id,
            TenantStatus = activeStatus,
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"sqlite-inventory-expiry-{Guid.NewGuid():N}@example.test",
                FirstName = "SQLite",
                LastName = "Expiry",
            },
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "SQLite Inventory Expiry Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        Guid eventId = Guid.CreateVersion7();
        var eventTarget = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = eventId,
            Title = "SQLite inventory expiry event",
            Subtitle = string.Empty,
            Description = string.Empty,
            FirstSessionDate = DateOnly.FromDateTime(UtcNow.AddDays(1)),
            LastSessionDate = DateOnly.FromDateTime(UtcNow.AddDays(1)),
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
        };
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenant.Id, eventId, "USD", 1);
        EventCapacityPool pool = EventCapacityPool.Create(
            tenant.Id,
            eventId,
            "SQLite capacity",
            maximumQuantity: 1,
            holdDurationSeconds: 900,
            CapacityHoldPolicyEnum.TimedHoldOnSelection,
            CapacityOversellPolicyEnum.Disallow,
            isActive: true);
        EventTicketType ticket = EventTicketType.Create(
            Guid.CreateVersion7(),
            tenant.Id,
            catalog.Id,
            "General",
            catalog.CurrencyCode,
            TicketPricingModeEnum.Free,
            fixedPrice: null,
            minimumPrice: null,
            suggestedPrice: null,
            ParticipantDataCollectionModeEnum.None,
            pool.Id,
            minimumAge: null,
            maximumAge: null,
            requiresGuardian: false,
            requiresApproval: false,
            perOrderLimit: null,
            perAccountLimit: null,
            perVerifiedContactLimit: null,
            perBookingPartyLimit: null);
        catalog.AddTicketType(ticket, pool);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenant.Id, eventId, 1));
        catalog.Publish();
        context.AddRange(eventTarget, catalog, pool);
        await context.SaveChangesAsync();

        Guid orderId = Guid.CreateVersion7();
        Guid holdId = Guid.CreateVersion7();
        RegistrationOrder order = RegistrationOrder.Create(
            orderId,
            tenant.Id,
            eventId,
            user.Id,
            purchaserActorId: null,
            BookingPartyTypeEnum.Individual,
            catalog.Id,
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(),
                participationHandlingModeId: 4,
                advanceRegistrationObligationId: 3,
                identityAccessModeId: 2,
                GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            registrationWorkflowVersionId: null,
            guestAccessTokenHash: null,
            catalog.CurrencyCode,
            UtcNow.AddMinutes(-20),
            UtcNow.AddMinutes(-5));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, UtcNow.AddMinutes(-20));
        RegistrationInventoryHold hold = RegistrationInventoryHold.Create(
            holdId,
            order.Id,
            pool.Id,
            ticket.Id,
            tenant.Id,
            quantity: 1,
            UtcNow.AddMinutes(-20),
            expiresAt ?? UtcNow.AddMinutes(-5));
        context.AddRange(order, hold);
        await context.SaveChangesAsync();

        return (tenant.Id, order.Id, hold.Id, pool.Id);
    }

    private static ExploreDbContext CreateSqliteContext(string databasePath, Guid? tenantId)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options)
        {
            TenantContext = tenantId.HasValue ? new TestTenantContext(tenantId.Value) : null,
        };
    }

    private static ExploreDbContext CreateProviderContext(string provider)
        => ExploreDbContextModelProviderTests.CreateContext(provider);

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
