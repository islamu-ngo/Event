using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class TenantQueryFilterFailClosedTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task TenantScopedRows_WhenTenantContextIsMissing_AreHiddenByDefault()
    {
        await fixture.ResetAsync();
        using var seedContext = fixture.CreateDbContext();
        var tenantA = await SeedEventWithDependencies(seedContext, "fail-closed-a");
        var tenantB = await SeedEventWithDependencies(seedContext, "fail-closed-b");
        var eventIds = new[] { tenantA.EventId, tenantB.EventId };

        using var missingTenantContext = fixture.CreateTenantFilteredDbContext();
        var missingTenantResults = await missingTenantContext.Events
            .AsNoTracking()
            .Where(@event => eventIds.Contains(@event.Id))
            .Select(@event => @event.Id)
            .ToListAsync();

        using var tenantAContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId));
        var tenantAResults = await tenantAContext.Events
            .AsNoTracking()
            .Where(@event => eventIds.Contains(@event.Id))
            .Select(@event => @event.Id)
            .ToListAsync();

        using var bypassContext = fixture.CreateTenantFilteredDbContext();
        bypassContext.EnableTenantFilterBypass("Test verifies explicit system tenant-filter bypass behavior.");
        var bypassResults = await bypassContext.Events
            .AsNoTracking()
            .Where(@event => eventIds.Contains(@event.Id))
            .Select(@event => @event.Id)
            .ToListAsync();

        await Assert.That(missingTenantResults).IsEmpty();
        await Assert.That(tenantAResults).IsEquivalentTo([tenantA.EventId]);
        await Assert.That(bypassResults).IsEquivalentTo(eventIds);
    }

    [Test]
    public async Task IgnoreTenantFilter_WhenReasonIsMissing_Throws()
    {
        using var context = fixture.CreateDbContext();

        var exception = Assert.Throws<ArgumentException>(() =>
            context.Events.IgnoreTenantFilter(string.Empty));

        await Assert.That(exception.ParamName).IsEqualTo("reason");
    }

    private static async Task<TenantEventScope> SeedEventWithDependencies(
        ExploreDbContext context,
        string slugPrefix)
    {
        var tenant = new Tenant
        {
            FullName = $"Tenant Filter {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = 2,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "Tenant",
                LastName = "Filter",
            },
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = $"Tenant Filter Actor {slugPrefix}" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = $"Tenant Filter Event {slugPrefix}",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            EventStatus = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return new TenantEventScope(tenant.Id, @event.Id);
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record TenantEventScope(Guid TenantId, Guid EventId);
}
