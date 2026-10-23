using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ActorSubscriptions.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task SubscribeCannotReuseTrackedStateForAnAdministrativeBlock()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var subscription = await SeedStateSubscription(db, data.OutsiderId, ActorSubscriptionStatusEnum.Blocked);
        subscription.StatusId = (int)ActorSubscriptionStatusEnum.Active;
        subscription.IsDeleted = false;
        var handler = ActivatorUtilities.CreateInstance<SubscribeToActorCommandHandler>(scope.ServiceProvider);

        var result = await handler.ExecuteAsync(new()
        {
            Subscription = new() { TargetActorId = subscription.TargetActorId }
        }, default);

        await Assert.That(result.IsSuccess).IsFalse();
        var stored = await db.ActorSubscriptions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking().SingleAsync(row => row.Id == subscription.Id);
        await Assert.That(stored.StatusId).IsEqualTo((int)ActorSubscriptionStatusEnum.Blocked);
        await Assert.That(stored.IsDeleted).IsFalse();
    }

    [Test]
    public async Task UnsubscribeAndResubscribeCannotRemoveAnAdministrativeBlock()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var subscription = await SeedStateSubscription(db, data.OutsiderId, ActorSubscriptionStatusEnum.Blocked);
        var unsubscribe = ActivatorUtilities.CreateInstance<UnsubscribeFromActorCommandHandler>(scope.ServiceProvider);
        var subscribe = ActivatorUtilities.CreateInstance<SubscribeToActorCommandHandler>(scope.ServiceProvider);

        var removed = await unsubscribe.ExecuteAsync(new()
        {
            Subscription = new()
            {
                TargetActorId = subscription.TargetActorId,
                ExpectedConcurrencyStamp = subscription.ConcurrencyStamp
            }
        }, default);
        var restored = await subscribe.ExecuteAsync(new()
        {
            Subscription = new() { TargetActorId = subscription.TargetActorId }
        }, default);

        await Assert.That(removed.IsSuccess).IsFalse();
        await Assert.That(restored.IsSuccess).IsFalse();
        var rows = await db.ActorSubscriptions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking().Where(row => row.SubscriberTenantUserId == subscription.SubscriberTenantUserId
                && row.TargetActorId == subscription.TargetActorId).ToListAsync();
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].StatusId).IsEqualTo((int)ActorSubscriptionStatusEnum.Blocked);
    }

    [Test]
    public async Task RetiredSubscriptionCannotSatisfyCurrentStateLookup()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var subscription = await SeedStateSubscription(db, data.OutsiderId,
            ActorSubscriptionStatusEnum.Active, retired: true);
        subscription.IsDeleted = false;
        var repository = scope.ServiceProvider.GetRequiredService<IActorSubscriptionRepository>();

        await Assert.That(await repository.GetBySubscriberAndTargetAsync(
            subscription.TenantId, subscription.SubscriberTenantUserId, subscription.TargetActorId)).IsNull();
    }

    [Test]
    public async Task SubscriberCanCreateANewRowAfterRetirement()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var retired = await SeedStateSubscription(db, data.OutsiderId,
            ActorSubscriptionStatusEnum.Active, retired: true);
        db.ChangeTracker.Clear();
        var handler = ActivatorUtilities.CreateInstance<SubscribeToActorCommandHandler>(scope.ServiceProvider);

        var result = await handler.ExecuteAsync(new()
        {
            Subscription = new() { TargetActorId = retired.TargetActorId }
        }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsNotEqualTo(retired.Id);
        var rows = await db.ActorSubscriptions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking().Where(row => row.SubscriberTenantUserId == retired.SubscriberTenantUserId
                && row.TargetActorId == retired.TargetActorId).ToListAsync();
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows.Single(row => row.Id == retired.Id).IsDeleted).IsTrue();
    }

    [Test]
    public async Task SelfServiceSubscriptionReadsCannotBorrowAnotherTenantLease()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        ActorSubscription foreign;
        await using (var db = await scope.ServiceProvider
                         .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync())
        {
            db.TenantContext = new AuthorityTenant(data.ForeignTenantId);
            foreign = await SeedStateSubscription(db, data.OutsiderId, ActorSubscriptionStatusEnum.Active);
            var selected = await new ActorSubscriptionRepository(db).GetActiveFanoutBatchAsync(
                foreign.TenantId, foreign.TargetActorId, null, 10);
            await Assert.That(selected.Select(value => value.Id)).Contains(foreign.Id);
        }
        var repository = scope.ServiceProvider.GetRequiredService<IActorSubscriptionRepository>();

        await Assert.That(await repository.GetBySubscriberAndTargetAsync(
            foreign.TenantId, foreign.SubscriberTenantUserId, foreign.TargetActorId)).IsNull();
        await Assert.That(await repository.GetDiscoverableBySubscriberAndTargetAsync(
            foreign.TenantId, foreign.SubscriberTenantUserId, foreign.TargetActorId)).IsNull();
        var page = await repository.GetBySubscriberPagedAsync(
            foreign.TenantId, foreign.SubscriberTenantUserId, 1, 10);
        await Assert.That(page.Items).IsEmpty();
        await Assert.That(page.TotalCount).IsEqualTo(0);
        await Assert.That(await repository.GetActiveFanoutBatchAsync(
            foreign.TenantId, foreign.TargetActorId, null, 10)).IsEmpty();
    }

    [Test]
    public async Task InconsistentStoredRecipientCannotBecomeAReadOrFanoutTarget()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var subscription = await SeedStateSubscription(db, data.OutsiderId,
            ActorSubscriptionStatusEnum.Active, recipientUserId: data.OwnerId);
        var repository = scope.ServiceProvider.GetRequiredService<IActorSubscriptionRepository>();

        await Assert.That(await repository.GetBySubscriberAndTargetAsync(
            subscription.TenantId, subscription.SubscriberTenantUserId, subscription.TargetActorId)).IsNull();
        await Assert.That(await repository.GetDiscoverableBySubscriberAndTargetAsync(
            subscription.TenantId, subscription.SubscriberTenantUserId, subscription.TargetActorId)).IsNull();
        await Assert.That(await repository.GetActiveFanoutBatchAsync(
            subscription.TenantId, subscription.TargetActorId, null, 10)).IsEmpty();
    }

    [Test]
    public async Task MissingSubscriptionRetainsIdempotentUnsubscribeResult()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var handler = ActivatorUtilities.CreateInstance<UnsubscribeFromActorCommandHandler>(scope.ServiceProvider);

        var result = await handler.ExecuteAsync(new()
        {
            Subscription = new() { TargetActorId = Guid.CreateVersion7(), ExpectedConcurrencyStamp = Guid.CreateVersion7() }
        }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(Guid.Empty);
    }

    private static async Task<ActorSubscription> SeedStateSubscription(
        ExploreDbContext db, Guid userId, ActorSubscriptionStatusEnum status,
        bool retired = false, Guid? recipientUserId = null)
    {
        Guid tenantId = db.TenantContext!.TenantId;
        var member = await db.TenantUsers.SingleOrDefaultAsync(row => row.UserId == userId);
        if (member is null)
        {
            member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                UserId = userId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active
            };
            db.TenantUsers.Add(member);
        }
        var actor = new ActorBuilder().WithId(Guid.CreateVersion7())
            .WithActorType(ActorTypeEnum.Organization).WithDisplayName("Subscription state target").Build();
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "Subscription state target" },
            Actor = actor
        };
        actor.OrganizationId = organization.Id;
        actor.Organization = organization;
        db.Organizations.Add(organization);
        db.Actors.Add(actor);
        db.OrganizationTenants.Add(new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true
        });
        await db.SaveChangesAsync();
        var subscription = new ActorSubscription
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            SubscriberTenantUserId = member.Id,
            SubscriberTenantUser = member,
            SubscriberUserId = recipientUserId ?? userId,
            SubscriberUser = null!,
            TargetActorId = actor.Id,
            TargetActor = actor,
            TargetActorTypeId = (int)ActorTypeEnum.Organization,
            TargetActorType = null!,
            StatusId = (int)status,
            Status = null!,
            NotificationLevelId = (int)ActorSubscriptionNotificationLevelEnum.All,
            NotificationLevel = null!,
            ConcurrencyStamp = Guid.CreateVersion7(),
            SubscribedAt = DateTime.UtcNow,
            IsDeleted = retired
        };
        db.ActorSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription;
    }
}
