using System.Data;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests;

/// <summary>Tracked save paths only; these cases do not certify bulk/native producer cooperation.</summary>
[NotInParallel]
public sealed class HumanSecuritySourceMutationGuardTests
{
    [Test]
    public async Task ImplicitSourceSaveAcquiresItsHumanGateBeforeItsOwnBegin()
    {
        await using var store = await Store.CreateAsync();
        var probe = new BeginProbe(requireGate: true);
        await using var writer = store.Open(probe);
        var permission = await writer.Permissions.SingleAsync();
        permission.IsActive = false;

        await writer.SaveChangesAsync();

        await Assert.That(probe.Observed).IsTrue();
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(writer)).IsFalse();
        await using var observer = store.Open();
        await Assert.That((await observer.Permissions.SingleAsync()).IsActive).IsFalse();
    }

    [Test]
    public async Task ExplicitSourceSaveWithoutPreBeginCustodyRefusesBeforeSourceDml()
    {
        await using var store = await Store.CreateAsync();
        var commands = new SourceDmlProbe(typeof(Permission));
        await using var writer = store.Open(commands);
        var permission = await writer.Permissions.SingleAsync();
        await using (var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            permission.IsActive = false;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());
            await Assert.That(refusal!.Message).IsEqualTo("human_security_source_gate_missing");
            await Assert.That(commands.Writes).IsEqualTo(0);
            await Assert.That(writer.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }
        await using var observer = store.Open();
        await Assert.That((await observer.Permissions.SingleAsync()).IsActive).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreparedActorAndPrivacyUserSavesCannotBypassOriginalHumanCustody(bool privacy)
    {
        await using var store = await Store.CreateAsync();
        var commands = new SourceDmlProbe(privacy ? typeof(User) : typeof(Actor));
        await using var writer = store.Open(commands);
        var user = await writer.Users.SingleAsync();
        var actor = await writer.Actors.SingleAsync();
        await using (var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            if (privacy) user.IsDeleted = true;
            else actor.IsSuspended = true;
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => privacy
                ? writer.SavePrivacyErasureChangesAsync(CancellationToken.None)
                : writer.SavePreparedAtprotoChangesAsync(CancellationToken.None));
            await Assert.That(refusal!.Message).IsEqualTo("human_security_source_gate_missing");
            await Assert.That(commands.Writes).IsEqualTo(0);
            await transaction.RollbackAsync();
        }
        await using var observer = store.Open();
        await Assert.That((await observer.Users.SingleAsync()).IsDeleted).IsFalse();
        await Assert.That((await observer.Actors.SingleAsync()).IsSuspended).IsFalse();
    }

    [Test]
    public async Task UnrelatedPresentationUpdateDoesNotAcquireTheSecurityGate()
    {
        await using var store = await Store.CreateAsync();
        var probe = new BeginProbe(requireGate: false);
        await using var writer = store.Open(probe);
        var permission = await writer.Permissions.SingleAsync();
        permission.FullName = "Changed presentation only";

        await writer.SaveChangesAsync();

        await Assert.That(probe.Observed).IsTrue();
        await using var observer = store.Open();
        await Assert.That((await observer.Permissions.SingleAsync()).FullName).IsEqualTo("Changed presentation only");
        await Assert.That((await observer.Permissions.SingleAsync()).IsActive).IsTrue();
    }

    [Test]
    public async Task GenuineOriginalGateRemainsHeldThroughExplicitSourceSaveAndRollback()
    {
        await using var store = await Store.CreateAsync();
        await using var writer = store.Open();
        var permission = await writer.Permissions.SingleAsync();
        await using var gate = new HumanSecuritySourceGate(writer);
        await using var held = await gate.AcquireAsync();
        var connector = writer.Database.GetDbConnection();
        await using (var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            permission.IsActive = false;
            await writer.SaveChangesAsync();
            gate.RequireCurrent();
            await Assert.That(writer.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(writer.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction().Connection).IsSameReferenceAs(connector);
            await transaction.RollbackAsync();
        }
        gate.RequireCurrent();
        await using var observer = store.Open();
        await Assert.That((await observer.Permissions.SingleAsync()).IsActive).IsTrue();
    }

    [Test]
    public async Task ImplicitSourceRetryAfterConnectorLossRefusesBeforeDml()
    {
        await using var store = await Store.CreateAsync();
        var interruption = new ReopenBeforeFirstBegin();
        var commands = new SourceDmlProbe(typeof(Permission));
        await using var writer = store.OpenRetrying(interruption, commands);
        var permission = await writer.Permissions.SingleAsync();
        permission.IsActive = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());

        await Assert.That(interruption.Begins).IsEqualTo(2);
        await using var observer = store.Open();
        await Assert.That((await observer.Permissions.SingleAsync()).IsActive).IsTrue();
        await Assert.That(commands.Writes).IsEqualTo(0);
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task CaughtPostSaveCustodyLossCannotCommitSourceMutation()
    {
        await using var store = await Store.CreateAsync();
        var release = new ReleaseHumanGateAfterSave();
        var commands = new SourceDmlProbe(typeof(Permission));
        await using var writer = store.Open(release, commands);
        await writer.Database.OpenConnectionAsync();
        var permission = await writer.Permissions.SingleAsync();
        await using var gate = new HumanSecuritySourceGate(writer);
        await using var held = await gate.AcquireAsync();
        release.Lease = held;
        InvalidOperationException? saveFailure = null;
        Exception? settlementFailure = null;

        try
        {
            await new EfCoreUnitOfWork(writer).ExecuteInTransactionAsync(async token =>
            {
                permission.IsActive = false;
                try
                {
                    await writer.SaveChangesAsync(token);
                }
                catch (InvalidOperationException failure)
                {
                    saveFailure = failure;
                    writer.ChangeTracker.Clear();
                }
            });
        }
        catch (Exception failure)
        {
            settlementFailure = failure;
        }

        await Assert.That(release.Released).IsTrue();
        await Assert.That(commands.Writes).IsEqualTo(1);
        await Assert.That(saveFailure?.Message).IsEqualTo("human_security_source_gate_missing");
        await using var observer = store.Open();
        await Assert.That((await observer.Permissions.SingleAsync()).IsActive).IsTrue();
        await Assert.That(settlementFailure is ConcurrencyConflictException).IsTrue();
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"human-source-{Guid.CreateVersion7():N}.db");

        internal ExploreDbContext Open(params IInterceptor[] interceptors)
        {
            var context = EmailDispatchSqliteFixture.CreateContext(path, interceptors);
            context.BindTenantLease();
            return context;
        }

        internal ExploreDbContext OpenRetrying(params IInterceptor[] interceptors)
        {
            using var template = Open();
            var options = new DbContextOptionsBuilder<ExploreDbContext>(
                (DbContextOptions<ExploreDbContext>)template.GetService<IDbContextOptions>())
                .ReplaceService<IExecutionStrategyFactory, ConnectorRetryStrategyFactory>()
                .AddInterceptors(interceptors);
            var context = new ExploreDbContext(options.Options);
            context.BindTenantLease();
            return context;
        }

        internal static async Task<Store> CreateAsync()
        {
            var store = new Store();
            try
            {
                await using var seed = store.Open();
                await seed.Database.EnsureCreatedAsync();
                await using var human = new HumanSecuritySourceGate(seed);
                await using var humanLease = await human.AcquireAsync();
                await using var lifecycle = await LocalIdentityLifecycleGateCustody.EnterAsync(seed, CancellationToken.None);
                await using var discovery = new EventDiscoveryEnrollmentGate(seed);
                await using var discoveryLease = await discovery.AcquireAsync();
                Guid userId = Guid.CreateVersion7();
                seed.AddRange(
                    new RoleScope { Id = (int)RoleScopeEnum.Platform, MasterCode = "platform", FullName = "Platform" },
                    new Permission
                    {
                        Id = 1,
                        MasterCode = "event:create",
                        ResourceKind = "event",
                        Action = "create",
                        FullName = "Create event",
                        GroupName = "Events",
                        RoleScopeId = (int)RoleScopeEnum.Platform,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new ActorType { Id = (int)ActorTypeEnum.User, MasterCode = "user", FullName = "User" },
                    new User
                    {
                        Id = userId,
                        Pii = new UserPii { UserId = userId, Email = "source@example.test", FirstName = "Source", LastName = "Test" },
                        ConcurrencyStamp = Guid.CreateVersion7(),
                        CreatedAt = DateTime.UtcNow
                    });
                await seed.SaveChangesAsync();
                seed.Add(new Actor
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userId,
                    ActorTypeId = (int)ActorTypeEnum.User,
                    ActorType = null!,
                    Pii = new ActorPii { DisplayName = "Source test" },
                    ConcurrencyStamp = Guid.CreateVersion7(),
                    CreatedAt = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
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

    private sealed class ConnectorRetryStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new ConnectorRetryStrategy(dependencies);
    }

    private sealed class ConnectorRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is ConnectorInterruptedException;
    }

    private sealed class ConnectorInterruptedException : Exception;

    private sealed class ReleaseHumanGateAfterSave : SaveChangesInterceptor
    {
        internal IAsyncDisposable? Lease { get; set; }
        internal bool Released { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Released && result > 0 && Lease is { } lease)
            {
                await lease.DisposeAsync();
                Released = true;
            }
            return result;
        }
    }

    private sealed class ReopenBeforeFirstBegin : DbTransactionInterceptor
    {
        internal int Begins { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Begins++;
            if (Begins == 1)
            {
                HumanSecuritySourceGate.RequireCurrent((ExploreDbContext)eventData.Context!);
                await connection.CloseAsync();
                await connection.OpenAsync(cancellationToken);
                throw new ConnectorInterruptedException();
            }
            return result;
        }
    }

    private sealed class BeginProbe(bool requireGate) : DbTransactionInterceptor
    {
        internal bool Observed { get; private set; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context)
            {
                if (requireGate) HumanSecuritySourceGate.RequireCurrent(context);
                else if (HumanSecuritySourceGate.TryRequireCurrent(context))
                    throw new InvalidOperationException("An unrelated presentation update acquired Human custody.");
                Observed = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SourceDmlProbe(Type source) : DbCommandInterceptor
    {
        internal int Writes { get; private set; }
        private void Observe(DbCommand command, CommandEventData eventData)
        {
            if (eventData.Context is not ExploreDbContext context) return;
            string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Model.FindEntityType(source)!.GetTableName()!);
            if (command.CommandText.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(table, StringComparison.Ordinal)) Writes++;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }
    }
}
