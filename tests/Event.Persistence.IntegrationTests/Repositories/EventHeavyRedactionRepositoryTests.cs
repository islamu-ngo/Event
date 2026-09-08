using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Features.Events.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventHeavyRedactionRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GetForUpdateAsync_WhenGraphIsRedacted_PersistsRedactionAndImageDeleteState()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, image) = await SetupEventGraphAsync(context);
        var repository = new EventHeavyRedactionRepository(context);

        var graph = await repository.GetForUpdateAsync(@event.Id, CancellationToken.None);

        await Assert.That(graph).IsNotNull();
        EventHeavyRedactionApplicator.Apply(graph!, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(CancellationToken.None);

        await using var assertionContext = fixture.CreateDbContext();
        var savedEvent = await assertionContext.Events
            .AsNoTracking()
            .SingleAsync(e => e.Id == @event.Id);
        var savedSession = await assertionContext.EventSessions
            .AsNoTracking()
            .SingleAsync(s => s.EventId == @event.Id);
        var savedDay = await assertionContext.EventDays
            .AsNoTracking()
            .SingleAsync(d => d.EventId == @event.Id);
        var savedStorageObject = await assertionContext.StorageObjects
            .AsNoTracking()
            .SingleAsync(s => s.Id == image.Id);

        await Assert.That(savedEvent.TenantId).IsEqualTo(tenant.Id);
        await Assert.That(savedEvent.Title).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(savedEvent.Slug).StartsWith("redacted-event-");
        await Assert.That(savedEvent.FeaturedImageId).IsNull();
        await Assert.That(savedEvent.BackgroundImageId).IsNull();
        await Assert.That(savedEvent.AtprotoRecordId).IsNull();
        await Assert.That(savedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Moderated);

        await Assert.That(savedSession.Title).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(savedSession.FeaturedImageId).IsNull();
        await Assert.That(savedDay.Label).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(savedDay.BannerImageId).IsNull();
        await Assert.That(savedStorageObject.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        await Assert.That(savedStorageObject.OwningResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(savedStorageObject.OwningResourceId).IsEqualTo(@event.Id);
    }

    private static async Task<(Tenant Tenant, Explore.Domain.Event Event, StorageObject Image)> SetupEventGraphAsync(
        Explore.Persistence.ExploreDbContext context)
    {
        var tenant = new Tenant
        {
            FullName = "Heavy Redaction Tenant",
            Slug = "heavy-redaction-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"heavy-redaction-{Guid.NewGuid():N}@example.com",
                FirstName = "Heavy",
                LastName = "Moderator"
            }
        };
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Heavy Redaction Actor" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var image = new StorageObject
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Provider = StorageProviders.Local,
            ObjectKey = $"tenants/{tenant.Id:N}/illegal.png",
            Uri = "/images/illegal.png",
            FullName = "illegal.png",
            SafeDisplayName = "illegal.png",
            Extension = ".png",
            Size = 100,
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active
        };
        var atprotoRecord = new AtprotoRecord
        {
            Id = Guid.NewGuid(),
            Did = "did:plc:heavyredaction",
            Collection = "app.bsky.feed.post",
            RecordKey = Guid.NewGuid().ToString("N"),
            Cid = "unsafe-cid",
            Uri = "at://did:plc:heavyredaction/app.bsky.feed.post/unsafe",
            Direction = AtprotoRecordDirection.Outbound,
            Provenance = AtprotoRecordProvenance.LocalLifecycle
        };
        context.StorageObjects.Add(image);
        context.AtprotoRecords.Add(atprotoRecord);
        await context.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = eventId,
            Title = "Illegal Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            Subtitle = "Illegal Subtitle",
            Description = "Illegal Description",
            Content = "Illegal Content",
            Slug = "illegal-event",
            FeaturedImageId = image.Id,
            BackgroundImageId = image.Id,
            AtprotoRecordId = atprotoRecord.Id,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!
        };
        var session = new EventSession(EventSessionStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = @event,
            Title = "Illegal Session",
            Description = "Illegal Session Description",
            Slug = "illegal-session",
            FeaturedImageId = image.Id,
            TenantId = tenant.Id,
            Tenant = null!,
        };
        var day = new EventDay
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = @event,
            LocalDate = new DateOnly(2026, 7, 1),
            Label = "Illegal Day",
            Description = "Illegal Day Description",
            BannerText = "Illegal Banner",
            BannerImageId = image.Id,
            TenantId = tenant.Id,
            Tenant = null!
        };

        context.Events.Add(@event);
        context.EventSessions.Add(session);
        context.EventDays.Add(day);
        await context.SaveChangesAsync();

        return (tenant, @event, image);
    }
}
