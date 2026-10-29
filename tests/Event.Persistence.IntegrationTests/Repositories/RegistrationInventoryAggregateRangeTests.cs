using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Database;
using Microsoft.Extensions.Configuration;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationInventoryAggregateRangeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public Task NativePostgresTotalsExceedIndividualQuantityRangeWithoutLosingScope() =>
        InventoryAggregateRangeScenario.AssertAsync(() => fixture.CreateDbContext());
}

[ClassDataSource<EventResourceFileUploadTests.Database>]
[NotInParallel("PersistenceDb")]
public sealed class SqliteRegistrationInventoryAggregateRangeTests(EventResourceFileUploadTests.Database database)
{
    [Test]
    public Task NativeSqliteTotalsExceedIndividualQuantityRangeWithoutLosingScope() =>
        InventoryAggregateRangeScenario.AssertAsync(() => database.CreateContext());
}

[RequiresStructuredPrimaryDatabase]
[NotInParallel("PersistenceDb")]
public sealed class SelectedProviderRegistrationInventoryAggregateRangeTests
{
    [Test]
    public async Task ActualSelectedProviderPreservesWideInventoryAndTicketLimitTotals()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var provider = PrimaryDatabaseProviderBehaviorFixture.Create(
            PrimaryDatabaseConfiguration.BindMigrator(configuration));
        await provider.PrepareAsync();
        await InventoryAggregateRangeScenario.AssertAsync(() => provider.CreateSystemContext());
    }
}

internal static class InventoryAggregateRangeScenario
{
    public static async Task AssertAsync(Func<ExploreDbContext> create)
    {
        DateTime now = DateTime.UtcNow;
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid accountId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        string email = $"range-{accountId:N}@example.test";
        Guid poolId;
        Guid ticketId;
        await using (var seed = create())
        {
            var tenant = new Tenant
            {
                Id = tenantId,
                FullName = "Inventory range",
                Slug = $"inventory-range-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            var account = new User
            {
                Id = accountId,
                Pii = new UserPii { Email = email, FirstName = "Range", LastName = "Owner" }
            };
            var actor = new Actor
            {
                Id = actorId,
                UserId = accountId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Range owner" }
            };
            var target = new DomainEvent(EventStatusEnum.Draft)
            {
                Id = eventId,
                TenantId = tenantId,
                Tenant = tenant,
                ActorId = actorId,
                Actor = actor,
                OrganizerActorId = actorId,
                Title = "Inventory range",
                Subtitle = string.Empty,
                Description = string.Empty,
                FirstSessionDate = DateOnly.FromDateTime(now.AddDays(1)),
                LastSessionDate = DateOnly.FromDateTime(now.AddDays(1)),
                EventTypeId = 1,
                EventFormatId = 1,
                EventFormat = null!,
                AudienceGenderId = 1,
                AudienceAgeId = 1,
                VisibilityTypeId = 1,
                VisibilityType = null!,
                EventStatus = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated
            };
            var catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
            var pool = EventCapacityPool.Create(tenantId, eventId, "Unbounded pool", null, 900,
                CapacityHoldPolicyEnum.TimedHoldOnSelection, CapacityOversellPolicyEnum.Disallow, true);
            var ticket = EventTicketType.Create(Guid.CreateVersion7(), tenantId, catalog.Id, "Range ticket",
                "USD", TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None,
                pool.Id, null, null, false, false, null, null, null, null);
            catalog.AddTicketType(ticket, pool);
            catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenantId, eventId, 1));
            catalog.Publish();
            seed.AddRange(tenant, account, actor, target, catalog, pool);
            await seed.SaveChangesAsync();
            poolId = pool.Id;
            ticketId = ticket.Id;
            for (int index = 0; index < 2; index++)
            {
                var order = RegistrationOrder.Create(tenantId, eventId, accountId, actorId,
                    BookingPartyTypeEnum.Individual, catalog.Id,
                    RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, null, null),
                    null, null, "USD", now, now.AddHours(1));
                order.AddLine(RegistrationOrderLine.Create(
                    Guid.CreateVersion7(), catalog, ticket, order.Id, int.MaxValue, null, null));
                order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
                order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
                order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, now);
                seed.Add(order);
                seed.Add(RegistrationOrderPii.CreateFromVerifiedContact(order.Id, tenantId,
                    "Range owner", email, null, null, email.ToUpperInvariant(), 1, now));
                seed.Add(RegistrationInventoryHold.Create(order.Id, pool.Id, ticket.Id, tenantId,
                    int.MaxValue, now, now.AddHours(1)));
            }
            await seed.SaveChangesAsync();
        }
        await using var observer = create();
        var repository = new RegistrationInventoryRepository(observer);
        long expected = 2L * int.MaxValue;
        await Assert.That(await repository.GetAllocatedQuantityAsync(poolId, tenantId, default)).IsEqualTo(expected);
        var usage = await repository.GetTicketLimitUsageAsync(
            eventId, tenantId, accountId, email.ToUpperInvariant(), actorId, [ticketId], default);
        await Assert.That(usage[ticketId].AccountQuantity).IsEqualTo(expected);
        await Assert.That(usage[ticketId].VerifiedContactQuantity).IsEqualTo(expected);
        await Assert.That(usage[ticketId].BookingPartyQuantity).IsEqualTo(expected);
        await Assert.That(await repository.GetAllocatedQuantityAsync(poolId, Guid.CreateVersion7(), default)).IsEqualTo(0L);
        var foreign = await repository.GetTicketLimitUsageAsync(eventId, Guid.CreateVersion7(),
            accountId, email.ToUpperInvariant(), actorId, [ticketId], default);
        await Assert.That(foreign.Count).IsEqualTo(0);
        var empty = await repository.GetTicketLimitUsageAsync(eventId, tenantId,
            accountId, email.ToUpperInvariant(), actorId, [Guid.CreateVersion7()], default);
        await Assert.That(empty.Count).IsEqualTo(0);
        var otherOwner = await repository.GetTicketLimitUsageAsync(eventId, tenantId,
            Guid.CreateVersion7(), $"other-{Guid.CreateVersion7():N}@example.test",
            Guid.CreateVersion7(), [ticketId], default);
        await Assert.That(otherOwner[ticketId].AccountQuantity).IsEqualTo(0L);
        await Assert.That(otherOwner[ticketId].VerifiedContactQuantity).IsEqualTo(0L);
        await Assert.That(otherOwner[ticketId].BookingPartyQuantity).IsEqualTo(0L);
    }
}
