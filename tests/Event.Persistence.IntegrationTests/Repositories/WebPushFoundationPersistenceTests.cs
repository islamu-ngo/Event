// ABOUTME: PostgreSQL-backed tests for Web Push preference metadata, subscription persistence, and dispatch outbox transitions.
// ABOUTME: Proves tenant isolation, active uniqueness, idempotent claims, retry/dead-letter, lease recovery, and stale cleanup.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebPushFoundationPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task LookupSeeder_SeedsPushChannelAndDefaultsForEveryCategory()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();

        var channels = await context.NotificationPreferenceChannels
            .AsNoTracking()
            .OrderBy(channel => channel.SortOrder)
            .ToListAsync();
        var categories = await context.NotificationPreferenceCategories
            .AsNoTracking()
            .OrderBy(category => category.SortOrder)
            .ToListAsync();

        var push = channels.Single(channel => channel.MasterCode == NotificationPreferenceChannelCodes.Push);
        var requiredCategories = categories.Where(category => category.IsRequired).ToArray();
        var marketing = categories.Single(category => category.MasterCode == NotificationPreferenceCategoryCodes.Marketing);
        var productAnnouncements = categories.Single(category => category.MasterCode == NotificationPreferenceCategoryCodes.ProductAnnouncements);

        await Assert.That(push.Id).IsEqualTo((int)NotificationPreferenceChannelEnum.Push);
        await Assert.That(push.FullName).IsEqualTo("Browser Push");
        await Assert.That(requiredCategories.All(category => category.DefaultPushEnabled)).IsTrue();
        await Assert.That(marketing.DefaultPushEnabled).IsFalse();
        await Assert.That(productAnnouncements.DefaultPushEnabled).IsEqualTo(productAnnouncements.DefaultInAppEnabled);
    }

    [Test]
    public async Task Resolver_ReturnsRequiredAndDefaultPushDecisions()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("push-defaults");
        var user = CreateUser("push-defaults");
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var resolver = new NotificationPreferenceResolver(context);

        var decisions = await resolver.ResolveBatchAsync(
            [
                new NotificationPreferenceResolveRequest(tenant.Id, user.Id, null, null, NotificationPreferenceCategoryCodes.AccountSecurity, NotificationPreferenceChannelCodes.Push),
                new NotificationPreferenceResolveRequest(tenant.Id, user.Id, null, null, NotificationPreferenceCategoryCodes.EventUpdates, NotificationPreferenceChannelCodes.Push),
                new NotificationPreferenceResolveRequest(tenant.Id, user.Id, null, null, NotificationPreferenceCategoryCodes.Marketing, NotificationPreferenceChannelCodes.Push)
            ]);

        await Assert.That(decisions[0].IsEnabled).IsTrue();
        await Assert.That(decisions[0].IsRequired).IsTrue();
        await Assert.That(decisions[0].IsLocked).IsTrue();
        await Assert.That(decisions[1].IsEnabled).IsTrue();
        await Assert.That(decisions[1].EffectiveSourceScope).IsEqualTo("Default");
        await Assert.That(decisions[2].IsEnabled).IsFalse();
        await Assert.That(decisions[2].IsRequired).IsFalse();
    }

    [Test]
    public async Task SubscriptionRepository_UpsertsUserDeviceAndRejectsEndpointOwnedByAnotherDevice()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("push-subscription");
        var user = CreateUser("push-subscription");
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var repository = new WebPushSubscriptionRepository(context);
        var now = DateTime.UtcNow;

        var created = await repository.UpsertAsync(
            tenant.Id,
            user.Id,
            "device-1",
            "https://push.example/subscription/1",
            "p256dh-key",
            "auth-secret",
            null,
            now,
            CancellationToken.None);
        var updated = await repository.UpsertAsync(
            tenant.Id,
            user.Id,
            "device-1",
            "https://push.example/subscription/2",
            "p256dh-key-2",
            "auth-secret-2",
            now.AddDays(7),
            now.AddMinutes(1),
            CancellationToken.None);

        await Assert.That(updated.Id).IsEqualTo(created.Id);
        await Assert.That(updated.Endpoint).IsEqualTo("https://push.example/subscription/2");
        await Assert.That(updated.P256Dh).IsEqualTo("p256dh-key-2");
        await Assert.That(updated.LastSeenAt).IsEqualTo(now.AddMinutes(1));

        await Assert.That(async () => await repository.UpsertAsync(
                tenant.Id,
                user.Id,
                "device-2",
                "https://push.example/subscription/2",
                "p256dh-other",
                "auth-other",
                null,
                now.AddMinutes(2),
                CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SubscriptionRepository_ConcurrentSameOwnerSameEndpoint_ReturnsOneActiveSubscriptionForBothCalls()
    {
        await fixture.ResetAsync();
        await using (var seedContext = fixture.CreateDbContext())
        {
            var tenant = CreateTenant("push-concurrent");
            var user = CreateUser("push-concurrent");
            seedContext.Tenants.Add(tenant);
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();

            using var gate = new ConcurrentWebPushInsertGate(expectedCount: 2);
            await using var contextA = CreateDbContext(fixture.ConnectionString, gate);
            await using var contextB = CreateDbContext(fixture.ConnectionString, gate);
            var repositoryA = new WebPushSubscriptionRepository(contextA);
            var repositoryB = new WebPushSubscriptionRepository(contextB);
            var endpoint = $"https://push.example/concurrent/{Guid.NewGuid():N}";
            var now = DateTime.UtcNow;

            var first = repositoryA.UpsertAsync(tenant.Id, user.Id, "device-1", endpoint, "p256dh-a", "auth-a", null, now, CancellationToken.None);
            var second = repositoryB.UpsertAsync(tenant.Id, user.Id, "device-1", endpoint, "p256dh-b", "auth-b", now.AddDays(7), now.AddSeconds(1), CancellationToken.None);
            var results = await Task.WhenAll(first, second);

            await Assert.That(results.Select(result => result.Id).Distinct()).Count().IsEqualTo(1);
            var activeRows = await seedContext.WebPushSubscriptions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(row => row.Endpoint == endpoint && row.IsActive)
                .ToArrayAsync();
            await Assert.That(activeRows).Count().IsEqualTo(1);
            await Assert.That(activeRows[0].TenantId).IsEqualTo(tenant.Id);
            await Assert.That(activeRows[0].UserId).IsEqualTo(user.Id);
            await Assert.That(activeRows[0].DeviceIdentifier).IsEqualTo("device-1");
            var latestSeen = results.Max(result => result.LastSeenAt);
            await Assert.That(activeRows[0].LastSeenAt).IsBetween(latestSeen.AddMilliseconds(-1), latestSeen.AddMilliseconds(1));
        }
    }

    [Test]
    public async Task SubscriptionRepository_EndpointCollisionOwnedByAnotherUser_RemainsControlledFailure()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("push-owner-a");
        var otherTenant = CreateTenant("push-owner-b");
        var user = CreateUser("push-owner-a");
        var otherUser = CreateUser("push-owner-b");
        context.Tenants.AddRange(tenant, otherTenant);
        context.Users.AddRange(user, otherUser);
        await context.SaveChangesAsync();
        var repository = new WebPushSubscriptionRepository(context);
        var endpoint = $"https://push.example/cross-owner/{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;

        await repository.UpsertAsync(otherTenant.Id, otherUser.Id, "device-other", endpoint, "p256dh-other", "auth-other", null, now, CancellationToken.None);

        await Assert.That(async () => await repository.UpsertAsync(
                tenant.Id,
                user.Id,
                "device-1",
                endpoint,
                "p256dh-user",
                "auth-user",
                null,
                now.AddSeconds(1),
                CancellationToken.None))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("already owned");
    }

    [Test]
    public async Task TenantFilter_HidesWebPushSubscriptionAndOutboxRowsFromOtherTenants()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();
        var tenantA = CreateTenant("push-filter-a");
        var tenantB = CreateTenant("push-filter-b");
        var user = CreateUser("push-filter");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        seedContext.Users.Add(user);
        await seedContext.SaveChangesAsync();
        var subA = WebPushSubscription.Create(tenantA.Id, user.Id, "device-a", "https://push.example/a", "key-a", "auth-a", null, DateTime.UtcNow);
        var subB = WebPushSubscription.Create(tenantB.Id, user.Id, "device-b", "https://push.example/b", "key-b", "auth-b", null, DateTime.UtcNow);
        seedContext.WebPushSubscriptions.AddRange(subA, subB);
        seedContext.WebPushDispatchOutbox.AddRange(CreateDispatch(tenantA.Id, subA.Id, user.Id), CreateDispatch(tenantB.Id, subB.Id, user.Id));
        await seedContext.SaveChangesAsync();

        await using var tenantAContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));

        var visibleSubscriptions = await tenantAContext.WebPushSubscriptions.AsNoTracking().ToListAsync();
        var visibleOutboxRows = await tenantAContext.WebPushDispatchOutbox.AsNoTracking().ToListAsync();

        await Assert.That(visibleSubscriptions).Count().IsEqualTo(1);
        await Assert.That(visibleSubscriptions[0].TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(visibleOutboxRows).Count().IsEqualTo(1);
        await Assert.That(visibleOutboxRows[0].TenantId).IsEqualTo(tenantA.Id);
    }

    [Test]
    public async Task DispatchRepository_ClaimsCompletesRetriesDeadLettersAndRecoversStaleLeases()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("push-outbox");
        var user = CreateUser("push-outbox");
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var subscription = WebPushSubscription.Create(tenant.Id, user.Id, "device-1", "https://push.example/outbox", "key", "auth", null, DateTime.UtcNow);
        context.WebPushSubscriptions.Add(subscription);
        var dispatch = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        context.WebPushDispatchOutbox.Add(dispatch);
        await context.SaveChangesAsync();
        var repository = new WebPushDispatchOutboxRepository(context);
        var now = DateTime.UtcNow;
        var leaseToken = Guid.CreateVersion7();

        var pending = await repository.GetPendingBatch(10, now, CancellationToken.None);
        var claimed = await repository.TryMarkAsProcessing(dispatch.Id, leaseToken, now, CancellationToken.None);
        var duplicateClaim = await repository.TryMarkAsProcessing(dispatch.Id, Guid.CreateVersion7(), now, CancellationToken.None);
        var activeClaim = await repository.GetActiveClaimAsync(tenant.Id, dispatch.Id, leaseToken, CancellationToken.None);
        var wrongTenantClaim = await repository.GetActiveClaimAsync(Guid.CreateVersion7(), dispatch.Id, leaseToken, CancellationToken.None);
        var staleLeaseClaim = await repository.GetActiveClaimAsync(tenant.Id, dispatch.Id, Guid.CreateVersion7(), CancellationToken.None);
        var staleDelivery = await repository.MarkAsDelivered(dispatch.Id, Guid.CreateVersion7(), now.AddSeconds(1), CancellationToken.None);
        var delivery = await repository.MarkAsDelivered(dispatch.Id, leaseToken, now.AddSeconds(1), CancellationToken.None);
        var delivered = await context.WebPushDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);

        var retry = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        var exhausted = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        var stale = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        stale.Status = WebPushDispatchStatus.Processing;
        stale.ProcessingStartedAt = now.AddMinutes(-30);
        stale.ProcessingLeaseToken = Guid.CreateVersion7();
        context.WebPushDispatchOutbox.AddRange(retry, exhausted, stale);
        await context.SaveChangesAsync();
        var retryLeaseToken = Guid.CreateVersion7();
        await repository.TryMarkAsProcessing(retry.Id, retryLeaseToken, now.AddSeconds(2), CancellationToken.None);
        var staleRetryFailure = await repository.MarkAsFailed(retry.Id, Guid.CreateVersion7(), "push_service_unavailable", "temporary", true, TimeSpan.FromMinutes(5), 5, now.AddSeconds(3), CancellationToken.None);
        var retryFailure = await repository.MarkAsFailed(retry.Id, retryLeaseToken, "push_service_unavailable", "temporary", true, TimeSpan.FromMinutes(5), 5, now.AddSeconds(3), CancellationToken.None);
        var exhaustedLeaseToken = Guid.CreateVersion7();
        await repository.TryMarkAsProcessing(exhausted.Id, exhaustedLeaseToken, now.AddSeconds(4), CancellationToken.None);
        var exhaustedFailure = await repository.MarkAsFailed(exhausted.Id, exhaustedLeaseToken, "invalid_payload", "permanent", false, TimeSpan.Zero, 5, now.AddSeconds(5), CancellationToken.None);
        var recovered = await repository.RecoverStaleProcessing(now.AddMinutes(-10), now.AddSeconds(6), "lease_timeout", "Processing lease expired.", 10, CancellationToken.None);

        var rows = await context.WebPushDispatchOutbox.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(row => row.Id);
        await Assert.That(pending.Select(row => row.Id)).Contains(dispatch.Id);
        await Assert.That(claimed).IsTrue();
        await Assert.That(duplicateClaim).IsFalse();
        await Assert.That(activeClaim).IsNotNull();
        await Assert.That(activeClaim!.TenantId).IsEqualTo(tenant.Id);
        await Assert.That(activeClaim.UserId).IsEqualTo(user.Id);
        await Assert.That(activeClaim.SubscriptionId).IsEqualTo(subscription.Id);
        await Assert.That(wrongTenantClaim).IsNull();
        await Assert.That(staleLeaseClaim).IsNull();
        await Assert.That(staleDelivery).IsFalse();
        await Assert.That(delivery).IsTrue();
        await Assert.That(delivered.Status).IsEqualTo(WebPushDispatchStatus.Delivered);
        await Assert.That(staleRetryFailure).IsFalse();
        await Assert.That(retryFailure).IsTrue();
        await Assert.That(rows[retry.Id].Status).IsEqualTo(WebPushDispatchStatus.RetryScheduled);
        await Assert.That(rows[retry.Id].NextAttemptAt).IsNotNull();
        await Assert.That(exhaustedFailure).IsTrue();
        await Assert.That(rows[exhausted.Id].Status).IsEqualTo(WebPushDispatchStatus.DeadLettered);
        await Assert.That(recovered).IsEqualTo(1);
        await Assert.That(rows[stale.Id].Status).IsEqualTo(WebPushDispatchStatus.RetryScheduled);
        await Assert.That(rows[stale.Id].ProcessingLeaseToken).IsNull();
    }

    [Test]
    public async Task DispatchRepository_CreateIfNotExistsAsync_RepairsMissingDispatchAndSkipsExistingNotificationSubscription()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("push-repair");
        var user = CreateUser("push-repair");
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var subscription = WebPushSubscription.Create(tenant.Id, user.Id, "device-1", "https://push.example/repair", "key", "auth", null, DateTime.UtcNow);
        context.WebPushSubscriptions.Add(subscription);
        await context.SaveChangesAsync();
        var repository = new WebPushDispatchOutboxRepository(context);
        var notificationId = Guid.CreateVersion7();
        var dispatch = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        dispatch.NotificationId = notificationId;
        var duplicate = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        duplicate.NotificationId = notificationId;

        var created = await repository.CreateIfNotExistsAsync(dispatch, CancellationToken.None);
        var skipped = await repository.CreateIfNotExistsAsync(duplicate, CancellationToken.None);

        var rows = await context.WebPushDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == tenant.Id && row.NotificationId == notificationId && row.SubscriptionId == subscription.Id)
            .ToListAsync();
        await Assert.That(created).IsTrue();
        await Assert.That(skipped).IsFalse();
        await Assert.That(rows).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DispatchRepository_TerminalStaleCleanupMarksDispatchAndDeactivatesSubscriptionAtomically()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("push-stale");
        var user = CreateUser("push-stale");
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var subscription = WebPushSubscription.Create(tenant.Id, user.Id, "device-1", "https://push.example/stale", "key", "auth", null, DateTime.UtcNow);
        context.WebPushSubscriptions.Add(subscription);
        var dispatch = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        var leaseToken = Guid.CreateVersion7();
        dispatch.Status = WebPushDispatchStatus.Processing;
        dispatch.ProcessingStartedAt = DateTime.UtcNow.AddSeconds(-5);
        dispatch.ProcessingLeaseToken = leaseToken;
        context.WebPushDispatchOutbox.Add(dispatch);
        await context.SaveChangesAsync();
        var repository = new WebPushDispatchOutboxRepository(context);
        var failedAt = DateTime.UtcNow;

        var cleaned = await repository.MarkPermanentFailureAndDeactivateSubscription(
            tenant.Id,
            dispatch.Id,
            leaseToken,
            subscription.Id,
            "gone_410",
            "Push service returned 410 Gone.",
            failedAt,
            CancellationToken.None);

        var dispatchRow = await context.WebPushDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        var subscriptionRow = await context.WebPushSubscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == subscription.Id);
        await Assert.That(cleaned).IsTrue();
        await Assert.That(dispatchRow.Status).IsEqualTo(WebPushDispatchStatus.PermanentFailed);
        await Assert.That(dispatchRow.LastFailureCategory).IsEqualTo("gone_410");
        await Assert.That(subscriptionRow.IsActive).IsFalse();
        await Assert.That(subscriptionRow.DeactivatedAt).IsNotNull();
        await Assert.That(subscriptionRow.DeactivatedAt!.Value).IsBetween(failedAt.AddSeconds(-1), failedAt.AddSeconds(1));
        await Assert.That(subscriptionRow.DeactivationReason).IsEqualTo("gone_410");
    }

    [Test]
    public async Task DispatchRepository_RejectsStaleLeaseCleanupButCommitsAlreadyInactiveSameTenantCleanup()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = CreateTenant("push-cleanup-a");
        var otherTenant = CreateTenant("push-cleanup-b");
        var user = CreateUser("push-cleanup");
        context.Tenants.AddRange(tenant, otherTenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var subscription = WebPushSubscription.Create(tenant.Id, user.Id, "device-1", "https://push.example/inactive", "key", "auth", null, DateTime.UtcNow);
        subscription.IsActive = false;
        subscription.DeactivatedAt = DateTime.UtcNow.AddMinutes(-1);
        subscription.DeactivationReason = "previous_gone_410";
        context.WebPushSubscriptions.Add(subscription);
        var dispatch = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        var leaseToken = Guid.CreateVersion7();
        dispatch.Status = WebPushDispatchStatus.Processing;
        dispatch.ProcessingStartedAt = DateTime.UtcNow.AddSeconds(-5);
        dispatch.ProcessingLeaseToken = leaseToken;
        context.WebPushDispatchOutbox.Add(dispatch);
        await context.SaveChangesAsync();
        var repository = new WebPushDispatchOutboxRepository(context);
        var failedAt = DateTime.UtcNow;

        var staleLeaseCleaned = await repository.MarkPermanentFailureAndDeactivateSubscription(
            tenant.Id,
            dispatch.Id,
            Guid.CreateVersion7(),
            subscription.Id,
            "gone_410",
            "stale worker",
            failedAt,
            CancellationToken.None);
        var crossTenantCleaned = await repository.MarkPermanentFailureAndDeactivateSubscription(
            otherTenant.Id,
            dispatch.Id,
            leaseToken,
            subscription.Id,
            "gone_410",
            "wrong tenant",
            failedAt,
            CancellationToken.None);
        var repeatedCleanup = await repository.MarkPermanentFailureAndDeactivateSubscription(
            tenant.Id,
            dispatch.Id,
            leaseToken,
            subscription.Id,
            "gone_410",
            "same tenant already inactive",
            failedAt,
            CancellationToken.None);

        var dispatchRow = await context.WebPushDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        var subscriptionRow = await context.WebPushSubscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == subscription.Id);
        await Assert.That(staleLeaseCleaned).IsFalse();
        await Assert.That(crossTenantCleaned).IsFalse();
        await Assert.That(repeatedCleanup).IsTrue();
        await Assert.That(dispatchRow.Status).IsEqualTo(WebPushDispatchStatus.PermanentFailed);
        await Assert.That(subscriptionRow.IsActive).IsFalse();
        await Assert.That(subscriptionRow.DeactivationReason).IsEqualTo("previous_gone_410");
    }

    [Test]
    public async Task DispatchRepository_SubscriptionFailureRollsBackTheTerminalDispatch()
    {
        await fixture.ResetAsync();
        var tenant = CreateTenant("push-cleanup-rollback");
        var user = CreateUser("push-cleanup-rollback");
        DateTime failedAt = DateTime.UtcNow;
        WebPushSubscription subscription;
        WebPushDispatchOutbox dispatch;
        Guid leaseToken = Guid.CreateVersion7();
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            setup.AddRange(tenant, user);
            await setup.SaveChangesAsync();
            subscription = WebPushSubscription.Create(
                tenant.Id, user.Id, "device-rollback", "https://push.example/rollback",
                "key", "auth", null, failedAt);
            setup.WebPushSubscriptions.Add(subscription);
            dispatch = CreateDispatch(tenant.Id, subscription.Id, user.Id);
            dispatch.Status = WebPushDispatchStatus.Processing;
            dispatch.ProcessingStartedAt = failedAt.AddSeconds(-5);
            dispatch.ProcessingLeaseToken = leaseToken;
            setup.WebPushDispatchOutbox.Add(dispatch);
            await setup.SaveChangesAsync();
        }

        await using (ExploreDbContext failing = fixture.CreateDbContext(new RejectSubscriptionUpdate()))
        {
            await Assert.That(failing.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
            var repository = new WebPushDispatchOutboxRepository(failing);
            await Assert.That(async () => await repository.MarkPermanentFailureAndDeactivateSubscription(
                    tenant.Id, dispatch.Id, leaseToken, subscription.Id,
                    "gone_410", "Push endpoint retired.", failedAt))
                .Throws<SubscriptionUpdateFailure>();
        }

        await using ExploreDbContext verification = fixture.CreateDbContext();
        var persistedDispatch = await verification.WebPushDispatchOutbox.AsNoTracking()
            .SingleAsync(row => row.Id == dispatch.Id);
        var persistedSubscription = await verification.WebPushSubscriptions.AsNoTracking()
            .SingleAsync(row => row.Id == subscription.Id);
        await Assert.That(persistedDispatch.Status).IsEqualTo(WebPushDispatchStatus.Processing);
        await Assert.That(persistedDispatch.ProcessingLeaseToken).IsEqualTo(leaseToken);
        await Assert.That(persistedDispatch.PermanentFailedAt).IsNull();
        await Assert.That(persistedSubscription.IsActive).IsTrue();
        await Assert.That(persistedSubscription.DeactivatedAt).IsNull();
    }

    private sealed class SubscriptionUpdateFailure : Exception;

    private sealed class RejectSubscriptionUpdate : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("web_push_subscriptions", StringComparison.Ordinal))
            {
                throw new SubscriptionUpdateFailure();
            }

            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public async Task CurrentBaseline_PersistsIntendedPushDefaults()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var categories = await context.NotificationPreferenceCategories.AsNoTracking().ToArrayAsync();

        await Assert.That(categories
            .Where(category => category.MasterCode != NotificationPreferenceCategoryCodes.Marketing)
            .All(category => category.DefaultPushEnabled == category.DefaultInAppEnabled)).IsTrue();
        await Assert.That(categories.Single(category =>
            category.MasterCode == NotificationPreferenceCategoryCodes.Marketing).DefaultPushEnabled).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task DispatchRepository_PartialCleanupFailurePreservesAtomicityAndAllowsRecovery(int failureMode)
    {
        await fixture.ResetAsync();
        await using var setup = fixture.CreateDbContext();
        var tenant = CreateTenant("push-recovery");
        var user = CreateUser("push-recovery");
        setup.Tenants.Add(tenant);
        setup.Users.Add(user);
        await setup.SaveChangesAsync();
        var subscription = WebPushSubscription.Create(tenant.Id, user.Id, "device-1",
            "https://push.example/recovery", "key", "auth", null, DateTime.UtcNow);
        setup.WebPushSubscriptions.Add(subscription);
        var dispatch = CreateDispatch(tenant.Id, subscription.Id, user.Id);
        var leaseToken = Guid.CreateVersion7();
        dispatch.Status = WebPushDispatchStatus.Processing;
        dispatch.ProcessingStartedAt = DateTime.UtcNow.AddSeconds(-5);
        dispatch.ProcessingLeaseToken = leaseToken;
        setup.WebPushDispatchOutbox.Add(dispatch);
        await setup.SaveChangesAsync();

        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var failure = new FailAfterFirstCleanupWrite(failureMode, request);
        await using var context = fixture.CreateDbContext(failure);
        var repository = new WebPushDispatchOutboxRepository(context);
        var failedAt = DateTime.UtcNow;
        Task<bool> Attempt(CancellationToken token) => repository.MarkPermanentFailureAndDeactivateSubscription(
            tenant.Id, dispatch.Id, leaseToken, subscription.Id, "gone_410", "Synthetic cleanup failure.", failedAt, token);

        if (failureMode == 0)
        {
            await Assert.That(await Attempt(request.Token)).IsTrue();
        }
        else
        {
            if (failureMode == 1)
            {
                await Assert.That(() => Attempt(request.Token)).Throws<InvalidOperationException>();
            }
            else
            {
                await Assert.That(() => Attempt(request.Token)).Throws<OperationCanceledException>();
            }

            await using var durable = fixture.CreateDbContext();
            var unchangedDispatch = await durable.WebPushDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(row => row.Id == dispatch.Id);
            var unchangedSubscription = await durable.WebPushSubscriptions.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(row => row.Id == subscription.Id);
            await Assert.That(unchangedDispatch.Status).IsEqualTo(WebPushDispatchStatus.Processing);
            await Assert.That(unchangedDispatch.ProcessingLeaseToken).IsEqualTo(leaseToken);
            await Assert.That(unchangedDispatch.PermanentFailedAt).IsNull();
            await Assert.That(unchangedSubscription.IsActive).IsTrue();
            await Assert.That(unchangedSubscription.DeactivationReason).IsNull();

            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Assert.That(await Attempt(recovery.Token)).IsTrue();
        }

        await Assert.That(failure.WasInjected).IsTrue();
        await using var verify = fixture.CreateDbContext();
        var dispatchRow = await verify.WebPushDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == dispatch.Id);
        var subscriptionRow = await verify.WebPushSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == subscription.Id);
        await Assert.That(dispatchRow.Status).IsEqualTo(WebPushDispatchStatus.PermanentFailed);
        await Assert.That(dispatchRow.ProcessingLeaseToken).IsNull();
        await Assert.That(dispatchRow.LastFailureCategory).IsEqualTo("gone_410");
        await Assert.That(subscriptionRow.IsActive).IsFalse();
        await Assert.That(subscriptionRow.DeactivationReason).IsEqualTo("gone_410");
        await Assert.That(await Attempt(CancellationToken.None)).IsFalse();
        await Assert.That(await verify.WebPushDispatchOutbox.IgnoreQueryFilters()
            .CountAsync(row => row.Id == dispatch.Id)).IsEqualTo(1);
    }

    private sealed class FailAfterFirstCleanupWrite(int failureMode, CancellationTokenSource request)
        : DbCommandInterceptor
    {
        private bool _failed;
        public bool WasInjected => _failed;

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (!_failed && eventData.CommandSource == CommandSource.ExecuteUpdate)
            {
                _failed = true;
                if (failureMode == 0)
                {
                    throw new TimeoutException("Synthetic transient cleanup failure after the dispatch write.");
                }
                if (failureMode == 1)
                {
                    throw new InvalidOperationException("Synthetic permanent cleanup failure after the dispatch write.");
                }
                request.Cancel();
                request.Token.ThrowIfCancellationRequested();
            }
            return ValueTask.FromResult(result);
        }
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            FullName = $"Web Push {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static User CreateUser(string emailPrefix)
    {
        return new User
        {
            Pii = new UserPii
            {
                Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Push",
                LastName = "Recipient",
            },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    private static WebPushDispatchOutbox CreateDispatch(Guid tenantId, Guid subscriptionId, Guid userId)
    {
        return new WebPushDispatchOutbox
        {
            TenantId = tenantId,
            Tenant = null!,
            NotificationId = Guid.CreateVersion7(),
            CategoryId = (int)NotificationPreferenceCategoryEnum.EventUpdates,
            Category = null!,
            SubscriptionId = subscriptionId,
            Subscription = null!,
            UserId = userId,
            User = null!,
            PayloadJson = $"{{\"notificationId\":\"{Guid.CreateVersion7()}\",\"route\":\"/notifications\"}}",
        };
    }

    private static ExploreDbContext CreateDbContext(string connectionString, SaveChangesInterceptor interceptor)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Persistence integration test system context.");
        return context;
    }

    private sealed class ConcurrentWebPushInsertGate(int expectedCount) : SaveChangesInterceptor, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private int _arrived;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<WebPushSubscription>().Any(entry => entry.State == EntityState.Added) == true
                && Interlocked.Increment(ref _arrived) >= expectedCount)
            {
                _release.Set();
            }

            if (Volatile.Read(ref _arrived) > 0)
            {
                _release.Wait(TimeSpan.FromSeconds(10), cancellationToken);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public void Dispose() => _release.Dispose();
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

}
