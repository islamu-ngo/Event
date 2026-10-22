using System.Data;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class InternalEmailDeliverySecurityGateTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InternalEmailOwnerHoldsHumanCustodyBeforeKeysAndBeginThroughItsCommit(bool disable)
    {
        await using var store = await Store.CreateAsync(enabled: disable);
        var settlement = new GateSettlementProbe();
        await using var context = store.Open(settlement);
        long before = await PolicyRevisionAsync(context);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var keys = new ObservedKeys(context, new RelationalSettingMutationLock(context, unitOfWork), requireHuman: true);
        var writer = new InternalEmailDeliverySettingsWriter(context, keys, unitOfWork);

        var result = disable
            ? await writer.DisableInternalAsync(null, null)
            : await writer.ApplyInternalAsync(
                [new(null, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true"),
                    new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, "\"smtp.source.test\""),
                    new(null, GovernanceSettingKeys.Email.FromAddress, EmailDeliverySettingMutationKind.SetValue, "\"events@source.test\"")], null);

        await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
        await Assert.That(keys.Entries).IsEqualTo(1);
        await Assert.That(settlement.ObservedBeforeBegin).IsTrue();
        await Assert.That(settlement.ObservedCommit).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(context)).IsFalse();
        await using var observer = store.Open();
        await Assert.That(await DeliveryValueAsync(observer)).IsEqualTo(disable ? "false" : "true");
        await Assert.That(await PolicyRevisionAsync(observer)).IsEqualTo(before + 1);
        await Assert.That(await observer.Users.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task AnExistingTransactionWithoutHumanCustodyIsRefusedBeforeEnteringSettingLocks()
    {
        await using var store = await Store.CreateAsync(enabled: false);
        var dml = new RejectedDmlProbe();
        await using var context = store.Open(dml);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var originalKeys = new RelationalSettingMutationLock(context, unitOfWork);
        var keys = new ObservedKeys(context, originalKeys, requireHuman: false);
        var writer = new InternalEmailDeliverySettingsWriter(context, keys, unitOfWork);
        long before = await PolicyRevisionAsync(context);
        await originalKeys.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All], async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ApplyInternalAsync(
                [new(null, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true"),
                    new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, "\"smtp.source.test\""),
                    new(null, GovernanceSettingKeys.Email.FromAddress, EmailDeliverySettingMutationKind.SetValue, "\"events@source.test\"")], null));
            await Assert.That(refusal!.Message).IsEqualTo("human_security_source_gate_missing");
            await Assert.That(keys.Entries).IsEqualTo(0);
            await Assert.That(dml.Writes).IsEqualTo(0);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
            return true;
        });
        await using var observer = store.Open();
        await Assert.That(await DeliveryValueAsync(observer)).IsEqualTo("false");
        await Assert.That(await PolicyRevisionAsync(observer)).IsEqualTo(before);
    }

    [Test]
    public async Task AnExistingOriginalHumanGateAndTransactionAreRetainedWithoutReacquisition()
    {
        await using var store = await Store.CreateAsync(enabled: true);
        await using var context = store.Open();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var originalKeys = new RelationalSettingMutationLock(context, unitOfWork);
        var keys = new ObservedKeys(context, originalKeys, requireHuman: true);
        var writer = new InternalEmailDeliverySettingsWriter(context, keys, unitOfWork);
        long before = await PolicyRevisionAsync(context);
        await using var human = new HumanSecuritySourceGate(context);
        await using var held = await human.AcquireAsync();
        var connector = context.Database.GetDbConnection();
        await originalKeys.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All], async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var result = await writer.DisableInternalAsync(null, null);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
            await Assert.That(keys.Entries).IsEqualTo(1);
            human.RequireCurrent();
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction().Connection).IsSameReferenceAs(connector);
            await transaction.RollbackAsync();
            return true;
        });
        human.RequireCurrent();
        await using var observer = store.Open();
        await Assert.That(await DeliveryValueAsync(observer)).IsEqualTo("true");
        await Assert.That(await PolicyRevisionAsync(observer)).IsEqualTo(before);
    }

    private static Task<string> DeliveryValueAsync(ExploreDbContext context) => context.SystemSettings.AsNoTracking()
        .Where(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled).Select(row => row.Value).SingleAsync();

    private static Task<long> PolicyRevisionAsync(ExploreDbContext context) => context.EmailDispatchProcessorStates.AsNoTracking()
        .Select(row => row.DeliveryPolicyRevision).SingleAsync();

    private sealed class Store : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"email-human-source-{Guid.CreateVersion7():N}.db");

        internal ExploreDbContext Open(params IInterceptor[] interceptors)
        {
            var context = EmailDispatchSqliteFixture.CreateContext(path, interceptors);
            context.BindTenantLease();
            return context;
        }

        internal static async Task<Store> CreateAsync(bool enabled)
        {
            var store = new Store();
            try
            {
                await using var context = store.Open();
                await context.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(context, CancellationToken.None);
                await using var human = new HumanSecuritySourceGate(context);
                await using var humanLease = await human.AcquireAsync();
                await using var discovery = new EventDiscoveryEnrollmentGate(context);
                await using var discoveryLease = await discovery.AcquireAsync();
                await LookupTableSeeder.SeedAsync(context);
                await EmailDispatchSqliteFixture.EnableInstanceEmailAsync(context);
                if (!enabled)
                {
                    var disabled = await EmailDispatchSqliteFixture.CreateInternalEmailSettingsWriter(context)
                        .DisableInternalAsync(null, null);
                    if (disabled.Status != EmailDeliverySettingsWriteStatus.Applied)
                        throw new InvalidOperationException("The real internal owner did not seed the disabled policy.");
                }
                return store;
            }
            catch { await store.DisposeAsync(); throw; }
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Observes entry to the real lock owner; no operation or authority is replaced.</summary>
    private sealed class ObservedKeys(ExploreDbContext context, ISettingMutationLock original, bool requireHuman)
        : ISettingMutationLock
    {
        internal int Entries { get; private set; }
        private void Observe()
        {
            Entries++;
            if (requireHuman) HumanSecuritySourceGate.RequireCurrent(context);
        }

        public Task<T> ExecuteAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            Observe();
            return original.ExecuteAsync(key, operation, cancellationToken);
        }

        public Task<T> ExecuteManyAsync<T>(IEnumerable<string> keys, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            Observe();
            return original.ExecuteManyAsync(keys, operation, cancellationToken);
        }

        public Task<T> ExecuteOrderedGroupsAsync<T>(IEnumerable<IEnumerable<string>> groups,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            Observe();
            return original.ExecuteOrderedGroupsAsync(groups, operation, cancellationToken);
        }
    }

    private sealed class GateSettlementProbe : DbTransactionInterceptor
    {
        internal bool ObservedBeforeBegin { get; private set; }
        internal bool ObservedCommit { get; private set; }
        private ExploreDbContext? originalContext;
        private DbConnection? connector;

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            HumanSecuritySourceGate.RequireCurrent(context);
            originalContext = context;
            connector = connection;
            ObservedBeforeBegin = true;
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            HumanSecuritySourceGate.RequireCurrent(originalContext!);
            if (!ObservedBeforeBegin || !ReferenceEquals(eventData.Context, originalContext)
                || !ReferenceEquals(transaction.Connection, connector)
                || !ReferenceEquals(originalContext!.Database.CurrentTransaction?.GetDbTransaction(), transaction))
                throw new InvalidOperationException("email_test_original_human_settlement_changed");
            ObservedCommit = true;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RejectedDmlProbe : DbCommandInterceptor
    {
        internal int Writes { get; private set; }
        private void Observe(DbCommand command)
        {
            string sql = command.CommandText.TrimStart();
            if (new[] { "INSERT ", "UPDATE ", "DELETE " }.Any(verb => sql.StartsWith(verb, StringComparison.OrdinalIgnoreCase)))
                Writes++;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }
    }
}
