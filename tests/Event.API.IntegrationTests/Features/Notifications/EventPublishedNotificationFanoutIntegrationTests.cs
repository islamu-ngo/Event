using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features.Notifications;

[Category(TestCategories.Fast)]
[Category("NotificationFanout")]
[NotInParallel("AuthenticatedApiFixture")]
[ClassDataSource<AuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public class EventPublishedNotificationFanoutIntegrationTests(AuthenticatedApiTestFixture fixture)
{
    private readonly AuthenticatedApiTestFixture _fixture = fixture;

    [Test]
    public async Task InternalFanoutDispatch_WithActiveOrganizationSubscription_CreatesSingleNotificationAndCompletedRun()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var scenario = await SeedFanoutScenarioAsync(context);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>();
        var message = CreateFanoutOutboxMessage(scenario);

        await dispatcher.DispatchAsync(message);
        await dispatcher.DispatchAsync(message);

        var notifications = await context.Notifications
            .IgnoreQueryFilters()
            .Where(notification => notification.TenantId == scenario.TenantId
                && notification.UserId == scenario.SubscriberUserId
                && notification.NotificationEntityTypeId == (int)NotificationEntityTypeEnum.Event
                && notification.EntityId == scenario.EventId.ToString())
            .ToListAsync();

        await Assert.That(notifications).Count().IsEqualTo(1);
        var notification = notifications.Single();
        await Assert.That(notification.NotificationTypeId).IsEqualTo((int)NotificationTypeEnum.EventCreated);
        await Assert.That(notification.NotificationScopeId).IsEqualTo((int)ActorTypeEnum.Organization);
        await Assert.That(notification.SourceActorId).IsEqualTo(scenario.SourceActorId);
        await Assert.That(notification.RecipientContextActorId).IsEqualTo(scenario.SourceActorId);
        await Assert.That(notification.NotificationReasonId).IsEqualTo((int)NotificationReasonEnum.Subscription);
        await Assert.That(notification.DeduplicationKey).IsEqualTo(
            $"event-published:{scenario.TenantId:N}:{scenario.EventId:N}:{scenario.SubscriberTenantUserId:N}");

        var fanoutRun = await context.NotificationFanoutRuns
            .IgnoreQueryFilters()
            .SingleAsync(run => run.TenantId == scenario.TenantId
                && run.FanoutKind == EventPublishedNotificationFanoutService.FanoutKind
                && run.NotificationEntityTypeId == (int)NotificationEntityTypeEnum.Event
                && run.EntityId == scenario.EventId
                && run.SourceActorId == scenario.SourceActorId);

        await Assert.That(fanoutRun.Status).IsEqualTo(EventPublishedNotificationFanoutService.StatusCompleted);
        await Assert.That(fanoutRun.ProcessedCount).IsEqualTo(1);
        await Assert.That(fanoutRun.CreatedNotificationCount).IsEqualTo(1);
        await Assert.That(fanoutRun.CursorSubscriberTenantUserId).IsEqualTo(scenario.SubscriberTenantUserId);
        await Assert.That(fanoutRun.CompletedAt).IsNotNull();
    }

    [Test]
    public async Task InternalFanoutDispatch_WithFencedAndActiveSubscribers_CreatesOnlyActiveNotificationAndWebPushDispatch()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var scenario = await SeedFanoutScenarioAsync(context, includeFencedSubscriber: true);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>();
        var message = CreateFanoutOutboxMessage(scenario);

        await dispatcher.DispatchAsync(message);
        context.ChangeTracker.Clear();

        var notifications = await context.Notifications
            .IgnoreQueryFilters()
            .Where(notification => notification.TenantId == scenario.TenantId
                && (notification.UserId == scenario.SubscriberUserId
                    || notification.UserId == scenario.FencedSubscriberUserId))
            .ToListAsync();
        var dispatches = await context.WebPushDispatchOutbox
            .IgnoreQueryFilters()
            .Where(dispatch => dispatch.TenantId == scenario.TenantId
                && (dispatch.UserId == scenario.SubscriberUserId
                    || dispatch.UserId == scenario.FencedSubscriberUserId))
            .ToListAsync();
        var fanoutRun = await context.NotificationFanoutRuns
            .IgnoreQueryFilters()
            .SingleAsync(run => run.TenantId == scenario.TenantId
                && run.FanoutKind == EventPublishedNotificationFanoutService.FanoutKind
                && run.EntityId == scenario.EventId);

        await Assert.That(notifications.Select(notification => notification.UserId))
            .IsEquivalentTo([scenario.SubscriberUserId]);
        await Assert.That(dispatches.Select(dispatch => dispatch.UserId))
            .IsEquivalentTo([scenario.SubscriberUserId]);
        await Assert.That(fanoutRun.Status).IsEqualTo(EventPublishedNotificationFanoutService.StatusCompleted);
        await Assert.That(fanoutRun.ProcessedCount).IsEqualTo(2);
        await Assert.That(fanoutRun.CreatedNotificationCount).IsEqualTo(1);
    }

    private static OutboxMessage CreateFanoutOutboxMessage(FanoutScenario scenario)
    {
        var payload = new EventPublishedNotificationFanoutRequested
        {
            TenantId = scenario.TenantId,
            EventId = scenario.EventId,
            EventTitle = scenario.EventTitle,
            SourceActorId = scenario.SourceActorId,
            StartDate = scenario.StartDate,
            EndDate = scenario.EndDate,
            PublishedAt = DateTimeOffset.UtcNow
        };

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateType = "Event",
            AggregateId = scenario.EventId,
            EventType = PublishEventCommandHandler.EventPublishedNotificationFanoutRequestedEventType,
            Payload = JsonSerializer.Serialize(payload),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = 5
        };
    }

    private static async Task<FanoutScenario> SeedFanoutScenarioAsync(
        ExploreDbContext context,
        bool includeFencedSubscriber = false)
    {
        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.Id == PlatformDefaults.DefaultTenantId);
        if (tenant is null)
        {
            tenant = new TenantBuilder()
                .WithId(PlatformDefaults.DefaultTenantId)
                .WithFullName("Default Fanout Test Tenant")
                .WithSlug($"default-fanout-{Guid.NewGuid():N}")
                .Build();
            context.Tenants.Add(tenant);
        }

        var subscriber = new UserBuilder().Build();
        context.Users.Add(subscriber);
        User? fencedSubscriber = includeFencedSubscriber ? new UserBuilder().Build() : null;
        if (fencedSubscriber is not null)
        {
            context.Users.Add(fencedSubscriber);
        }
        await context.SaveChangesAsync();

        var sourceOrganizationId = Guid.CreateVersion7();
        var sourceOrganization = new Organization
        {
            Id = sourceOrganizationId,
            Pii = new OrganizationPii
            {
                OrganizationId = sourceOrganizationId,
                FullName = "Fanout Source Organization"
            },
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.Organizations.Add(sourceOrganization);
        await context.SaveChangesAsync();

        var subscriberActor = new ActorBuilder()
            .WithUserId(subscriber.Id)
            .WithDisplayName("Fanout Subscriber")
            .Build();
        var sourceActor = new ActorBuilder()
            .WithActorType(ActorTypeEnum.Organization)
            .WithDisplayName("Fanout Source Organization")
            .Build();
        sourceActor.OrganizationId = sourceOrganization.Id;
        Actor? fencedSubscriberActor = fencedSubscriber is null
            ? null
            : new ActorBuilder()
                .WithUserId(fencedSubscriber.Id)
                .WithDisplayName("Fenced Fanout Subscriber")
                .Build();
        context.Actors.AddRange(subscriberActor, sourceActor);
        if (fencedSubscriberActor is not null)
        {
            context.Actors.Add(fencedSubscriberActor);
        }
        await context.SaveChangesAsync();

        var tenantUser = new TenantUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = subscriber.Id,
            User = subscriber,
            ActorId = subscriberActor.Id,
            Actor = subscriberActor,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow
        };

        var subscription = new ActorSubscription
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tenant = null!,
            SubscriberTenantUserId = tenantUser.Id,
            SubscriberTenantUser = null!,
            SubscriberUserId = subscriber.Id,
            SubscriberUser = null!,
            TargetActorId = sourceActor.Id,
            TargetActor = null!,
            TargetActorTypeId = (int)ActorTypeEnum.Organization,
            TargetActorType = null!,
            StatusId = (int)ActorSubscriptionStatusEnum.Active,
            Status = null!,
            NotificationLevelId = (int)ActorSubscriptionNotificationLevelEnum.All,
            NotificationLevel = null!,
            SubscribedAt = DateTime.UtcNow
        };
        var sourceParticipation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            OrganizationId = sourceOrganization.Id,
            Organization = sourceOrganization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true,
            IsOrganizerEligible = true,
            ApprovedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        TenantUser? fencedTenantUser = fencedSubscriber is null || fencedSubscriberActor is null
            ? null
            : new TenantUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Tenant = tenant,
                UserId = fencedSubscriber.Id,
                User = fencedSubscriber,
                ActorId = fencedSubscriberActor.Id,
                Actor = fencedSubscriberActor,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow
            };
        ActorSubscription? fencedSubscription = fencedTenantUser is null || fencedSubscriber is null
            ? null
            : new ActorSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Tenant = null!,
                SubscriberTenantUserId = fencedTenantUser.Id,
                SubscriberTenantUser = null!,
                SubscriberUserId = fencedSubscriber.Id,
                SubscriberUser = null!,
                TargetActorId = sourceActor.Id,
                TargetActor = null!,
                TargetActorTypeId = (int)ActorTypeEnum.Organization,
                TargetActorType = null!,
                StatusId = (int)ActorSubscriptionStatusEnum.Active,
                Status = null!,
                NotificationLevelId = (int)ActorSubscriptionNotificationLevelEnum.All,
                NotificationLevel = null!,
                SubscribedAt = DateTime.UtcNow
            };

        var startDate = DateTimeOffset.UtcNow.AddDays(1);
        var endDate = startDate.AddHours(2);
        var @event = new EventBuilder()
            .WithTenantId(tenant.Id)
            .WithActorId(sourceActor.Id)
            .WithTitle("Fanout Integration Event")
            .WithStatus(EventStatusEnum.Published)
            .Build();
        @event.FirstSessionStartUtc = startDate;
        @event.LastSessionStartUtc = endDate;

        context.TenantUsers.Add(tenantUser);
        context.OrganizationTenants.Add(sourceParticipation);
        context.ActorSubscriptions.Add(subscription);
        context.WebPushSubscriptions.Add(WebPushSubscription.Create(
            tenant.Id,
            subscriber.Id,
            "active-subscriber-device",
            $"https://push.example/{subscriber.Id:N}",
            "p256dh-active",
            "auth-active",
            null,
            DateTime.UtcNow));
        if (fencedTenantUser is not null && fencedSubscription is not null && fencedSubscriber is not null)
        {
            context.TenantUsers.Add(fencedTenantUser);
            context.ActorSubscriptions.Add(fencedSubscription);
            context.WebPushSubscriptions.Add(WebPushSubscription.Create(
                tenant.Id,
                fencedSubscriber.Id,
                "fenced-subscriber-device",
                $"https://push.example/{fencedSubscriber.Id:N}",
                "p256dh-fenced",
                "auth-fenced",
                null,
                DateTime.UtcNow));
            context.PrivacyErasureSagas.Add(CreateFencedSaga(fencedSubscriber.Id));
        }
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return new FanoutScenario(
            tenant.Id,
            subscriber.Id,
            tenantUser.Id,
            sourceActor.Id,
            @event.Id,
            @event.Title,
            startDate,
            endDate,
            fencedSubscriber?.Id);
    }

    private sealed record FanoutScenario(
        Guid TenantId,
        Guid SubscriberUserId,
        Guid SubscriberTenantUserId,
        Guid SourceActorId,
        Guid EventId,
        string EventTitle,
        DateTimeOffset StartDate,
        DateTimeOffset? EndDate,
        Guid? FencedSubscriberUserId);

    private static PrivacyErasureSaga CreateFencedSaga(Guid userId)
    {
        DateTime now = DateTime.UtcNow;
        PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            authoritySequence: 1,
            PrivacyErasureSubjectKind.User,
            userId,
            PrivacyErasureReasonCode.AccountDeletion,
            policyVersion: 1,
            now,
            now);
        return PrivacyErasureSaga.Start(
            intent,
            fenceToken: 1,
            SHA256.HashData([1]),
            now.AddHours(1),
            now);
    }
}
