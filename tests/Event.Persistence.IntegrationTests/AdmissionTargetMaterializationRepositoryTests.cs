// ABOUTME: Verifies admission target materialization persistence returns tenant-bound Domain entities.
// ABOUTME: Proves repeated publication reuses the same target and policy rows without test seeding.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests;

public sealed class AdmissionTargetMaterializationRepositoryTests
{
    [Test]
    public async Task MaterializeAsync_WhenRepeated_PersistsOneReusableTargetAndPolicy()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"admission-target-materialization-{Guid.NewGuid():N}")
            .Options;
        DomainEvent eventTarget = CreateEvent(tenantId, eventId);
        EventTicketCatalogVersion catalog = CreateCatalog(tenantId, eventId);

        await using (ExploreDbContext seed = CreateContext(options, tenantId))
        {
            seed.EventSessions.Add(new EventSession(EventSessionStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                EventId = eventId,
                Event = null!,
                Tenant = null!,
                StartTime = new DateTimeOffset(2026, 10, 1, 13, 0, 0, TimeSpan.Zero),
                EndTime = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero)
            });
            seed.EventSessions.Add(new EventSession(EventSessionStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                TenantId = Guid.CreateVersion7(),
                EventId = eventId,
                Event = null!,
                Tenant = null!,
                StartTime = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero),
                EndTime = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero)
            });
            await seed.SaveChangesAsync();
        }

        await using (ExploreDbContext first = CreateContext(options, tenantId))
        {
            var repository = new AdmissionTargetMaterializationRepository(first);
            await new AdmissionTargetMaterializer(repository).MaterializeAsync(eventTarget, catalog, CancellationToken.None);
            await first.SaveChangesAsync();
        }

        await using (ExploreDbContext repeated = CreateContext(options, tenantId))
        {
            var repository = new AdmissionTargetMaterializationRepository(repeated);
            await new AdmissionTargetMaterializer(repository).MaterializeAsync(eventTarget, catalog, CancellationToken.None);
            await repeated.SaveChangesAsync();
        }

        await using ExploreDbContext verification = CreateContext(options, tenantId);
        AdmissionTarget target = await verification.AdmissionTargets.SingleAsync();
        AdmissionCheckInPolicy policy = await verification.AdmissionCheckInPolicies.SingleAsync();
        await Assert.That(target.EventId).IsEqualTo(eventId);
        await Assert.That(target.AdmissionTargetTypeId).IsEqualTo((int)AdmissionTargetTypeEnum.Event);
        await Assert.That(policy.AdmissionTargetId).IsEqualTo(target.Id);
        await Assert.That(policy.OpensAtUtc).IsEqualTo(new DateTime(2026, 10, 1, 13, 0, 0, DateTimeKind.Utc));
        await Assert.That(policy.ClosesAtUtc).IsEqualTo(new DateTime(2026, 10, 1, 16, 0, 0, DateTimeKind.Utc));
        await Assert.That(policy.MaximumEntries).IsEqualTo(1);
    }

    private static ExploreDbContext CreateContext(
        DbContextOptions<ExploreDbContext> options,
        Guid tenantId) => new(options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };

    private static DomainEvent CreateEvent(Guid tenantId, Guid eventId) => new()
    {
        Id = eventId,
        TenantId = tenantId,
        Title = "Materialized admission",
        Actor = null!,
        Tenant = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };

    private static EventTicketCatalogVersion CreateCatalog(Guid tenantId, Guid eventId)
    {
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        EventTicketType ticketType = EventTicketType.Create(
            Guid.CreateVersion7(), tenantId, catalog.Id, "General admission", "USD",
            TicketPricingModeEnum.Free, null, null, null,
            ParticipantDataCollectionModeEnum.None, null, null, null, false, false,
            null, null, null, null);
        catalog.AddTicketType(ticketType, null);
        catalog.AddEntitlement(ticketType, TicketTypeEntitlement.CreateForEvent(ticketType.Id, tenantId, eventId, 1));
        return catalog;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
