using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventAiReferenceRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SearchAiReferenceEventsAsync_ReturnsPublicTenantMatchesOnlyInDeterministicOrder()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAsync("ai-reference-a");
        var tenantB = await SeedTenantAsync("ai-reference-b");

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Events.Add(CreateEvent(
                tenantA,
                "Alpha community eid lecture",
                "Searchable public match",
                EventStatusEnum.Published,
                VisibilityTypeEnum.Public,
                DateTimeOffset.UtcNow.AddDays(2)));
            seedContext.Events.Add(CreateEvent(
                tenantA,
                "Beta community eid lecture",
                "Second public match",
                EventStatusEnum.Published,
                VisibilityTypeEnum.Public,
                DateTimeOffset.UtcNow.AddDays(1)));
            seedContext.Events.Add(CreateEvent(
                tenantA,
                "Private community eid lecture",
                "Private match must be hidden",
                EventStatusEnum.Published,
                VisibilityTypeEnum.Private,
                DateTimeOffset.UtcNow.AddDays(3)));
            seedContext.Events.Add(CreateEvent(
                tenantA,
                "Draft community eid lecture",
                "Draft match must be hidden",
                EventStatusEnum.Draft,
                VisibilityTypeEnum.Public,
                DateTimeOffset.UtcNow.AddDays(4)));
            seedContext.Events.Add(CreateEvent(
                tenantB,
                "Other tenant community eid lecture",
                "Tenant-filtered match must be hidden",
                EventStatusEnum.Published,
                VisibilityTypeEnum.Public,
                DateTimeOffset.UtcNow.AddDays(1)));
            await seedContext.SaveChangesAsync();
        }

        await using var tenantAContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId));
        var repository = new EventRepository(tenantAContext);

        IReadOnlyList<DomainEvent> results = await repository.SearchAiReferenceEventsAsync(
            "eid lecture",
            limit: 1,
            CancellationToken.None);

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results.Single().Title).IsEqualTo("Beta community eid lecture");
        await Assert.That(results.Single().TenantId).IsEqualTo(tenantA.TenantId);
        await Assert.That(results.Single().EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That(results.Single().VisibilityTypeId).IsEqualTo((int)VisibilityTypeEnum.Public);
    }

    [Test]
    public async Task SearchAiReferenceEventsAsync_WhenTenantDoesNotMatch_ReturnsNoRows()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedTenantAsync("ai-reference-visible");
        var tenantB = await SeedTenantAsync("ai-reference-hidden");

        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.Events.Add(CreateEvent(
                tenantA,
                "Tenant-only zakat workshop",
                "Visible only to tenant A",
                EventStatusEnum.Published,
                VisibilityTypeEnum.Public,
                DateTimeOffset.UtcNow.AddDays(1)));
            await seedContext.SaveChangesAsync();
        }

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.TenantId));
        var repository = new EventRepository(tenantBContext);

        IReadOnlyList<DomainEvent> results = await repository.SearchAiReferenceEventsAsync(
            "zakat",
            limit: 10,
            CancellationToken.None);

        await Assert.That(results).IsEmpty();
    }

    private async Task<EventReferenceScope> SeedTenantAsync(string slugPrefix)
    {
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"AI Reference {slugPrefix}",
            Slug = $"ai-reference-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"ai-reference-{slugPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Reference",
                LastName = "Tester",
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            Pii = new ActorPii { DisplayName = $"AI Reference Actor {slugPrefix}" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        context.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active
        });
        await context.SaveChangesAsync();

        return new EventReferenceScope(tenant.Id, actor.Id);
    }

    private static DomainEvent CreateEvent(
        EventReferenceScope scope,
        string title,
        string description,
        EventStatusEnum status,
        VisibilityTypeEnum visibility,
        DateTimeOffset startsAt)
    {
        return new DomainEvent(status)
        {
            Id = Guid.CreateVersion7(),
            Title = title,
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            Subtitle = "AI reference card summary",
            Description = description,
            Content = "Full event content should not be needed for AI reference search.",
            FirstSessionDate = DateOnly.FromDateTime(startsAt.UtcDateTime),
            LastSessionDate = DateOnly.FromDateTime(startsAt.UtcDateTime),
            FirstSessionStartUtc = startsAt,
            LastSessionStartUtc = startsAt.AddHours(1),
            ActorId = scope.ActorId,
            Actor = null!,
            TenantId = scope.TenantId,
            Tenant = null!,
            VisibilityTypeId = (int)visibility,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            TotalViews = 0,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record EventReferenceScope(Guid TenantId, Guid ActorId);
}
