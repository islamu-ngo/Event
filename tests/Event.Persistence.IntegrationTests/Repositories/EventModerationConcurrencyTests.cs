using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Moderation;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventModerationConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task LightModeration_WhenStaleTransactionLosesConcurrency_RollsBackAuditAndOutbox()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (_, @event, user, _) = await SetupEventAsync(setupContext, EventStatusEnum.Published);

        await using var losingContext = fixture.CreateDbContext();
        var losingEventRepository = new EventRepository(losingContext);
        var staleEvent = await losingEventRepository.GetById(@event.Id);
        await Assert.That(staleEvent).IsNotNull();

        await using (var winningContext = fixture.CreateDbContext())
        {
            await ExecuteLightModerationAsync(
                winningContext,
                @event.Id,
                user.Id,
                "winning-light");
        }

        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
        {
            await new EfCoreUnitOfWork(losingContext).ExecuteInTransactionAsync(async ct =>
            {
                var moderationRecord = EventModerationRecord.CreateLightModeration(
                    Guid.CreateVersion7(),
                    staleEvent!.TenantId,
                    staleEvent.Id,
                    user.Id,
                    "policy_review",
                    staleEvent.EventStatusId,
                    "losing-light",
                    DateTimeOffset.UtcNow);

                staleEvent.ApplyLightModeration(DateTime.UtcNow);

                await new EventModerationRecordRepository(losingContext).Create(moderationRecord);
                await losingEventRepository.Update(staleEvent);
                await new OutboxRepository(losingContext).Create(
                    EventModerationOutboxMessageFactory.CreateLightModerationNotificationFanoutMessage(
                        Guid.CreateVersion7(),
                        staleEvent,
                        moderationRecord));
            }, CancellationToken.None);
        });

        await Assert.That(exception!.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);

        await using var verifyContext = fixture.CreateDbContext();
        var records = await verifyContext.EventModerationRecords
            .AsNoTracking()
            .Where(record => record.EventId == @event.Id)
            .ToListAsync();
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.AggregateId == @event.Id &&
                message.EventType == EventModerationOutboxMessageFactory.EventLightModeratedNotificationFanoutRequestedEventType)
            .ToListAsync();

        await Assert.That(records.Count).IsEqualTo(1);
        await Assert.That(records.Single().CorrelationId).IsEqualTo("winning-light");
        await Assert.That(outboxMessages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Unmoderation_WhenStaleTransactionLosesConcurrency_RollsBackDuplicateAudit()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (_, @event, user, _) = await SetupEventAsync(setupContext, EventStatusEnum.Moderated);
        var sourceRecord = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(),
            @event.TenantId,
            @event.Id,
            user.Id,
            "policy_review",
            (int)EventStatusEnum.Published,
            "source-light",
            DateTimeOffset.UtcNow.AddMinutes(-5));
        setupContext.EventModerationRecords.Add(sourceRecord);
        await setupContext.SaveChangesAsync();

        await using var losingContext = fixture.CreateDbContext();
        var losingEventRepository = new EventRepository(losingContext);
        var losingModerationRepository = new EventModerationRecordRepository(losingContext);
        var staleEvent = await losingEventRepository.GetById(@event.Id);
        var staleSourceRecord = await losingModerationRepository.GetLatestByEventAsync(
            @event.TenantId,
            @event.Id,
            CancellationToken.None);
        await Assert.That(staleEvent).IsNotNull();
        await Assert.That(staleSourceRecord).IsNotNull();

        await using (var winningContext = fixture.CreateDbContext())
        {
            await ExecuteUnmoderationAsync(
                winningContext,
                @event.Id,
                user.Id,
                "winning-unmoderate");
        }

        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
        {
            await new EfCoreUnitOfWork(losingContext).ExecuteInTransactionAsync(async ct =>
            {
                var unmoderationRecord = EventModerationRecord.CreateUnmoderation(
                    Guid.CreateVersion7(),
                    staleSourceRecord!,
                    user.Id,
                    "review_complete",
                    "losing-unmoderate",
                    DateTimeOffset.UtcNow);

                staleEvent!.RestoreAfterLightModeration(DateTime.UtcNow);

                await losingModerationRepository.Create(unmoderationRecord);
                await losingEventRepository.Update(staleEvent);
            }, CancellationToken.None);
        });

        await Assert.That(exception!.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);

        await using var verifyContext = fixture.CreateDbContext();
        var unmoderationRecords = await verifyContext.EventModerationRecords
            .AsNoTracking()
            .Where(record =>
                record.EventId == @event.Id &&
                record.ActionKind == EventModerationActionKind.Unmoderated)
            .ToListAsync();
        var savedEvent = await verifyContext.Events.AsNoTracking().SingleAsync(eventEntity => eventEntity.Id == @event.Id);

        await Assert.That(unmoderationRecords.Count).IsEqualTo(1);
        await Assert.That(unmoderationRecords.Single().CorrelationId).IsEqualTo("winning-unmoderate");
        await Assert.That(savedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
    }

    [Test]
    public async Task HeavyRedaction_WhenStaleGraphLosesConcurrency_RollsBackBeforeAuditAndOutbox()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (_, @event, user, _) = await SetupEventAsync(setupContext, EventStatusEnum.Published, withImage: true);

        await using var losingContext = fixture.CreateDbContext();
        var losingRedactionRepository = new EventHeavyRedactionRepository(losingContext);
        var staleGraph = await losingRedactionRepository.GetForUpdateAsync(@event.Id, CancellationToken.None);
        await Assert.That(staleGraph).IsNotNull();

        await using (var winningContext = fixture.CreateDbContext())
        {
            await ExecuteHeavyRedactionAsync(
                winningContext,
                @event.Id,
                user.Id,
                "winning-heavy");
        }

        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
        {
            await new EfCoreUnitOfWork(losingContext).ExecuteInTransactionAsync(async ct =>
            {
                var eventEntity = staleGraph!.Event;
                var moderationRecord = EventModerationRecord.CreateHeavyRedaction(
                    Guid.CreateVersion7(),
                    eventEntity.TenantId,
                    eventEntity.Id,
                    user.Id,
                    "illegal_content",
                    eventEntity.EventStatusId,
                    "losing-heavy",
                    DateTimeOffset.UtcNow);

                EventHeavyRedactionApplicator.Apply(staleGraph, user.Id, DateTimeOffset.UtcNow);

                await losingRedactionRepository.SaveChangesAsync(ct);
                await new EventModerationRecordRepository(losingContext).Create(moderationRecord);
                await new OutboxRepository(losingContext).Create(
                    EventModerationOutboxMessageFactory.CreateHeavyRedactionNotificationFanoutMessage(eventEntity, moderationRecord));
            }, CancellationToken.None);
        });

        await Assert.That(exception!.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);

        await using var verifyContext = fixture.CreateDbContext();
        var records = await verifyContext.EventModerationRecords
            .AsNoTracking()
            .Where(record => record.EventId == @event.Id)
            .ToListAsync();
        var outboxMessages = await verifyContext.OutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.AggregateId == @event.Id &&
                message.EventType == EventModerationOutboxMessageFactory.EventHeavyRedactedNotificationFanoutRequestedEventType)
            .ToListAsync();
        var savedEvent = await verifyContext.Events.AsNoTracking().SingleAsync(eventEntity => eventEntity.Id == @event.Id);

        await Assert.That(records.Count).IsEqualTo(1);
        await Assert.That(records.Single().CorrelationId).IsEqualTo("winning-heavy");
        await Assert.That(outboxMessages.Count).IsEqualTo(1);
        await Assert.That(savedEvent.EventStatusId).IsEqualTo((int)EventStatusEnum.Moderated);
        await Assert.That(savedEvent.Title).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
    }

    private static async Task ExecuteLightModerationAsync(
        ExploreDbContext context,
        Guid eventId,
        Guid moderatorUserId,
        string correlationId)
    {
        var eventRepository = new EventRepository(context);
        await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async ct =>
        {
            var eventEntity = await eventRepository.GetById(eventId)
                ?? throw new InvalidOperationException("Seeded event was not found.");
            var moderationRecord = EventModerationRecord.CreateLightModeration(
                Guid.CreateVersion7(),
                eventEntity.TenantId,
                eventEntity.Id,
                moderatorUserId,
                "policy_review",
                eventEntity.EventStatusId,
                correlationId,
                DateTimeOffset.UtcNow);

            eventEntity.ApplyLightModeration(DateTime.UtcNow);

            await new EventModerationRecordRepository(context).Create(moderationRecord);
            await eventRepository.Update(eventEntity);
            await new OutboxRepository(context).Create(
                EventModerationOutboxMessageFactory.CreateLightModerationNotificationFanoutMessage(
                    Guid.CreateVersion7(),
                    eventEntity,
                    moderationRecord));
        }, CancellationToken.None);
    }

    private static async Task ExecuteUnmoderationAsync(
        ExploreDbContext context,
        Guid eventId,
        Guid moderatorUserId,
        string correlationId)
    {
        var eventRepository = new EventRepository(context);
        var moderationRepository = new EventModerationRecordRepository(context);
        await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async ct =>
        {
            var eventEntity = await eventRepository.GetById(eventId)
                ?? throw new InvalidOperationException("Seeded event was not found.");
            var sourceRecord = await moderationRepository.GetLatestByEventAsync(
                eventEntity.TenantId,
                eventEntity.Id,
                ct) ?? throw new InvalidOperationException("Seeded moderation record was not found.");
            var unmoderationRecord = EventModerationRecord.CreateUnmoderation(
                Guid.CreateVersion7(),
                sourceRecord,
                moderatorUserId,
                "review_complete",
                correlationId,
                DateTimeOffset.UtcNow);

            eventEntity.RestoreAfterLightModeration(DateTime.UtcNow);

            await moderationRepository.Create(unmoderationRecord);
            await eventRepository.Update(eventEntity);
        }, CancellationToken.None);
    }

    private static async Task ExecuteHeavyRedactionAsync(
        ExploreDbContext context,
        Guid eventId,
        Guid moderatorUserId,
        string correlationId)
    {
        var redactionRepository = new EventHeavyRedactionRepository(context);
        await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async ct =>
        {
            var graph = await redactionRepository.GetForUpdateAsync(eventId, ct)
                ?? throw new InvalidOperationException("Seeded event graph was not found.");
            var eventEntity = graph.Event;
            var moderationRecord = EventModerationRecord.CreateHeavyRedaction(
                Guid.CreateVersion7(),
                eventEntity.TenantId,
                eventEntity.Id,
                moderatorUserId,
                "illegal_content",
                eventEntity.EventStatusId,
                correlationId,
                DateTimeOffset.UtcNow);

            EventHeavyRedactionApplicator.Apply(graph, moderatorUserId, DateTimeOffset.UtcNow);

            await redactionRepository.SaveChangesAsync(ct);
            await new EventModerationRecordRepository(context).Create(moderationRecord);
            await new OutboxRepository(context).Create(
                EventModerationOutboxMessageFactory.CreateHeavyRedactionNotificationFanoutMessage(eventEntity, moderationRecord));
        }, CancellationToken.None);
    }

    private static async Task<(Tenant Tenant, Explore.Domain.Event Event, User User, StorageObject? Image)> SetupEventAsync(
        ExploreDbContext context,
        EventStatusEnum status,
        bool withImage = false)
    {
        var tenant = new Tenant
        {
            FullName = "Moderation Concurrency Tenant",
            Slug = "moderation-concurrency-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"moderation-concurrency-{Guid.NewGuid():N}@example.com",
                FirstName = "Concurrency",
                LastName = "Moderator"
            }
        };
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Moderation Concurrency Actor" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        StorageObject? image = null;
        if (withImage)
        {
            image = new StorageObject
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
            context.StorageObjects.Add(image);
            await context.SaveChangesAsync();
        }

        var eventEntity = new Explore.Domain.Event(status)
        {
            Id = Guid.NewGuid(),
            Title = "Moderation Concurrency Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            Subtitle = "Unsafe subtitle",
            Description = "Unsafe description",
            Content = "Unsafe content",
            Slug = "moderation-concurrency-event",
            FeaturedImageId = image?.Id,
            BackgroundImageId = image?.Id,
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
        context.Events.Add(eventEntity);
        await context.SaveChangesAsync();

        return (tenant, eventEntity, user, image);
    }
}
