using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Federation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoEventProjectionDiscoveryDeduplicationTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task PublishedLinkedEventRemainsSuppressedWhenDetachedOrDeleted()
    {
        await fixture.ResetAsync();
        (Guid tenantId, Guid eventId, Guid recordId) =
            await SeedLinkedProjectionAsync(EventStatusEnum.Published);
        await using ExploreDbContext context =
            fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenantId));

        (IReadOnlyList<AtprotoEventProjection> items, int totalCount) =
            await new AtprotoEventProjectionRepository(context)
                .GetPublicWindowAsync(PublicQuery(), CancellationToken.None);

        await Assert.That(items).IsEmpty();
        await Assert.That(totalCount).IsEqualTo(0);

        await using ExploreDbContext mutationContext = fixture.CreateDbContext();
        Explore.Domain.Event linkedEvent = await mutationContext.Events.SingleAsync(value => value.Id == eventId);
        linkedEvent.AtprotoRecordId = null;
        await mutationContext.SaveChangesAsync();

        (items, totalCount) = await new AtprotoEventProjectionRepository(context)
            .GetPublicWindowAsync(PublicQuery(), CancellationToken.None);
        await Assert.That(items).IsEmpty();
        await Assert.That(totalCount).IsEqualTo(0);

        linkedEvent.AtprotoRecordId = recordId;
        linkedEvent.IsDeleted = true;
        await mutationContext.SaveChangesAsync();

        (items, totalCount) = await new AtprotoEventProjectionRepository(context)
            .GetPublicWindowAsync(PublicQuery(), CancellationToken.None);
        await Assert.That(items).IsEmpty();
        await Assert.That(totalCount).IsEqualTo(0);
    }

    [Test]
    public async Task DraftLinkedEventKeepsProjectionVisible()
    {
        await fixture.ResetAsync();
        var (tenantId, _, _) = await SeedLinkedProjectionAsync(EventStatusEnum.Draft);
        await using ExploreDbContext context =
            fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenantId));

        (IReadOnlyList<AtprotoEventProjection> items, int totalCount) =
            await new AtprotoEventProjectionRepository(context)
                .GetPublicWindowAsync(PublicQuery(), CancellationToken.None);

        await Assert.That(items).HasSingleItem();
        await Assert.That(totalCount).IsEqualTo(1);
    }

    private async Task<(Guid TenantId, Guid EventId, Guid RecordId)> SeedLinkedProjectionAsync(
        EventStatusEnum eventStatus)
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        DateTime now = new(2026, 7, 23, 10, 0, 0, DateTimeKind.Utc);
        Guid tenantId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        Guid recordId = Guid.CreateVersion7();
        var servicePrincipal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = $"atproto-dedup-{eventStatus.ToString().ToLowerInvariant()}",
            DisplayName = "Federated event importer",
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var tenant = new Tenant
        {
            Id = tenantId,
            FullName = $"ATProto de-duplication {eventStatus}",
            Slug = $"atproto-dedup-{eventStatus.ToString().ToLowerInvariant()}-{tenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var actor = new Actor
        {
            Id = actorId,
            ActorTypeId = (int)ActorTypeEnum.Bot,
            ActorType = null!,
            ServicePrincipalId = servicePrincipal.Id,
            ServicePrincipal = servicePrincipal,
            Pii = new ActorPii { DisplayName = "Federated event importer" },
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var record = new AtprotoRecord
        {
            Id = recordId,
            Did = "did:plc:discovery-deduplication",
            Collection = "community.lexicon.calendar.event",
            RecordKey = $"event-{eventStatus.ToString().ToLowerInvariant()}",
            Cid = $"bafy-{eventStatus.ToString().ToLowerInvariant()}",
            Uri = $"at://did:plc:discovery-deduplication/community.lexicon.calendar.event/event-{eventStatus.ToString().ToLowerInvariant()}",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = 1,
            RecordJson = $$"""{"name":"{{eventStatus}} linked event","createdAt":"2026-07-23T10:00:00Z"}""",
            RecordHash = new string('a', 64),
            IndexedAt = now,
            UpdatedAt = now
        };
        var importedEvent = new Explore.Domain.Event(eventStatus)
        {
            Id = Guid.CreateVersion7(),
            Title = $"{eventStatus} imported event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated,
            PublicCode = "ATPROTO",
            ActorId = actorId,
            Actor = actor,
            TenantId = tenantId,
            Tenant = tenant,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            AtprotoRecordId = recordId,
            AtprotoRecord = record,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(
            tenant,
            servicePrincipal,
            actor,
            record,
            new AtprotoEventProjection
            {
                AtprotoRecordId = recordId,
                Name = $"{eventStatus} linked event",
                CreatedAt = new DateTimeOffset(now),
                StartsAt = new DateTimeOffset(now.AddDays(1)),
                SourceVersion = 1,
                MaterializedAt = now
            },
            new AtprotoRecordTenantPresentation
            {
                TenantId = tenantId,
                AtprotoRecordId = recordId,
                IsVisible = true,
                SourceVersion = 1,
                EvaluatedAt = now
            },
            new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse(record.Did))
            {
                Id = Guid.CreateVersion7(),

                ActorId = actorId,
                Actor = actor,
                PdsHost = "https://pds.example.test",
                IsActive = true,
                LastResolvedAt = now,
                LastSeenAt = now,
                CreatedAt = now
            },
            importedEvent);
        await context.SaveChangesAsync();
        return (tenantId, importedEvent.Id, recordId);
    }

    private static AtprotoEventProjectionQuery PublicQuery() => new(
        20,
        null,
        null,
        null,
        null,
        AtprotoEventTemporalFilter.All,
        AtprotoEventDiscoverySort.Date,
        false,
        new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero));

    private sealed record StaticTenantContext(Guid TenantId) : ITenantContext;
}
