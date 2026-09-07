// ABOUTME: Guards tenant-delegation command locking, authoritative reads, and committed notification publication.
// ABOUTME: Exercises real administrator grants, MediatR, governance services, and SQLite transactions.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class TenantDelegationMutationTests
{
    [Test]
    public async Task GlobalSmtpFencePrecedesMixedPatchTransaction_AndNotificationsFollowCommit()
    {
        const string heldKey = GovernanceSettingKeys.Email.DeliveryEnabled;
        var databasePath = Path.Combine(Path.GetTempPath(), $"delegation-command-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            Guid administrator;
            await using (var context = CreateContext(databasePath))
            {
                administrator = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
                var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
                await SetDeploymentAsync(context, mutationLock, "\"MultiTenant\"");
            }

            using var cancellation = new CancellationTokenSource();
            await using var holderContext = CreateContext(databasePath);
            var transaction = new TransactionObserver();
            await using var writerContext = CreateContext(databasePath, transaction);
            var holderOwnsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var holderLock = new RelationalSettingMutationLock(holderContext, new EfCoreUnitOfWork(holderContext));
            var writerLock = new RelationalSettingMutationLock(writerContext, new EfCoreUnitOfWork(writerContext), (key, _) =>
            {
                if (key == heldKey) reachedLock.TrySetResult();
                return Task.CompletedTask;
            });
            using var fixture = new InstanceSettingsCommandFixture(writerContext, administrator, writerLock);
            var handler = CreateHandler(fixture);
            Task holder = holderLock.ExecuteOrderedGroupsAsync([[heldKey]], async token =>
            {
                holderOwnsLock.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
                return true;
            }, cancellation.Token);
            Task<BaseCommandResponse<Guid>>? write = null;
            Task? observed = null;
            bool transactionStartedBeforeRelease = false;
            try
            {
                await holderOwnsLock.Task.WaitAsync(TimeSpan.FromSeconds(15));
                write = handler.Handle(new UpdateTenantDelegationSettingsCommand
                {
                    UserId = administrator,
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
                observed = await Task.WhenAny(reachedLock.Task, transaction.Started.Task, write)
                    .WaitAsync(TimeSpan.FromSeconds(15));
                transactionStartedBeforeRelease = transaction.Started.Task.IsCompleted;
            }
            finally
            {
                release.TrySetResult();
                cancellation.CancelAfter(TimeSpan.FromSeconds(15));
                Task[] pending = write is null ? [holder] : [holder, write];
                await Task.WhenAll(pending);
            }

            await Assert.That(observed).IsSameReferenceAs(reachedLock.Task);
            await Assert.That(transactionStartedBeforeRelease).IsFalse();
            await Assert.That((await write!).IsSuccess).IsTrue();
            var settings = await fixture.Governance.ReadSettingsAsync();
            await Assert.That(settings.TenantDelegation.LockTenantSmtp).IsFalse();
            await Assert.That(settings.TenantDelegation.DefaultPublicHomePage).IsEqualTo("LandingPage");
            await Assert.That(settings.TenantDelegation.LockTenantHomePagePreference).IsTrue();
            await Assert.That(settings.TenantDelegation.AllowTenantSelfServiceRegistration).IsTrue();
            await Assert.That((await fixture.SystemSettings.GetByKey(GovernanceSettingKeys.Tenants.SelfServiceRegistration))!.Value)
                .IsEqualTo("true");
            await Assert.That(fixture.Notifications.Published.Select(notification => notification.Key)).IsEquivalentTo(
                new[]
                {
                    GovernanceSettingKeys.Tenants.SelfServiceRegistration, GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
                    GovernanceSettingKeys.Routing.DefaultPublicHomePage, GovernanceSettingKeys.TenantDelegation.LockSmtp,
                    GovernanceSettingKeys.TenantDelegation.LockStorage, GovernanceSettingKeys.TenantDelegation.LockAnalytics,
                    GovernanceSettingKeys.TenantDelegation.LockAiAssistant
                });
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task NonSmtpPatch_PreservesDeploymentRulesWithoutAcquiringSmtpFence()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"delegation-nonsmtp-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            Guid administrator;
            await using (var context = CreateContext(databasePath))
            {
                administrator = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
                await SetDeploymentAsync(context, new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)),
                    "\"SingleTenant\"");
            }
            await using var writerContext = CreateContext(databasePath);
            var smtpFenceReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var mutationLock = new RelationalSettingMutationLock(writerContext, new EfCoreUnitOfWork(writerContext), (key, _) =>
            {
                if (key == GovernanceSettingKeys.Email.DeliveryEnabled) smtpFenceReached.TrySetResult();
                return Task.CompletedTask;
            });
            using var fixture = new InstanceSettingsCommandFixture(writerContext, administrator, mutationLock);
            var result = await CreateHandler(fixture).Handle(new UpdateTenantDelegationSettingsCommand
            {
                UserId = administrator,
                Patch = new PatchTenantDelegationSettingsDto
                {
                    AllowTenantSelfServiceRegistration = OptionalUpdate<bool>.Set(true),
                    LockTenantStorage = OptionalUpdate<bool>.Set(false)
                }
            }, CancellationToken.None);

            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(smtpFenceReached.Task.IsCompleted).IsFalse();
            var settings = await fixture.Governance.ReadSettingsAsync();
            await Assert.That(settings.TenantDelegation.AllowTenantSelfServiceRegistration).IsFalse();
            await Assert.That(settings.TenantDelegation.LockTenantStorage).IsFalse();
            await Assert.That(fixture.Notifications.Published.Select(notification => notification.Key)).IsEquivalentTo(
                new[] { GovernanceSettingKeys.Tenants.SelfServiceRegistration, GovernanceSettingKeys.TenantDelegation.LockStorage });
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InvalidOrUnauthorizedPatch_ProducesNoTransactionOrNotifications(bool unauthorized)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"delegation-rejection-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            Guid administrator;
            await using (var context = CreateContext(databasePath))
                administrator = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
            var transaction = new TransactionObserver();
            await using var writerContext = CreateContext(databasePath, transaction);
            using var fixture = new InstanceSettingsCommandFixture(writerContext, administrator);
            var result = await CreateHandler(fixture).Handle(new UpdateTenantDelegationSettingsCommand
            {
                UserId = unauthorized ? Guid.CreateVersion7() : administrator,
                Patch = unauthorized
                    ? new PatchTenantDelegationSettingsDto { LockTenantSmtp = OptionalUpdate<bool>.Set(false) }
                    : new PatchTenantDelegationSettingsDto()
            }, CancellationToken.None);

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(transaction.Started.Task.IsCompleted).IsFalse();
            await Assert.That(fixture.Notifications.Published).IsEmpty();
            await Assert.That((await fixture.Governance.ReadSettingsAsync()).TenantDelegation.LockTenantSmtp).IsTrue();
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static UpdateTenantDelegationSettingsCommandHandler CreateHandler(InstanceSettingsCommandFixture fixture) =>
        new(fixture.AdminContext, fixture.Governance, fixture.UnitOfWork, fixture.Mediator, fixture.MutationLock);

    private static Task SetDeploymentAsync(ExploreDbContext context, RelationalSettingMutationLock mutationLock,
        string value, CancellationToken cancellationToken = default) =>
        new SystemSettingRepository(context, mutationLock).UpsertAsync(new SystemSetting
        {
            Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Deployment.Mode, Value = value,
            ValueType = SettingValueType.String, CreatedAt = DateTime.UtcNow
        }, cancellationToken);

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }

    private sealed class TransactionObserver : DbTransactionInterceptor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
