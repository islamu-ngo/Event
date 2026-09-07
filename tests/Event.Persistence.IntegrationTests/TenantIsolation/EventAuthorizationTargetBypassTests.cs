using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class EventAuthorizationTargetBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GetAuthorizationTargetByIdAsync_WithAmbientTenant_ReturnsOnlyExactEvent()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("event-auth-a");
        var tenantB = CreateTenant("event-auth-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var actorA = await CreateActorAsync(seedContext, tenantA, "event-auth-a");
        var actorB = await CreateActorAsync(seedContext, tenantB, "event-auth-b");
        var tenantAEvent = CreateEvent(tenantA.Id, actorA.Id, "Tenant A authorization target");
        var tenantBEvent = CreateEvent(tenantB.Id, actorB.Id, "Tenant B ambient event");
        seedContext.Events.AddRange(tenantAEvent, tenantBEvent);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.Events
            .AsNoTracking()
            .Select(@event => @event.Id)
            .ToListAsync();

        var repository = new EventRepository(tenantBContext);
        var tenantAWithoutBypass = await repository.GetById(tenantAEvent.Id);
        var tenantAAuthorizationTarget = await repository.GetAuthorizationTargetByIdAsync(
            tenantAEvent.Id,
            CancellationToken.None);
        var missingAuthorizationTarget = await repository.GetAuthorizationTargetByIdAsync(
            Guid.CreateVersion7(),
            CancellationToken.None);

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantBEvent.Id]);
        await Assert.That(tenantAWithoutBypass).IsNull();
        await Assert.That(tenantAAuthorizationTarget).IsNotNull();
        await Assert.That(tenantAAuthorizationTarget!.Id).IsEqualTo(tenantAEvent.Id);
        await Assert.That(tenantAAuthorizationTarget.TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(missingAuthorizationTarget).IsNull();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Event Authorization {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static async Task<Actor> CreateActorAsync(
        Explore.Persistence.ExploreDbContext context,
        Tenant tenant,
        string slugPrefix)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"{slugPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Authorization",
                LastName = "Target",
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            Pii = new ActorPii { DisplayName = $"Event Authorization Actor {slugPrefix}" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        return actor;
    }

    private static DomainEvent CreateEvent(Guid tenantId, Guid actorId, string title)
    {
        return new DomainEvent(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = title,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actorId,
            Actor = null!,
            TenantId = tenantId,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            TotalViews = 0,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
