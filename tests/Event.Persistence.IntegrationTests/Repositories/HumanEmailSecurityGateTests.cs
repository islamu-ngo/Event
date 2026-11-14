using System.Data;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class HumanEmailSecurityGateTests
{
    [Test]
    public async Task SourceAcquisitionReservesBorrowedDeferredTransactionUntilRollback()
    {
        await using var authentication = await AuthenticatedSettingsFixture.CreateSqliteAsync();
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await authentication.RunInstanceEmailAsync(async session =>
        {
            var context = session.Services.GetRequiredService<ExploreDbContext>();
            await using var gate = await session.Services.GetRequiredService<IHumanSecuritySourceGate>().AcquireAsync(token);
            var protocol = await session.Services.GetRequiredService<IHumanEmailSourceFactory>().CreateAsync(token);
            var preparation = await protocol.PrepareAsync(includeSelectedRoute: false, token);
            await context.Database.OpenConnectionAsync(token);
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            await using var deferred = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
            await using var borrowed = await context.Database.UseTransactionAsync(deferred, token);
            var contenderOptions = new SqliteConnectionStringBuilder(connection.ConnectionString) { DefaultTimeout = 1 };
            await using var contender = new SqliteConnection(contenderOptions.ToString());
            await contender.OpenAsync(token);

            await using (var before = contender.BeginTransaction(IsolationLevel.Serializable, deferred: false))
                await before.RollbackAsync(token);

            await protocol.AcquireAsync(preparation, token);
            var blocked = await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await using var unexpected = contender.BeginTransaction(IsolationLevel.Serializable, deferred: false);
                await unexpected.RollbackAsync(token);
            });
            await Assert.That(blocked!.SqliteErrorCode).IsEqualTo(5);

            await borrowed!.RollbackAsync(token);
            await using var after = contender.BeginTransaction(IsolationLevel.Serializable, deferred: false);
            await after.RollbackAsync(token);
            return true;
        }, token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalHumanEmailOwnerRetainsTheGateFromSourceObservationThroughSettlement(bool failAfterSave)
    {
        var observation = new EmailGateObservation(failAfterSave);
        await using var authentication = await AuthenticatedSettingsFixture.CreateSqliteAsync(services =>
        {
            var registration = services.Last(descriptor => descriptor.ServiceType == typeof(IHumanEmailSourceFactory));
            var actualFactory = registration.ImplementationFactory
                ?? throw new InvalidOperationException("The actual email source factory registration is required.");
            services.Remove(registration);
            services.AddScoped<IHumanEmailSourceFactory>(provider => new ObservedSourceFactory(
                (IHumanEmailSourceFactory)actualFactory(provider), provider.GetRequiredService<ExploreDbContext>(),
                provider.GetRequiredService<ExploreDbContextExecutionLease>(), observation));
            services.AddScoped<ISettingMutationLock>(provider =>
            {
                var context = provider.GetRequiredService<ExploreDbContext>();
                return new ObservedEmailKeys(context,
                    new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)), observation);
            });
        });
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        var before = await authentication.ReadInstanceEmailPolicyAsync(token);
        observation.Armed = true;

        Task<EmailDeliverySettingsWriteResult> ApplyAsync() => authentication.RunInstanceEmailAsync(session =>
            session.Services.GetRequiredService<IEmailDeliverySettingsWriter>().ApplyAsync(
                [new(null, GovernanceSettingKeys.Email.FromName, EmailDeliverySettingMutationKind.SetValue,
                    observation.ExpectedValue)], session.ActorId, token), token);

        if (failAfterSave)
        {
            var failure = await Assert.ThrowsAsync<Exception>(ApplyAsync);
            await Assert.That(ContainsOwnSaveFailure(failure!)).IsTrue();
        }
        else
        {
            var result = await ApplyAsync();
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await Assert.That(observation.HeldAfterSettlement).IsTrue();
        }

        await Assert.That(observation.ObservedBeforeSource).IsTrue();
        await Assert.That(observation.ObservedBeforeKeys).IsTrue();
        await Assert.That(observation.SavedOnOriginalOwner).IsTrue();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(observation.OriginalContext!)).IsFalse();
        var after = await authentication.ReadInstanceEmailPolicyAsync(token);
        await Assert.That(after.Value).IsEqualTo(failAfterSave ? before.Value : observation.ExpectedValue);
        await Assert.That(after.Revision).IsEqualTo(failAfterSave ? before.Revision : before.Revision + 1);
    }

    private static bool ContainsOwnSaveFailure(Exception failure) => failure is EmailGateSaveFailure
        || failure.InnerException is { } inner && ContainsOwnSaveFailure(inner)
        || failure is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsOwnSaveFailure);

    private sealed class EmailGateSaveFailure : Exception;

    /// <summary>Delegates the actual registered protocol; never issues source or human authority.</summary>
    private sealed class ObservedSourceFactory(IHumanEmailSourceFactory actual, ExploreDbContext context,
        ExploreDbContextExecutionLease selection, EmailGateObservation observation) : IHumanEmailSourceFactory
    {
        public Task<IHumanEmailSourceProtocol> CreateAsync(CancellationToken cancellationToken = default)
        {
            observation.BeforeSource(context, selection);
            return actual.CreateAsync(cancellationToken);
        }
    }

    private sealed class EmailGateObservation(bool failAfterSave)
    {
        internal bool Armed { get; set; }
        internal string ExpectedValue { get; } = $"\"source-gate-{Guid.CreateVersion7():N}\"";
        internal bool ObservedBeforeSource { get; private set; }
        internal bool ObservedBeforeKeys { get; private set; }
        internal bool SavedOnOriginalOwner { get; private set; }
        internal bool HeldAfterSettlement { get; private set; }
        internal ExploreDbContext? OriginalContext { get; private set; }
        private DbConnection? connector;

        internal void BeforeSource(ExploreDbContext context, ExploreDbContextExecutionLease selection)
        {
            if (!Armed) return;
            HumanSecuritySourceGate.RequireCurrent(context);
            selection.RequireOriginalOwner(context);
            if (selection.PolicyPurpose != PolicyMutationPurpose.EmailDelivery
                || selection.SelectedTenantId is not null || !context.HasHumanCommitContextLease
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.Database.CurrentTransaction is not null || OriginalContext is not null)
                throw new InvalidOperationException("email_test_original_idle_purpose_missing");
            OriginalContext = context;
            connector = context.Database.GetDbConnection();
            context.SavedChanges += (_, _) =>
            {
                if (!context.ChangeTracker.Entries<SystemSetting>().Any(entry =>
                    entry.Entity.SettingKey == GovernanceSettingKeys.Email.FromName && entry.Entity.Value == ExpectedValue)) return;
                RequireOriginal(context);
                if (context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
                    || transaction.IsolationLevel != IsolationLevel.Serializable
                    || !ReferenceEquals(transaction.Connection, connector))
                    throw new InvalidOperationException("email_test_original_saved_transaction_missing");
                SavedOnOriginalOwner = true;
                if (failAfterSave) throw new EmailGateSaveFailure();
            };
            ObservedBeforeSource = true;
        }

        internal void BeforeKeys(ExploreDbContext context)
        {
            if (!Armed) return;
            RequireOriginal(context);
            if (!ObservedBeforeSource || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("email_test_original_keys_acquired_after_begin");
            ObservedBeforeKeys = true;
        }

        internal void AfterSettlement(ExploreDbContext context)
        {
            if (!Armed) return;
            RequireOriginal(context);
            if (!SavedOnOriginalOwner || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("email_test_original_settlement_missing");
            HeldAfterSettlement = true;
        }

        private void RequireOriginal(ExploreDbContext context)
        {
            HumanSecuritySourceGate.RequireCurrent(context);
            if (!ReferenceEquals(context, OriginalContext) || !ReferenceEquals(context.Database.GetDbConnection(), connector))
                throw new InvalidOperationException("email_test_original_source_owner_changed");
        }
    }

    private sealed class ObservedEmailKeys(ExploreDbContext context, ISettingMutationLock actual, EmailGateObservation observation)
        : ISettingMutationLock
    {
        public Task<T> ExecuteAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            actual.ExecuteAsync(key, operation, cancellationToken);

        public Task<T> ExecuteManyAsync<T>(IEnumerable<string> keys, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            actual.ExecuteManyAsync(keys, operation, cancellationToken);

        public async Task<T> ExecuteOrderedGroupsAsync<T>(IEnumerable<IEnumerable<string>> groups,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            observation.BeforeKeys(context);
            T result = await actual.ExecuteOrderedGroupsAsync(groups, operation, cancellationToken);
            observation.AfterSettlement(context);
            return result;
        }
    }
}
