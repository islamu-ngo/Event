using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Event.Api.IntegrationTests.Features.Notifications;

/// <summary>Collects final native fanout diagnostics without replacing business collaborators.</summary>
[NotInParallel]
public sealed class NativeFanoutDiagnosticSinkTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublishedFanoutPreservesRecipientStateAndReplayWithoutPrivateDiagnosticCopies(bool includeFencedSubscriber)
    {
        var sink = new FanoutDiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(sink)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var database = services.GetRequiredService<ExploreDbContext>();
        var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
        var subscriber = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(database);
        var tenantUser = await database.TenantUsers.SingleAsync(value => value.UserId == subscriber.UserId);
        AddSubscription(database, tenantUser, publisher.OrganizationActorId);
        var target = new EventBuilder()
            .WithTenantId(publisher.TenantId)
            .WithActorId(publisher.OrganizationActorId)
            .WithTitle(Guid.NewGuid().ToString("N"))
            .WithStatus(EventStatusEnum.Published)
            .Build();
        database.Events.Add(target);
        Guid? fencedUserId = null;
        Guid? fencedActorId = null;
        Guid? fencedTenantUserId = null;
        if (includeFencedSubscriber)
        {
            var fenced = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(database);
            var fencedTenantUser = await database.TenantUsers.SingleAsync(value => value.UserId == fenced.UserId);
            AddSubscription(database, fencedTenantUser, publisher.OrganizationActorId);
            fencedUserId = fenced.UserId;
            fencedActorId = fenced.ActorId;
            fencedTenantUserId = fencedTenantUser.Id;
            var now = DateTime.UtcNow;
            var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
                fenced.UserId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
            var privacy = services.GetRequiredService<IPrivacyErasureStateRepository>();
            await privacy.AddSagaAsync(PrivacyErasureSaga.Start(
                intent, 1, RandomNumberGenerator.GetBytes(32), now.AddDays(1), now), default);
        }
        await database.SaveChangesAsync();
        var request = new EventPublishedNotificationFanoutRequested
        {
            TenantId = publisher.TenantId,
            EventId = target.Id,
            SourceActorId = publisher.OrganizationActorId,
            EventTitle = target.Title,
            StartDate = DateTimeOffset.UtcNow.AddDays(1),
            EndDate = null,
            PublishedAt = DateTimeOffset.UtcNow
        };
        var fanout = services.GetRequiredService<IEventPublishedNotificationFanoutService>();
        sink.Arm();

        await fanout.FanoutAsync(request);
        database.ChangeTracker.Clear();

        var run = await database.NotificationFanoutRuns.IgnoreQueryFilters().SingleAsync(value =>
            value.TenantId == request.TenantId && value.EntityId == target.Id
            && value.FanoutKind == EventPublishedNotificationFanoutService.FanoutKind);
        var notifications = await database.Notifications.IgnoreQueryFilters().Where(value =>
            value.TenantId == request.TenantId && value.EntityId == target.Id.ToString()).ToListAsync();
        await Assert.That(run.Status).IsEqualTo(EventPublishedNotificationFanoutService.StatusCompleted);
        await Assert.That(run.ProcessedCount).IsEqualTo(includeFencedSubscriber ? 2 : 1);
        await Assert.That(run.CreatedNotificationCount).IsEqualTo(1);
        await Assert.That(run.CompletedAt).IsNotNull();
        await Assert.That(run.LastError).IsNull();
        await Assert.That(notifications).Count().IsEqualTo(1);
        var notification = notifications.Single();
        await Assert.That(notification.UserId).IsEqualTo(subscriber.UserId);
        await Assert.That(notification.SourceActorId).IsEqualTo(request.SourceActorId);
        await Assert.That(notification.NotificationTypeId).IsEqualTo((int)NotificationTypeEnum.EventCreated);
        await Assert.That(notification.NotificationReasonId).IsEqualTo((int)NotificationReasonEnum.Subscription);
        await Assert.That(notification.Title).IsEqualTo($"New event: {target.Title}");
        if (fencedUserId is { } subject)
        {
            await Assert.That(await services.GetRequiredService<IPrivacyErasureStateRepository>()
                .GetBySubjectAsync(subject, default)).IsNotNull();
        }
        var producer = sink.FromProducer<EventPublishedNotificationFanoutService>();
        await Assert.That(producer.Any(value => HasScalar(value, "ProcessedCount", includeFencedSubscriber ? 2 : 1)
            && HasScalar(value, "CreatedCount", 1) && HasScalar(value, "DuplicateSkippedCount", 0))).IsTrue();
        Guid?[] privateIds =
        [
            request.TenantId, target.Id, request.SourceActorId, publisher.UserId, publisher.ActorId,
            subscriber.UserId, subscriber.ActorId, tenantUser.Id, notification.Id, run.Id,
            fencedUserId, fencedActorId, fencedTenantUserId
        ];
        await AssertPrivateCopiesAbsentAsync(sink, privateIds, target.Title);
        Guid runId = run.Id;
        Guid notificationId = notification.Id;
        DateTime? completedAt = run.CompletedAt;
        Guid? cursor = run.CursorSubscriberTenantUserId;
        sink.Arm();

        await fanout.FanoutAsync(request);
        database.ChangeTracker.Clear();

        var replayRun = await database.NotificationFanoutRuns.IgnoreQueryFilters().SingleAsync(value =>
            value.TenantId == request.TenantId && value.EntityId == target.Id
            && value.FanoutKind == EventPublishedNotificationFanoutService.FanoutKind);
        await Assert.That(replayRun.Id).IsEqualTo(runId);
        await Assert.That(replayRun.Status).IsEqualTo(EventPublishedNotificationFanoutService.StatusCompleted);
        await Assert.That(replayRun.ProcessedCount).IsEqualTo(includeFencedSubscriber ? 2 : 1);
        await Assert.That(replayRun.CreatedNotificationCount).IsEqualTo(1);
        await Assert.That(replayRun.CompletedAt).IsEqualTo(completedAt);
        await Assert.That(replayRun.CursorSubscriberTenantUserId).IsEqualTo(cursor);
        await Assert.That(await database.Notifications.IgnoreQueryFilters().Where(value =>
            value.TenantId == request.TenantId && value.EntityId == target.Id.ToString())
            .Select(value => value.Id).ToListAsync()).IsEquivalentTo([notificationId]);
        await Assert.That(sink.FromProducer<EventPublishedNotificationFanoutService>().Length > 0).IsTrue();
        await AssertPrivateCopiesAbsentAsync(sink, privateIds, target.Title);
    }

    [Test]
    [Arguments(false, "none")]
    [Arguments(true, "none")]
    [Arguments(true, "actor")]
    [Arguments(true, "record")]
    [Arguments(true, "event")]
    [Arguments(true, "heavy-record")]
    [Arguments(true, "content")]
    public async Task LightModerationPreservesAudienceAndReplayWithoutPrivateDiagnosticCopies(
        bool includeRegisteredAudience, string changed)
    {
        var sink = new FanoutDiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(sink)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
        var target = new EventBuilder()
            .WithTenantId(publisher.TenantId)
            .WithActorId(publisher.OrganizationActorId)
            .WithTitle(Guid.NewGuid().ToString("N"))
            .WithStatus(EventStatusEnum.Moderated)
            .Build();
        database.Events.Add(target);
        EventTicketCatalogVersion? catalog = null;
        RegistrationOrder? order = null;
        if (includeRegisteredAudience)
        {
            catalog = EventTicketCatalogVersion.Create(publisher.TenantId, target.Id, "USD", 1);
            database.EventTicketCatalogVersions.Add(catalog);
            DateTime now = DateTime.UtcNow;
            order = RegistrationOrder.Create(
                publisher.TenantId, target.Id, publisher.UserId, null,
                BookingPartyTypeEnum.Individual, catalog.Id,
                RegistrationParticipationSnapshot.Create(
                    Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                    (int)AdvanceRegistrationObligationEnum.Required,
                    (int)IdentityAccessModeEnum.AccountRequired, GuestRecoveryPolicyEnum.NoRecovery),
                null, null, "USD", now, null);
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, now);
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
            order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
            order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, now);
            database.RegistrationOrders.Add(order);
        }
        var record = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(), publisher.TenantId, target.Id, publisher.UserId,
            "native-diagnostic", (int)EventStatusEnum.Published, null, DateTimeOffset.UtcNow);
        database.EventModerationRecords.Add(record);
        await database.SaveChangesAsync();
        var request = new EventLightModeratedNotificationFanoutRequested
        {
            TenantId = publisher.TenantId,
            EventId = target.Id,
            ModerationRecordId = record.Id,
            SourceActorId = publisher.OrganizationActorId,
            EventTitle = target.Title,
            ModeratedAt = record.CreatedAt
        };
        string canary = Guid.CreateVersion7().ToString("N");
        if (changed == "actor")
            request = request with { SourceActorId = publisher.ActorId };
        else if (changed == "record")
            request = request with { ModerationRecordId = Guid.CreateVersion7() };
        else if (changed == "event")
        {
            var other = new EventBuilder().WithTenantId(publisher.TenantId)
                .WithActorId(publisher.OrganizationActorId).WithStatus(EventStatusEnum.Published).Build();
            database.Events.Add(other);
            await database.SaveChangesAsync();
            request = request with { EventId = other.Id };
        }
        else if (changed == "heavy-record")
        {
            var heavy = EventModerationRecord.CreateHeavyRedaction(
                Guid.CreateVersion7(), publisher.TenantId, target.Id, publisher.UserId,
                "native-diagnostic", (int)EventStatusEnum.Published, null, record.CreatedAt);
            database.EventModerationRecords.Add(heavy);
            await database.SaveChangesAsync();
            request = request with { ModerationRecordId = heavy.Id };
        }
        else if (changed == "content")
            request = request with { EventTitle = canary, ModeratedAt = record.CreatedAt.AddDays(-1) };
        var fanout = scope.ServiceProvider.GetRequiredService<IEventModerationNotificationFanoutService>();
        sink.Arm();

        if (changed is "actor" or "record" or "event" or "heavy-record")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => fanout.FanoutLightModerationAsync(request));
            database.ChangeTracker.Clear();
            await Assert.That(await database.NotificationFanoutRuns.IgnoreQueryFilters()
                .AnyAsync(value => value.FanoutKind == EventModerationNotificationFanoutService.LightFanoutKind)).IsFalse();
            await Assert.That(await database.Notifications.IgnoreQueryFilters()
                .AnyAsync(value => value.EntityId == target.Id.ToString() || value.EntityId == request.EventId.ToString())).IsFalse();
            return;
        }
        await fanout.FanoutLightModerationAsync(request);
        database.ChangeTracker.Clear();

        var run = await database.NotificationFanoutRuns.IgnoreQueryFilters().SingleAsync(value =>
            value.TenantId == request.TenantId && value.EntityId == record.Id
            && value.FanoutKind == EventModerationNotificationFanoutService.LightFanoutKind);
        await Assert.That(run.Status).IsEqualTo(EventModerationNotificationFanoutService.StatusCompleted);
        int expectedCount = includeRegisteredAudience ? 1 : 0;
        await Assert.That(run.ProcessedCount).IsEqualTo(expectedCount);
        await Assert.That(run.CreatedNotificationCount).IsEqualTo(expectedCount);
        await Assert.That(run.CompletedAt).IsNotNull();
        await Assert.That(run.LastError).IsNull();
        await Assert.That(run.CursorSubscriberTenantUserId)
            .IsEqualTo(includeRegisteredAudience ? (Guid?)publisher.UserId : null);
        var notifications = await database.Notifications.IgnoreQueryFilters().Where(value =>
            value.TenantId == request.TenantId && value.EntityId == target.Id.ToString()).ToListAsync();
        await Assert.That(notifications).Count().IsEqualTo(expectedCount);
        if (includeRegisteredAudience)
        {
            var notification = notifications.Single();
            await Assert.That(notification.UserId).IsEqualTo(publisher.UserId);
            await Assert.That(notification.SourceActorId).IsEqualTo(request.SourceActorId);
            await Assert.That(notification.NotificationTypeId).IsEqualTo((int)NotificationTypeEnum.EventUpdated);
            await Assert.That(notification.NotificationReasonId).IsEqualTo((int)NotificationReasonEnum.System);
            await Assert.That(notification.Title).IsEqualTo($"Event moderated: {target.Title}");
            await Assert.That(notification.Title).DoesNotContain(canary);
        }
        Guid[] notificationIds = notifications.Select(value => value.Id).ToArray();
        Guid runId = run.Id;
        DateTime? completedAt = run.CompletedAt;
        Guid?[] privateIds =
        [
            request.TenantId, target.Id, record.Id, request.SourceActorId, publisher.UserId,
            publisher.ActorId, run.Id, catalog?.Id, order?.Id,
            .. notificationIds.Select(value => (Guid?)value)
        ];
        await AssertPrivateCopiesAbsentAsync(sink, privateIds, target.Title);
        sink.Arm();

        await fanout.FanoutLightModerationAsync(request);
        database.ChangeTracker.Clear();

        var replayRun = await database.NotificationFanoutRuns.IgnoreQueryFilters().SingleAsync(value =>
            value.TenantId == request.TenantId && value.EntityId == record.Id
            && value.FanoutKind == EventModerationNotificationFanoutService.LightFanoutKind);
        await Assert.That(replayRun.Id).IsEqualTo(runId);
        await Assert.That(replayRun.CompletedAt).IsEqualTo(completedAt);
        await Assert.That(replayRun.Status).IsEqualTo(EventModerationNotificationFanoutService.StatusCompleted);
        await Assert.That(replayRun.ProcessedCount).IsEqualTo(expectedCount);
        await Assert.That(replayRun.CreatedNotificationCount).IsEqualTo(expectedCount);
        await Assert.That(replayRun.CursorSubscriberTenantUserId).IsEqualTo(run.CursorSubscriberTenantUserId);
        await Assert.That(await database.Notifications.IgnoreQueryFilters().Where(value =>
            value.TenantId == request.TenantId && value.EntityId == target.Id.ToString())
            .Select(value => value.Id).ToListAsync()).IsEquivalentTo(notificationIds);
        if (order is not null)
        {
            await Assert.That((await database.RegistrationOrders.IgnoreQueryFilters()
                .SingleAsync(value => value.Id == order.Id)).RegistrationOrderStatusId)
                .IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
        }
        var retained = await scope.ServiceProvider.GetRequiredService<IEventModerationRecordRepository>()
            .GetByIdAsync(request.TenantId, record.Id, default);
        await Assert.That(retained).IsNotNull();
        await Assert.That(retained!.AllowsUnmoderation).IsTrue();
        await Assert.That(retained.EventId).IsEqualTo(target.Id);
        await Assert.That((await database.Events.IgnoreQueryFilters().SingleAsync(value => value.Id == target.Id))
            .EventStatusId).IsEqualTo((int)EventStatusEnum.Moderated);
        await Assert.That(sink.FromProducer<EventModerationNotificationFanoutService>().Length > 0).IsTrue();
        await AssertPrivateCopiesAbsentAsync(sink, privateIds, target.Title);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HeavyModerationRetainsOneDurablePointerOnReplayWithoutPrivateDiagnosticCopies(bool mismatchedEventEnvelope)
    {
        var sink = new FanoutDiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(sink)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
        var target = new EventBuilder()
            .WithTenantId(publisher.TenantId)
            .WithActorId(publisher.OrganizationActorId)
            .WithTitle(Guid.NewGuid().ToString("N"))
            .WithStatus(EventStatusEnum.Moderated)
            .Build();
        database.Events.Add(target);
        var record = EventModerationRecord.CreateHeavyRedaction(
            Guid.CreateVersion7(), publisher.TenantId, target.Id, publisher.UserId,
            Guid.NewGuid().ToString("N"), (int)EventStatusEnum.Published, null, DateTimeOffset.UtcNow);
        database.EventModerationRecords.Add(record);
        await database.SaveChangesAsync();
        var request = new EventHeavyRedactedNotificationFanoutRequested
        {
            TenantId = publisher.TenantId,
            ModerationRecordId = record.Id
        };
        var fanout = scope.ServiceProvider.GetRequiredService<IEventModerationNotificationFanoutService>();
        sink.Arm();

        if (mismatchedEventEnvelope)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fanout.FanoutHeavyRedactionAsync(request, Guid.CreateVersion7()));
            database.ChangeTracker.Clear();
            await Assert.That(await database.Set<NotificationFanoutOccurrence>().IgnoreQueryFilters()
                .AnyAsync(value => value.SourceId == record.Id)).IsFalse();
            await Assert.That(await database.OutboxMessages.AnyAsync(value =>
                value.EventType == NotificationFanoutOccurrenceOutboxMessageFactory.EventType)).IsFalse();
            await Assert.That((await database.EventModerationRecords.SingleAsync(value => value.Id == record.Id))
                .IsIrreversible).IsTrue();
            return;
        }
        await fanout.FanoutHeavyRedactionAsync(request, target.Id);
        database.ChangeTracker.Clear();

        var occurrence = await database.Set<NotificationFanoutOccurrence>().IgnoreQueryFilters()
            .SingleAsync(value => value.TenantId == request.TenantId && value.SourceId == record.Id);
        await Assert.That(occurrence.EventId).IsEqualTo(target.Id);
        await Assert.That(occurrence.AggregateVersion).IsEqualTo(record.Id);
        await Assert.That(occurrence.DeliveryPolicyId)
            .IsEqualTo((int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired);
        await Assert.That(occurrence.State).IsEqualTo(NotificationFanoutOccurrenceState.Pending);
        var pointer = await database.Set<OutboxMessage>().SingleAsync(value => value.AggregateId == occurrence.Id);
        var payload = NotificationFanoutOccurrenceOutboxMessageFactory.DeserializePointer(pointer.Payload!);
        await Assert.That(payload.TenantId).IsEqualTo(request.TenantId);
        await Assert.That(payload.OccurrenceId).IsEqualTo(occurrence.Id);
        await Assert.That(pointer.EventType).IsEqualTo(NotificationFanoutOccurrenceOutboxMessageFactory.EventType);
        await Assert.That(pointer.Status).IsEqualTo(OutboxMessageStatus.Pending);
        Guid?[] privateIds =
        [
            request.TenantId, target.Id, record.Id, publisher.UserId, publisher.ActorId,
            publisher.OrganizationActorId, occurrence.Id, pointer.Id
        ];
        await Assert.That(sink.FromProducer<EventModerationNotificationFanoutService>()
            .Any(entry => HasScalar(entry, "Outcome", NotificationFanoutOccurrenceCoordinationOutcome.NewlyActive))).IsTrue();
        await AssertPrivateCopiesAbsentAsync(sink, privateIds, target.Title);
        sink.Arm();

        await fanout.FanoutHeavyRedactionAsync(request, target.Id);
        database.ChangeTracker.Clear();

        var replay = await database.Set<NotificationFanoutOccurrence>().IgnoreQueryFilters()
            .SingleAsync(value => value.TenantId == request.TenantId && value.SourceId == record.Id);
        await Assert.That(replay.Id).IsEqualTo(occurrence.Id);
        await Assert.That(replay.State).IsEqualTo(NotificationFanoutOccurrenceState.Pending);
        await Assert.That(replay.EmailDeliveryPolicyRevision).IsEqualTo(occurrence.EmailDeliveryPolicyRevision);
        var replayPointer = await database.Set<OutboxMessage>().SingleAsync(value => value.AggregateId == replay.Id);
        await Assert.That(replayPointer.Id).IsEqualTo(pointer.Id);
        await Assert.That(NotificationFanoutOccurrenceOutboxMessageFactory.DeserializePointer(replayPointer.Payload!))
            .IsEqualTo(payload);
        var retained = await database.EventModerationRecords.IgnoreQueryFilters()
            .SingleAsync(value => value.Id == record.Id);
        await Assert.That(retained.IsIrreversible).IsTrue();
        await Assert.That(retained.AllowsUnmoderation).IsFalse();
        await Assert.That(retained.EventId).IsEqualTo(target.Id);
        await Assert.That(sink.FromProducer<EventModerationNotificationFanoutService>()
            .Any(entry => HasScalar(entry, "Outcome", NotificationFanoutOccurrenceCoordinationOutcome.SourceReplay))).IsTrue();
        await AssertPrivateCopiesAbsentAsync(sink, privateIds, target.Title);
    }

    private static void AddSubscription(ExploreDbContext database, TenantUser subscriber, Guid sourceActorId) =>
        database.ActorSubscriptions.Add(new ActorSubscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = subscriber.TenantId,
            Tenant = null!,
            SubscriberTenantUserId = subscriber.Id,
            SubscriberTenantUser = null!,
            SubscriberUserId = subscriber.UserId,
            SubscriberUser = null!,
            TargetActorId = sourceActorId,
            TargetActor = null!,
            TargetActorTypeId = (int)ActorTypeEnum.Organization,
            TargetActorType = null!,
            StatusId = (int)ActorSubscriptionStatusEnum.Active,
            Status = null!,
            NotificationLevelId = (int)ActorSubscriptionNotificationLevelEnum.All,
            NotificationLevel = null!,
            SubscribedAt = DateTime.UtcNow
        });

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LightModerationFailureRetainsOriginalRunWithoutPersistingPrivateException(bool oversized)
    {
        var sink = new FanoutDiagnosticSink();
        string canary = Guid.CreateVersion7().ToString("N");
        var fault = new AudienceReadFailure(new InvalidOperationException(
            canary + (oversized ? new string('x', 8192) : string.Empty)));
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(sink);
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(fault));
        }));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
        var target = new EventBuilder().WithTenantId(publisher.TenantId)
            .WithActorId(publisher.OrganizationActorId).WithTitle(Guid.CreateVersion7().ToString("N"))
            .WithStatus(EventStatusEnum.Moderated).Build();
        database.Events.Add(target);
        var record = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(), publisher.TenantId, target.Id, publisher.UserId,
            "native-diagnostic", (int)EventStatusEnum.Published, null, DateTimeOffset.UtcNow);
        database.EventModerationRecords.Add(record);
        await database.SaveChangesAsync();
        var request = new EventLightModeratedNotificationFanoutRequested
        {
            TenantId = publisher.TenantId,
            EventId = target.Id,
            ModerationRecordId = record.Id,
            SourceActorId = publisher.OrganizationActorId,
            EventTitle = target.Title,
            ModeratedAt = record.CreatedAt
        };
        var fanout = scope.ServiceProvider.GetRequiredService<IEventModerationNotificationFanoutService>();
        sink.Arm();
        fault.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fanout.FanoutLightModerationAsync(request));
        await Assert.That(fault.Armed).IsFalse();
        database.ChangeTracker.Clear();
        var failed = await database.NotificationFanoutRuns.SingleAsync(value =>
            value.EntityId == record.Id && value.FanoutKind == EventModerationNotificationFanoutService.LightFanoutKind);
        await Assert.That(failed.Status).IsEqualTo(EventModerationNotificationFanoutService.StatusFailed);
        await Assert.That(failed.LastError).IsEqualTo("light_moderation_fanout_failed");
        await Assert.That(failed.FailedAt).IsNotNull();
        await Assert.That(failed.CompletedAt).IsNull();
        await Assert.That(failed.ProcessedCount).IsEqualTo(0);
        await Assert.That(await database.Notifications.AnyAsync(value => value.EntityId == target.Id.ToString())).IsFalse();
        var diagnostics = sink.FromProducer<EventModerationNotificationFanoutService>();
        await Assert.That(diagnostics.Length > 0).IsTrue();
        foreach (var entry in diagnostics)
        {
            await Assert.That(entry.Exception).IsNull();
            await Assert.That(entry.RenderMessage()).DoesNotContain(canary);
            await Assert.That(string.Join('\n', entry.Properties.Select(value => $"{value.Key}={value.Value}")))
                .DoesNotContain(canary);
        }
        await fanout.FanoutLightModerationAsync(request);
        database.ChangeTracker.Clear();
        var replay = await database.NotificationFanoutRuns.SingleAsync(value => value.EntityId == record.Id);
        await Assert.That(replay.Id).IsEqualTo(failed.Id);
        await Assert.That(replay.Status).IsEqualTo(EventModerationNotificationFanoutService.StatusCompleted);
        await Assert.That(replay.LastError).IsNull();
        await Assert.That(replay.FailedAt).IsNull();
        await Assert.That(replay.CompletedAt).IsNotNull();
        await Assert.That((await database.EventModerationRecords.SingleAsync(value => value.Id == record.Id)).AllowsUnmoderation)
            .IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicationFailureRetainsOriginalRunWithoutPersistingPrivateException(bool oversized)
    {
        var sink = new FanoutDiagnosticSink();
        string canary = Guid.CreateVersion7().ToString("N");
        var fault = new AudienceReadFailure(new InvalidOperationException(
            canary + (oversized ? new string('x', 8192) : string.Empty)), "actor_subscriptions");
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(sink);
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(fault));
        }));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
        var target = new EventBuilder().WithTenantId(publisher.TenantId)
            .WithActorId(publisher.OrganizationActorId).WithTitle(Guid.CreateVersion7().ToString("N"))
            .WithStatus(EventStatusEnum.Published).Build();
        database.Events.Add(target);
        await database.SaveChangesAsync();
        var request = new EventPublishedNotificationFanoutRequested
        {
            TenantId = publisher.TenantId,
            EventId = target.Id,
            SourceActorId = publisher.OrganizationActorId,
            EventTitle = target.Title,
            StartDate = DateTimeOffset.UtcNow.AddDays(1),
            PublishedAt = DateTimeOffset.UtcNow
        };
        var fanout = scope.ServiceProvider.GetRequiredService<IEventPublishedNotificationFanoutService>();
        sink.Arm();
        fault.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fanout.FanoutAsync(request));
        await Assert.That(fault.Armed).IsFalse();
        database.ChangeTracker.Clear();
        var failed = await database.NotificationFanoutRuns.SingleAsync(value =>
            value.EntityId == target.Id && value.FanoutKind == EventPublishedNotificationFanoutService.FanoutKind);
        await Assert.That(failed.Status).IsEqualTo(EventPublishedNotificationFanoutService.StatusFailed);
        await Assert.That(failed.LastError).IsEqualTo("event_publication_fanout_failed");
        await Assert.That(failed.FailedAt).IsNotNull();
        await Assert.That(failed.CompletedAt).IsNull();
        await Assert.That(failed.ProcessedCount).IsEqualTo(0);
        await Assert.That(await database.Notifications.AnyAsync(value => value.EntityId == target.Id.ToString())).IsFalse();
        var diagnostics = sink.FromProducer<EventPublishedNotificationFanoutService>();
        await Assert.That(diagnostics.Length > 0).IsTrue();
        foreach (var entry in diagnostics)
        {
            await Assert.That(entry.Exception).IsNull();
            await Assert.That(entry.RenderMessage()).DoesNotContain(canary);
            await Assert.That(string.Join('\n', entry.Properties.Select(value => $"{value.Key}={value.Value}")))
                .DoesNotContain(canary);
        }
        await fanout.FanoutAsync(request);
        database.ChangeTracker.Clear();
        var replay = await database.NotificationFanoutRuns.SingleAsync(value => value.EntityId == target.Id);
        await Assert.That(replay.Id).IsEqualTo(failed.Id);
        await Assert.That(replay.Status).IsEqualTo(EventPublishedNotificationFanoutService.StatusCompleted);
        await Assert.That(replay.LastError).IsNull();
        await Assert.That(replay.FailedAt).IsNull();
        await Assert.That(replay.CompletedAt).IsNotNull();
    }

    private sealed class AudienceReadFailure(Exception failure, string relation = "registration_orders") : DbCommandInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains(relation, StringComparison.Ordinal))
            {
                Armed = false;
                throw failure;
            }
            return ValueTask.FromResult(result);
        }
    }

    private static bool HasScalar(LogEvent entry, string field, object expected) =>
        entry.Properties.TryGetValue(field, out var property)
        && property is ScalarValue scalar && Equals(scalar.Value, expected);

    private static async Task AssertPrivateCopiesAbsentAsync(FanoutDiagnosticSink sink, Guid?[] ids, string title)
    {
        string exported = string.Join('\n', sink.Entries.Select(entry =>
            $"{entry.RenderMessage()}\n{entry.Exception}\n{string.Join('\n', entry.Properties.Select(property => $"{property.Key}={property.Value}"))}"));
        await Assert.That(exported).DoesNotContain(title);
        foreach (Guid id in ids.OfType<Guid>())
        {
            await Assert.That(exported).DoesNotContain(id.ToString("D"));
            await Assert.That(exported).DoesNotContain(id.ToString("N"));
        }
    }

    private sealed class FanoutDiagnosticSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _entries = new();
        private bool _armed;
        public LogEvent[] Entries => _entries.ToArray();

        public void Arm()
        {
            _entries.Clear();
            _armed = true;
        }

        public void Emit(LogEvent logEvent)
        {
            if (_armed)
                _entries.Enqueue(logEvent);
        }

        public LogEvent[] FromProducer<T>() => Entries.Where(entry =>
            HasScalar(entry, "SourceContext", typeof(T).FullName!)).ToArray();
    }
}
