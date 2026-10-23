using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class MutationScopeHumanGateTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectHumanCommandRetainsItsOriginalGateThroughCommitOrRollback(bool failAfterSave)
    {
        var observation = new CommandObservation(failAfterSave);
        await using var store = await AuthenticatedStore.CreateAsync(observation);
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;

        await store.Authentication.RunAsync(PlatformDefaults.DefaultTenantId, async session =>
        {
            observation.Arm(session.Context);
            await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(session.Context)).IsFalse();
            Task<BaseCommandResponse<Guid>> ExecuteAsync() => session.Services
                .GetRequiredService<ICommandHandler<CreateEventTemplateCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(observation.Command(), token);

            if (failAfterSave)
            {
                var failure = await Assert.ThrowsAsync<Exception>(ExecuteAsync);
                await Assert.That(ContainsOwnFailure(failure!)).IsTrue();
            }
            else
            {
                var result = await ExecuteAsync();
                await Assert.That(result.IsSuccess).IsTrue();
            }

            await Assert.That(observation.ObservedBeforePreparation).IsTrue();
            await Assert.That(observation.ObservedBeforeBegin).IsTrue();
            await Assert.That(observation.SavedOnOriginalTransaction).IsTrue();
            await Assert.That(observation.HeldAfterSettlement).IsTrue();
            await Assert.That(session.Context.Database.CurrentTransaction).IsNull();
            await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(session.Context)).IsFalse();
            return true;
        }, cancellationToken: token);

        await using var reader = store.OpenReader();
        var rows = await reader.EventTemplates.AsNoTracking().Where(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.TemplateKey == observation.TemplateKey)
            .Select(row => new { row.Id, row.DisplayName, row.CreatedBy }).ToArrayAsync(token);
        await Assert.That(rows.Length).IsEqualTo(failAfterSave ? 0 : 1);
        if (!failAfterSave)
        {
            await Assert.That(rows[0].Id).IsEqualTo(observation.SavedTemplateId);
            await Assert.That(rows[0].DisplayName).IsEqualTo("Original command owner");
            await Assert.That(rows[0].CreatedBy).IsEqualTo(store.Authentication.ActorId);
        }
    }

    [Test]
    public async Task FailedDirectCommandDoesNotDisposeItsCompatibleCallersHumanLease()
    {
        var observation = new CommandObservation(failAfterSave: true);
        await using var store = await AuthenticatedStore.CreateAsync(observation);
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;

        await store.Authentication.RunAsync(PlatformDefaults.DefaultTenantId, async session =>
        {
            await using var human = await new HumanSecuritySourceGate(session.Context).AcquireAsync(token);
            observation.Arm(session.Context);
            var failure = await Assert.ThrowsAsync<Exception>(async () => await session.Services
                .GetRequiredService<ICommandHandler<CreateEventTemplateCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(observation.Command(), token));

            await Assert.That(ContainsOwnFailure(failure!)).IsTrue();
            await Assert.That(observation.ObservedBeforePreparation).IsTrue();
            await Assert.That(observation.SavedOnOriginalTransaction).IsTrue();
            await Assert.That(observation.HeldAfterSettlement).IsTrue();
            HumanSecuritySourceGate.RequireCurrent(session.Context);
            await Assert.That(session.Context.Database.CurrentTransaction).IsNull();
            return true;
        }, cancellationToken: token);

        await using var reader = store.OpenReader();
        await Assert.That(await reader.EventTemplates.AsNoTracking().AnyAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.TemplateKey == observation.TemplateKey, token)).IsFalse();
    }

    [Test]
    public async Task LostOriginalConnectorAfterCommitRetainsUnknownOutcomeAndBothGateFailures()
    {
        var observation = new CommandObservation(failAfterSave: false, loseAfterCommit: true);
        await using var store = await AuthenticatedStore.CreateAsync(observation);
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;

        var failure = await Assert.ThrowsAsync<Exception>(() => store.Authentication.RunAsync(
            PlatformDefaults.DefaultTenantId, async session =>
            {
                observation.Arm(session.Context);
                return await session.Services
                    .GetRequiredService<ICommandHandler<CreateEventTemplateCommand, BaseCommandResponse<Guid>>>()
                    .ExecuteAsync(observation.Command(), token);
            }, cancellationToken: token));

        var causes = ExceptionCauses(failure!).ToArray();
        string diagnostic = DescribeCommitFailure(observation, causes);
        await Assert.That(observation.ObservedBeforePreparation).IsTrue().Because(diagnostic);
        await Assert.That(observation.SavedOnOriginalTransaction).IsTrue().Because(diagnostic);
        await Assert.That(observation.ObservedBeforePhysicalCommit).IsTrue().Because(diagnostic);
        await Assert.That(observation.ReachedPostCommitFault).IsTrue().Because(diagnostic);
        await Assert.That(causes.OfType<HumanMutationOutcomeUnknownException>().Any()).IsTrue().Because(diagnostic);
        await Assert.That(causes.OfType<CommandCommitNotificationFailure>().Any()).IsTrue().Because(diagnostic);
        await Assert.That(causes.Any(cause => cause.Message == "human_security_source_connector_changed"))
            .IsTrue().Because(diagnostic);
        await Assert.That(causes.Any(cause => cause.Message == "human_security_source_cleanup_lost_custody"))
            .IsTrue().Because(diagnostic);

        // The observer fault followed a real database COMMIT. The caller must
        // retain unknown settlement, not assert rollback or replay this command.
        await using var reader = store.OpenReader();
        var saved = await reader.EventTemplates.AsNoTracking().SingleAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.TemplateKey == observation.TemplateKey, token);
        await Assert.That(saved.Id).IsEqualTo(observation.SavedTemplateId);
        await Assert.That(saved.CreatedBy).IsEqualTo(store.Authentication.ActorId);
    }

    private static IEnumerable<Exception> ExceptionCauses(Exception failure)
    {
        yield return failure;
        if (failure is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in ExceptionCauses(inner))
                    yield return cause;
        }
        else if (failure.InnerException is { } inner)
        {
            foreach (var cause in ExceptionCauses(inner)) yield return cause;
        }
    }

    private static string DescribeCommitFailure(CommandObservation observation, IReadOnlyList<Exception> causes)
    {
        // Exception messages/ToString/SQL/requests can contain credentials or
        // personal values. Only closed probe states, types and method metadata
        // are needed to locate an earlier failure without exposing that data.
        string types = string.Join(" -> ", causes.Select(cause => cause.GetType().FullName));
        string methods = string.Join(" -> ", causes.SelectMany(cause =>
                new StackTrace(cause, fNeedFileInfo: false).GetFrames() ?? [])
            .Select(frame => frame.GetMethod()).Where(method => method is not null)
            .Select(method => $"{method!.DeclaringType?.FullName}.{method.Name}")
            .Distinct(StringComparer.Ordinal).Take(24));
        return $"Commit probe stage={observation.CommitFaultStage}; source={observation.ObservedBeforePreparation}; "
            + $"saved={observation.SavedOnOriginalTransaction}; fault={observation.ReachedPostCommitFault}; "
            + $"beforeCommit={observation.ObservedBeforePhysicalCommit}; "
            + $"completedMatchesSaved={observation.CompletedTransactionMatchesSaved}; "
            + $"completedConnectionNull={observation.CompletedTransactionConnectionIsNull}; "
            + $"exception types={types}; methods={methods}.";
    }

    private static bool ContainsOwnFailure(Exception failure) => failure is CommandSavedFailure
        || failure.InnerException is { } inner && ContainsOwnFailure(inner)
        || failure is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsOwnFailure);

    private sealed class CommandSavedFailure : Exception;
    private sealed class CommandCommitNotificationFailure : Exception;

    /// <summary>Observes real preparation; subject and source authority stay with the production factory.</summary>
    private sealed class ObservedPreparation(IHumanCommitScopePreparation actual, IServiceProvider provider,
        CommandObservation observation) : IHumanCommitScopePreparation
    {
        public HumanCommitSubject? Subject => actual.Subject;

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            observation.BeforePreparation(provider.GetRequiredService<ExploreDbContext>(),
                provider.GetRequiredService<ExploreDbContextExecutionLease>());
            return actual.PrepareAsync(cancellationToken);
        }
    }

    /// <summary>Forwards the actual single-attempt owner, including its rollback and non-replay behavior.</summary>
    private sealed class ObservedAttempt(ISerializableAttemptOwner actual, ExploreDbContext context,
        CommandObservation observation) : ISerializableAttemptOwner
    {
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            bool observed = observation.BeforeAttempt(context);
            try { return await actual.ExecuteAsync(operation, cancellationToken); }
            finally { if (observed) observation.AfterSettlement(context); }
        }
    }

    private sealed class CommandObservation(bool failAfterSave, bool loseAfterCommit = false) : DbTransactionInterceptor
    {
        internal bool LoseAfterCommit => loseAfterCommit;
        internal bool ReachedPostCommitFault { get; private set; }
        internal bool ObservedBeforePhysicalCommit { get; private set; }
        internal bool CompletedTransactionMatchesSaved { get; private set; }
        internal bool CompletedTransactionConnectionIsNull { get; private set; }
        internal string CommitFaultStage { get; private set; } = "not_entered";
        internal string TemplateKey { get; } = $"human-owner-{Guid.CreateVersion7():N}";
        internal Guid SavedTemplateId { get; private set; }
        internal bool ObservedBeforePreparation { get; private set; }
        internal bool ObservedBeforeBegin { get; private set; }
        internal bool SavedOnOriginalTransaction { get; private set; }
        internal bool HeldAfterSettlement { get; private set; }
        private ExploreDbContext? original;
        private DbConnection? connector;
        private DbTransaction? transaction;
        private DbTransaction? committingTransaction;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction committing,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!loseAfterCommit || !ReferenceEquals(eventData.Context, original))
                return ValueTask.FromResult(result);
            RequireOriginal(original!);
            if (!SavedOnOriginalTransaction || !ReferenceEquals(committing, transaction)
                || !ReferenceEquals(original!.Database.CurrentTransaction?.GetDbTransaction(), committing)
                || !ReferenceEquals(committing.Connection, connector))
                throw new InvalidOperationException("The precommit probe did not retain the original saved physical transaction.");
            committingTransaction = committing;
            ObservedBeforePhysicalCommit = true;
            return ValueTask.FromResult(result);
        }

        public override async Task TransactionCommittedAsync(DbTransaction committed,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!loseAfterCommit || !ReferenceEquals(eventData.Context, original)) return;
            CommitFaultStage = "entered";
            RequireOriginal(original!);
            CommitFaultStage = "custody_checked";
            CompletedTransactionMatchesSaved = ReferenceEquals(committed, transaction);
            CompletedTransactionConnectionIsNull = committed.Connection is null;
            // Connection custody was proven while the transaction was live.
            // A completed provider transaction may no longer expose Connection;
            // retain its exact object identity across the real commit callbacks.
            if (!ObservedBeforePhysicalCommit || !SavedOnOriginalTransaction
                || !CompletedTransactionMatchesSaved || !ReferenceEquals(committed, committingTransaction))
                throw new InvalidOperationException("The commit fault did not reach the original saved physical transaction.");
            CommitFaultStage = "physical_transaction_checked";
            ReachedPostCommitFault = true;
            await connector!.CloseAsync();
            CommitFaultStage = "connector_closed";
            throw new CommandCommitNotificationFailure();
        }

        internal CreateEventTemplateCommand Command() => new()
        {
            TemplateDto = new CreateEventTemplateDto
            {
                TemplateKey = TemplateKey,
                DisplayName = "Original command owner",
                Definitions = []
            }
        };

        internal void Arm(ExploreDbContext context)
        {
            if (original is not null) throw new InvalidOperationException("A command observation was already armed.");
            original = context;
            connector = context.Database.GetDbConnection();
            context.SavedChanges += (_, _) =>
            {
                var rows = context.ChangeTracker.Entries<EventTemplate>()
                    .Where(entry => entry.Entity.TemplateKey == TemplateKey).ToArray();
                if (rows.Length == 0) return;
                RequireOriginal(context);
                if (rows.Length != 1 || rows[0].Entity.TenantId != PlatformDefaults.DefaultTenantId
                    || context.Database.CurrentTransaction?.GetDbTransaction() is not { } current
                    || current.IsolationLevel != IsolationLevel.Serializable
                    || !ReferenceEquals(current.Connection, connector)
                    || transaction is not null && !ReferenceEquals(transaction, current))
                    throw new InvalidOperationException("The command saved outside its original Serializable transaction.");
                transaction = current;
                SavedTemplateId = rows[0].Entity.Id;
                SavedOnOriginalTransaction = true;
                if (failAfterSave) throw new CommandSavedFailure();
            };
        }

        internal void BeforePreparation(ExploreDbContext context, ExploreDbContextExecutionLease selection)
        {
            if (!ReferenceEquals(context, original)) return;
            RequireOriginal(context);
            selection.RequireOriginalOwner(context);
            if (context.Database.CurrentTransaction is not null || !context.HasHumanCommitContextLease
                || context.TenantFilterTenantId != PlatformDefaults.DefaultTenantId)
                throw new InvalidOperationException("Human preparation did not retain the original idle selected tenant.");
            ObservedBeforePreparation = true;
        }

        internal bool BeforeAttempt(ExploreDbContext context)
        {
            if (!ReferenceEquals(context, original)) return false;
            RequireOriginal(context);
            if (!ObservedBeforePreparation || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("The command lost Human custody before BEGIN.");
            ObservedBeforeBegin = true;
            return true;
        }

        internal void AfterSettlement(ExploreDbContext context)
        {
            RequireOriginal(context);
            if (!SavedOnOriginalTransaction || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("The command owner did not settle its original saved transaction.");
            HeldAfterSettlement = true;
        }

        private void RequireOriginal(ExploreDbContext context)
        {
            HumanSecuritySourceGate.RequireCurrent(context);
            if (!ReferenceEquals(context, original) || !ReferenceEquals(context.Database.GetDbConnection(), connector))
                throw new InvalidOperationException("The original command connector changed.");
        }
    }

    private sealed class AuthenticatedStore(string path, AuthenticatedSettingsFixture authentication) : IAsyncDisposable
    {
        internal AuthenticatedSettingsFixture Authentication => authentication;

        internal static async Task<AuthenticatedStore> CreateAsync(CommandObservation observation)
        {
            string path = Path.Combine(Path.GetTempPath(), $"human-command-owner-{Guid.CreateVersion7():N}.db");
            await EmailDispatchSqliteFixture.CreateDatabaseAsync(path);
            try
            {
                var authentication = await AuthenticatedSettingsFixture.CreateAsync(
                    () => EmailDispatchSqliteFixture.CreateContext(path), postgres: null,
                    interceptor: observation, configureServices: services =>
                    {
                        services.AddScoped<IHumanCommitScopePreparation>(provider => new ObservedPreparation(
                            (IHumanCommitScopePreparation)provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>(),
                            provider, observation));
                        if (!observation.LoseAfterCommit)
                        {
                            var owner = services.Last(descriptor => descriptor.ServiceType == typeof(ISerializableAttemptOwner));
                            var actual = ActivatorUtilities.CreateFactory(owner.ImplementationType
                                ?? throw new InvalidOperationException("The actual Serializable owner type is required."), []);
                            services.Remove(owner);
                            services.AddScoped<ISerializableAttemptOwner>(provider => new ObservedAttempt(
                                (ISerializableAttemptOwner)actual(provider, []),
                                provider.GetRequiredService<ExploreDbContext>(), observation));
                        }
                    });
                return new(path, authentication);
            }
            catch { DeleteDatabase(path); throw; }
        }

        internal ExploreDbContext OpenReader()
        {
            var context = EmailDispatchSqliteFixture.CreateContext(path);
            context.TenantContext = new ReaderTenant(PlatformDefaults.DefaultTenantId);
            context.BindTenantLease();
            return context;
        }

        public async ValueTask DisposeAsync()
        {
            await authentication.DisposeAsync();
            DeleteDatabase(path);
        }

        private static void DeleteDatabase(string path)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            SqliteConnection.ClearPool(connection);
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }

        private sealed record ReaderTenant(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;
    }
}
