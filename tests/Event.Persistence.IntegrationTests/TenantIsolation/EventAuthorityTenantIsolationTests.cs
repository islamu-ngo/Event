using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventAuthorityTenantIsolationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EventAuthorityRows_RespectTenantAndSoftDeleteFilters()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();
        var tenantA = await SeedAuthorityRowsAsync(seedContext, "event-authority-a");
        var tenantB = await SeedAuthorityRowsAsync(seedContext, "event-authority-b");

        await using var missingTenantContext = fixture.CreateTenantFilteredDbContext();
        await Assert.That(await missingTenantContext.Set<EventPublicAction>().CountAsync()).IsEqualTo(0);
        await Assert.That(await missingTenantContext.Set<EventOrganizerClaim>().CountAsync()).IsEqualTo(0);

        await using var tenantAContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId));
        await Assert.That(await tenantAContext.Set<EventPublicAction>().Select(row => row.Id).ToArrayAsync())
            .IsEquivalentTo([tenantA.ActiveActionId]);
        await Assert.That(await tenantAContext.Set<EventOrganizerClaim>().Select(row => row.Id).ToArrayAsync())
            .IsEquivalentTo([tenantA.ClaimId]);

        var tenantAWithDeleted = await tenantAContext.Set<EventPublicAction>()
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Select(row => row.Id)
            .ToArrayAsync();
        await Assert.That(tenantAWithDeleted).IsEquivalentTo([tenantA.ActiveActionId, tenantA.DeletedActionId]);

        await using var bypassContext = fixture.CreateTenantFilteredDbContext();
        bypassContext.EnableTenantFilterBypass("Test verifies explicit event-authority tenant bypass behavior.");
        await Assert.That(await bypassContext.Set<EventPublicAction>().Select(row => row.Id).ToArrayAsync())
            .IsEquivalentTo([tenantA.ActiveActionId, tenantB.ActiveActionId]);
        await Assert.That(await bypassContext.Set<EventOrganizerClaim>().Select(row => row.Id).ToArrayAsync())
            .IsEquivalentTo([tenantA.ClaimId, tenantB.ClaimId]);
    }

    private static async Task<AuthorityScope> SeedAuthorityRowsAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            FullName = $"Event Authority {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "Event",
                LastName = "Authority"
            }
        };
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = $"Event Authority Actor {slugPrefix}" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = $"Event Authority Event {slugPrefix}",
            ActorId = actor.Id,
            Actor = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.CommunityReported,
            TenantId = tenant.Id,
            Tenant = null!,
            EventStatus = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        var activeAction = CreateAction(tenant.Id, @event.Id, false, $"https://example.com/{slugPrefix}/active");
        var deletedAction = CreateAction(tenant.Id, @event.Id, true, $"https://example.com/{slugPrefix}/deleted");
        var claim = EventOrganizerClaim.CreatePending(
            tenant.Id,
            @event.Id,
            actor.Id,
            "domain-verification",
            $"https://example.com/{slugPrefix}/evidence",
            DateTime.UtcNow);
        context.Set<EventPublicAction>().AddRange(activeAction, deletedAction);
        context.Set<EventOrganizerClaim>().Add(claim);
        await context.SaveChangesAsync();

        return new AuthorityScope(tenant.Id, activeAction.Id, deletedAction.Id, claim.Id);
    }

    private static EventPublicAction CreateAction(Guid tenantId, Guid eventId, bool isDeleted, string url)
    {
        var action = new EventPublicAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            EventPublicActionKindId = (int)EventPublicActionKindEnum.OriginalSource,
            HealthStateId = (int)EventPublicActionHealthStateEnum.Active,
            SortOrder = 0,
            IsPrimary = false,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        action.SetDestination(ExternalActionUrl.Create(url));
        return action;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record AuthorityScope(
        Guid TenantId,
        Guid ActiveActionId,
        Guid DeletedActionId,
        Guid ClaimId);
}
