using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Persistence;
using Explore.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Event.Api.IntegrationTests.Authentication.TenantDelegationAuthenticationFixture;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class TenantDelegationAuthenticationTests
{
    [Test]
    public async Task GlobalSmtpFencePrecedesMixedPatchTransaction_AndNotificationsFollowCommit()
    {
        const string heldKey = GovernanceSettingKeys.Email.DeliveryEnabled;
        var notifications = new Notifications();
        var reachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(
            instanceAdministrator: true, tenantAdministrator: false,
            configureServices: services => ConfigureObservations(services, notifications, (key, _) =>
            {
                if (key == heldKey) reachedLock.TrySetResult();
                return Task.CompletedTask;
            }));
        await using (var seed = fixture.CreateIndependentContext())
        {
            await SeedSettingAsync(seed, GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"");
            await SeedSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockSmtp, "true");
        }

        await using var verification = fixture.CreateIndependentContext();
        verification.TenantContext = new SnapshotTenant(fixture.TenantId);
        var before = await SnapshotAsync(verification);
        await Assert.That(before.Count(row => row.TenantId == fixture.TenantId
            && row.Key is GovernanceSettingKeys.Email.SmtpHost or GovernanceSettingKeys.Email.FromAddress))
            .IsEqualTo(2);
        long revision = await RevisionAsync(verification);
        var publishedSnapshots = new List<SettingSnapshot[]>();
        notifications.OnPublishing = async _ =>
        {
            // Independent connection: an uncommitted writer's tracked values cannot satisfy this check.
            publishedSnapshots.Add(await SnapshotAsync(verification));
        };
        await using var holderContext = fixture.CreateIndependentContext();
        var holderLock = new RelationalSettingMutationLock(holderContext, new EfCoreUnitOfWork(holderContext));
        var holderOwnsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            using var transaction = new TransactionObserver(services.GetRequiredService<ExploreDbContext>());
            var handler = services.GetRequiredService<ICommandHandler<UpdateTenantDelegationSettingsCommand,
                BaseCommandResponse<Guid>>>();
            Task holder = holderLock.ExecuteOrderedGroupsAsync([[heldKey]], async token =>
            {
                holderOwnsLock.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                return true;
            }, cancellation.Token);
            Task<BaseCommandResponse<Guid>>? write = null;
            try
            {
                await holderOwnsLock.Task.WaitAsync(TimeSpan.FromSeconds(15));
                write = handler.ExecuteAsync(new UpdateTenantDelegationSettingsCommand
                {
                    UserId = fixture.Subject.PrincipalId,
                    Patch = new PatchTenantDelegationSettingsDto
                    {
                        AllowTenantSelfServiceRegistration = OptionalUpdate<bool>.Set(true),
                        AllowTenantWhiteLabeling = OptionalUpdate<bool>.Set(true),
                        DefaultPublicHomePage = OptionalUpdate<string?>.Set("LandingPage"),
                        LockTenantHomePagePreference = OptionalUpdate<bool>.Set(true),
                        LockTenantSmtp = OptionalUpdate<bool>.Set(false),
                        LockTenantStorage = OptionalUpdate<bool>.Set(false),
                        LockTenantAnalytics = OptionalUpdate<bool>.Set(false),
                        LockTenantAiAssistant = OptionalUpdate<bool>.Set(false)
                    }
                }, cancellation.Token);
                Task observed = await Task.WhenAny(reachedLock.Task, transaction.Started.Task, write)
                    .WaitAsync(TimeSpan.FromSeconds(15));
                await Assert.That(observed).IsSameReferenceAs(reachedLock.Task);
                await Assert.That(transaction.Started.Task.IsCompleted).IsFalse();
                await Assert.That(write.IsCompleted).IsFalse();
                await Assert.That(notifications.Items).IsEmpty();
                await Assert.That(await SnapshotAsync(verification)).IsEquivalentTo(before);
                await Assert.That(await RevisionAsync(verification)).IsEqualTo(revision);
            }
            finally
            {
                release.TrySetResult();
                cancellation.CancelAfter(TimeSpan.FromSeconds(15));
                Task[] pending = write is null ? [holder] : [holder, write];
                await Task.WhenAll(pending);
            }
            await Assert.That((await write!).IsSuccess).IsTrue();
            await Assert.That(transaction.Started.Task.IsCompletedSuccessfully).IsTrue();
            services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache();
            var settings = await services.GetRequiredService<IInstanceGovernanceSettingService>().ReadSettingsAsync();
            await Assert.That(settings.TenantDelegation.LockTenantSmtp).IsFalse();
            await Assert.That(settings.TenantDelegation.DefaultPublicHomePage).IsEqualTo("LandingPage");
            await Assert.That(settings.TenantDelegation.LockTenantHomePagePreference).IsTrue();
            await Assert.That(settings.TenantDelegation.AllowTenantSelfServiceRegistration).IsTrue();
            await Assert.That(settings.TenantDelegation.AllowTenantWhiteLabeling).IsTrue();
            await Assert.That(settings.TenantDelegation.LockTenantStorage).IsFalse();
            await Assert.That(settings.TenantDelegation.LockTenantAnalytics).IsFalse();
            await Assert.That(settings.TenantDelegation.LockTenantAiAssistant).IsFalse();
            await Assert.That((await services.GetRequiredService<ISystemSettingRepository>()
                .GetByKey(GovernanceSettingKeys.Tenants.SelfServiceRegistration))!.Value).IsEqualTo("true");
        });

        var after = await SnapshotAsync(verification);
        await Assert.That(notifications.Items.Select(notification => notification.Key)).IsEquivalentTo(
            new[]
            {
                GovernanceSettingKeys.Tenants.SelfServiceRegistration, GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
                GovernanceSettingKeys.Routing.DefaultPublicHomePage, GovernanceSettingKeys.TenantDelegation.LockSmtp,
                GovernanceSettingKeys.TenantDelegation.LockStorage, GovernanceSettingKeys.TenantDelegation.LockAnalytics,
                GovernanceSettingKeys.TenantDelegation.LockAiAssistant
            });
        await Assert.That(publishedSnapshots.Count).IsEqualTo(7);
        foreach (var snapshot in publishedSnapshots)
            await Assert.That(snapshot).IsEquivalentTo(after);
        await Assert.That(notifications.Items.All(notification =>
            notification.ActorUserId == fixture.Subject.PrincipalId && notification.TenantId is null)).IsTrue();
        var smtpNotification = notifications.Items.Single(notification =>
            notification.Key == GovernanceSettingKeys.TenantDelegation.LockSmtp);
        await Assert.That(smtpNotification.OldValue).IsEqualTo("true");
        await Assert.That(smtpNotification.NewValue).IsEqualTo("false");
        var previousSmtp = before.Single(row => row.TenantId is null
            && row.Key == GovernanceSettingKeys.TenantDelegation.LockSmtp);
        var smtp = after.Single(row => row.TenantId is null
            && row.Key == GovernanceSettingKeys.TenantDelegation.LockSmtp);
        await Assert.That(smtp.Value).IsEqualTo("false");
        await Assert.That(smtp.Id).IsEqualTo(previousSmtp.Id);
        await Assert.That(smtp.CreatedAt).IsEqualTo(previousSmtp.CreatedAt);
        await Assert.That(smtp.CreatedBy).IsEqualTo(previousSmtp.CreatedBy);
        await Assert.That(smtp.UpdatedBy).IsEqualTo(fixture.Subject.PrincipalId);
        await Assert.That(smtp.UpdatedAt).IsNotNull();
        await Assert.That(after.Where(row => row.TenantId is not null))
            .IsEquivalentTo(before.Where(row => row.TenantId is not null));
        await Assert.That(await RevisionAsync(verification)).IsEqualTo(revision + 1);
    }

    [Test]
    public async Task NonSmtpPatch_PreservesDeploymentRulesWithoutAcquiringSmtpFence()
    {
        var smtpFenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await TenantDelegationAuthenticationFixture.CreateAsync(beforeLock: (key, _) =>
        {
            if (key == GovernanceSettingKeys.Email.DeliveryEnabled) smtpFenceReached.TrySetResult();
            return Task.CompletedTask;
        });
        await using (var seed = fixture.CreateIndependentContext())
            await SeedSettingAsync(seed, GovernanceSettingKeys.Deployment.Mode, "\"SingleTenant\"");
        await using var verification = fixture.CreateIndependentContext();
        var before = await SnapshotAsync(verification);
        long revision = await RevisionAsync(verification);
        await using var selected = await fixture.SelectAsync();
        var services = selected.Services;
        using var transaction = new TransactionObserver(services.GetRequiredService<ExploreDbContext>());
        var result = await services.GetRequiredService<ICommandHandler<UpdateTenantDelegationSettingsCommand,
            BaseCommandResponse<Guid>>>().ExecuteAsync(new UpdateTenantDelegationSettingsCommand
            {
                UserId = fixture.UserId,
                Patch = new PatchTenantDelegationSettingsDto
                {
                    AllowTenantSelfServiceRegistration = OptionalUpdate<bool>.Set(true),
                    LockTenantStorage = OptionalUpdate<bool>.Set(false)
                }
            }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(smtpFenceReached.Task.IsCompleted).IsFalse();
        await Assert.That(transaction.Started.Task.IsCompletedSuccessfully).IsTrue();
        services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache();
        var settings = await services.GetRequiredService<IInstanceGovernanceSettingService>().ReadSettingsAsync();
        await Assert.That(settings.TenantDelegation.AllowTenantSelfServiceRegistration).IsFalse();
        await Assert.That(settings.TenantDelegation.LockTenantStorage).IsFalse();
        await Assert.That(fixture.Published.Items.Select(notification => notification.Key)).IsEquivalentTo(
            new[] { GovernanceSettingKeys.Tenants.SelfServiceRegistration, GovernanceSettingKeys.TenantDelegation.LockStorage });
        await Assert.That(fixture.Published.Items.All(notification =>
            notification.ActorUserId == fixture.UserId && notification.TenantId is null)).IsTrue();
        var after = await SnapshotAsync(verification);
        await Assert.That(after.Where(row => row.Key.StartsWith("email.", StringComparison.Ordinal)
            || row.Key == GovernanceSettingKeys.TenantDelegation.LockSmtp)).IsEquivalentTo(
            before.Where(row => row.Key.StartsWith("email.", StringComparison.Ordinal)
                || row.Key == GovernanceSettingKeys.TenantDelegation.LockSmtp));
        await Assert.That(await RevisionAsync(verification)).IsEqualTo(revision);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InvalidOrUnauthorizedPatch_ProducesNoTransactionOrNotifications(bool unauthorized)
    {
        if (unauthorized)
        {
            // A genuinely authenticated account without a persisted administrator grant.
            // A random command UserId is no longer authority for an SMTP patch.
            var notifications = new Notifications();
            await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(
                instanceAdministrator: false, tenantAdministrator: false,
                configureServices: services => ConfigureObservations(services, notifications));
            await using (var seed = fixture.CreateIndependentContext())
                await SeedSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockSmtp, "true");
            await using var verification = fixture.CreateIndependentContext();
            verification.TenantContext = new SnapshotTenant(fixture.TenantId);
            var before = await SnapshotAsync(verification);
            long revision = await RevisionAsync(verification);
            await fixture.ExecuteAsync(tenantOwner: false, async services =>
            {
                using var transaction = new TransactionObserver(services.GetRequiredService<ExploreDbContext>());
                var result = await services.GetRequiredService<ICommandHandler<UpdateTenantDelegationSettingsCommand,
                    BaseCommandResponse<Guid>>>().ExecuteAsync(new UpdateTenantDelegationSettingsCommand
                    {
                        UserId = fixture.Subject.PrincipalId,
                        Patch = new PatchTenantDelegationSettingsDto { LockTenantSmtp = OptionalUpdate<bool>.Set(false) }
                    }, CancellationToken.None);
                await Assert.That(result.IsSuccess).IsFalse();
                await Assert.That(transaction.Started.Task.IsCompleted).IsFalse();
                await Assert.That(notifications.Items).IsEmpty();
                services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache();
                await Assert.That((await services.GetRequiredService<IInstanceGovernanceSettingService>()
                    .ReadSettingsAsync()).TenantDelegation.LockTenantSmtp).IsTrue();
            });
            await Assert.That(await SnapshotAsync(verification)).IsEquivalentTo(before);
            await Assert.That(await RevisionAsync(verification)).IsEqualTo(revision);
        }
        else
        {
            await using var fixture = await TenantDelegationAuthenticationFixture.CreateAsync();
            await using (var seed = fixture.CreateIndependentContext())
                await SeedSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockSmtp, "true");
            await using var verification = fixture.CreateIndependentContext();
            var before = await SnapshotAsync(verification);
            long revision = await RevisionAsync(verification);
            await using var selected = await fixture.SelectAsync();
            var services = selected.Services;
            using var transaction = new TransactionObserver(services.GetRequiredService<ExploreDbContext>());
            var result = await services.GetRequiredService<ICommandHandler<UpdateTenantDelegationSettingsCommand,
                BaseCommandResponse<Guid>>>().ExecuteAsync(new UpdateTenantDelegationSettingsCommand
                {
                    UserId = fixture.UserId,
                    Patch = new PatchTenantDelegationSettingsDto()
                }, CancellationToken.None);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(transaction.Started.Task.IsCompleted).IsFalse();
            await Assert.That(fixture.Published.Items).IsEmpty();
            services.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache();
            await Assert.That((await services.GetRequiredService<IInstanceGovernanceSettingService>()
                .ReadSettingsAsync()).TenantDelegation.LockTenantSmtp).IsTrue();
            await Assert.That(await SnapshotAsync(verification)).IsEquivalentTo(before);
            await Assert.That(await RevisionAsync(verification)).IsEqualTo(revision);
        }
    }

    private static async Task<SettingSnapshot[]> SnapshotAsync(ExploreDbContext context)
    {
        var system = await context.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
            .Select(row => new SettingSnapshot(row.Id, null, row.SettingKey, row.Value, row.IsLocked,
                row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync();
        var tenant = await context.TenantSettingOverrides.AsNoTracking().OrderBy(row => row.SettingKey)
            .Select(row => new SettingSnapshot(row.Id, row.TenantId, row.SettingKey, row.Value, row.IsLocked,
                row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToArrayAsync();
        return [.. system, .. tenant];
    }

    private static Task<long> RevisionAsync(ExploreDbContext context) =>
        context.EmailDispatchProcessorStates.AsNoTracking().Select(row => row.DeliveryPolicyRevision).SingleAsync();

    private sealed record SettingSnapshot(Guid Id, Guid? TenantId, string Key, string Value, bool IsLocked,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
}
