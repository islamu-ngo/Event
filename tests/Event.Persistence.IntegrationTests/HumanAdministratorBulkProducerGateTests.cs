using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantUsers.Handlers.Commands;
using Explore.Application.Features.TenantUsers.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Management;
using Explore.Application.Management;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Exercises the real command/repositories and physical SQLite transactions, not HTTP authentication.
/// The Human gate is cooperative source coordination, never a substitute for the command's authorization.
/// </summary>
[NotInParallel]
public sealed class HumanAdministratorBulkProducerGateTests
{
    [Test]
    public async Task LifecycleCommandHoldsHumanBeforeBeginAndItsActualSettingKeyAcrossStatusAndAudit()
    {
        await using var store = await Store.CreateAsync();
        var probe = new ProducerProbe();
        var keys = new LifecycleKeyProbe(store.TenantId);
        await using var writer = store.Open(probe, keys);
        keys.ExpectedContext = writer;
        await using var services = store.Services(writer);
        var unitOfWork = new EfCoreUnitOfWork(writer);
        var mutationLock = new RelationalSettingMutationLock(writer, unitOfWork);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tenantRepository = new TenantRepository(writer);
        var capacity = new TenantActivationCapacityPolicy(new InstanceBootstrapStateRepository(writer), tenantRepository,
            new ManagedTenantProvisioningOperationRepository(writer), Options.Create(new ManagedControlPlaneOptions { Enabled = false }));
        var readiness = new TenantDirectoryOperatorReadinessEvaluator(new TypedSettingsDocumentResolver(
            new TenantSettingsDocumentRepository(writer), cache));
        var handler = ActivatorUtilities.CreateInstance<TransitionInstanceTenantLifecycleCommandHandler>(services,
            tenantRepository, new TenantLifecycleLogRepository(writer), new EmailDispatchOutboxRepository(writer),
            mutationLock, capacity, readiness);

        var result = await handler.ExecuteAsync(new TransitionInstanceTenantLifecycleCommand(
            store.TenantId, TenantStatusEnum.Suspended, "Source revocation control"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(keys.ObservedStatusSource).IsTrue();
        await Assert.That(keys.ObservedAudit).IsTrue();
        await Assert.That(probe.ReachedCommit).IsTrue();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(writer)).IsFalse();
        await using var observer = store.Open();
        await Assert.That((await observer.Tenants.SingleAsync(row => row.Id == store.TenantId)).TenantStatusId)
            .IsEqualTo((int)TenantStatusEnum.Suspended);
        var audit = await observer.TenantLifecycleLogs.SingleAsync(row => row.TenantId == store.TenantId);
        await Assert.That(audit.OldStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        await Assert.That(audit.NewStatusId).IsEqualTo((int)TenantStatusEnum.Suspended);
        await Assert.That(audit.TransitionedByUserId).IsEqualTo(store.AdministratorId);
    }

    [Test]
    public async Task WaitingMembershipRemovalRechecksTheAdministratorsRevokedGrantBeforeAnyTargetWrite()
    {
        await using var store = await Store.CreateAsync();
        await using var reader = store.Open();
        await using var readerGate = new HumanSecuritySourceGate(reader);
        await using var held = await readerGate.AcquireAsync();
        await Assert.That(await new TenantUserRoleGrantRepository(reader)
            .IsTenantAdminInCurrentTenantAsync(store.TenantId, store.AdministratorId)).IsTrue();

        var probe = new ProducerProbe();
        await using var writer = store.Open(probe);
        await using var services = store.Services(writer);
        var handler = ActivatorUtilities.CreateInstance<RemoveTenantMembershipCommandHandler>(services);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<bool> removal = handler.ExecuteAsync(new(store.TenantId, store.MemberId), timeout.Token);
        try
        {
            await RequireWaitingBeforeBeginAsync(removal, probe, timeout.Token);
            var originalGrant = await reader.TenantUserRoleGrants.SingleAsync(row => row.Id == store.AdministratorGrantId);
            originalGrant.RevokedAt = DateTime.UtcNow;
            originalGrant.RevokedBy = store.AdministratorId;
            await reader.SaveChangesAsync(timeout.Token);
            readerGate.RequireCurrent();
            await held.DisposeAsync();
            await Assert.ThrowsAsync<AuthorizationException>(async () => await removal);
        }
        finally
        {
            await held.DisposeAsync();
            await ObserveCompletionAsync(removal);
        }

        await Assert.That(probe.Begins).IsEqualTo(1);
        await Assert.That(probe.SourceWrites).IsEqualTo(0);
        await using var observer = store.Open();
        await Assert.That(await new TenantUserRoleGrantRepository(observer)
            .IsTenantAdminInCurrentTenantAsync(store.TenantId, store.AdministratorId)).IsFalse();
        await Assert.That(await observer.TenantUsers.AnyAsync(row => row.Id == store.MemberMembershipId && !row.IsDeleted)).IsTrue();
    }

    [Test]
    public async Task ImplicitStatusTransitionWaitsForTheReadersHumanGateBeforeBeginningAndPublishesTheNewStatus()
    {
        await using var store = await Store.CreateAsync();
        await using var reader = store.Open();
        await using var readerGate = new HumanSecuritySourceGate(reader);
        await using var held = await readerGate.AcquireAsync();
        await Assert.That((await reader.Tenants.AsNoTracking().SingleAsync(row => row.Id == store.TenantId)).TenantStatusId)
            .IsEqualTo((int)TenantStatusEnum.Active);
        var probe = new ProducerProbe();
        await using var writer = store.Open(probe);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<bool> transition = TransitionAsync(writer, store, timeout.Token);
        try
        {
            await RequireWaitingBeforeBeginAsync(transition, probe, timeout.Token);
            await held.DisposeAsync();
            await Assert.That(await transition).IsTrue();
        }
        finally
        {
            await held.DisposeAsync();
            await ObserveCompletionAsync(transition);
        }
        await Assert.That(probe.Begins).IsEqualTo(1);
        await Assert.That(probe.SourceWrites).IsEqualTo(1);
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(writer)).IsFalse();
        await using var observer = store.Open();
        await using var observerGate = new HumanSecuritySourceGate(observer);
        await using var observed = await observerGate.AcquireAsync();
        await Assert.That((await observer.Tenants.AsNoTracking().SingleAsync(row => row.Id == store.TenantId)).TenantStatusId)
            .IsEqualTo((int)TenantStatusEnum.Suspended);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CallerOwnedBulkMutationWithoutHumanCustodyRefusesBeforeDml(bool membership)
    {
        await using var store = await Store.CreateAsync();
        var probe = new ProducerProbe(requireAtBegin: false);
        await using var writer = store.Open(probe);
        await using (var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => membership
                ? new TenantUserRepository(writer).TryRemoveMembershipAsync(store.TenantId, store.MemberId,
                    store.AdministratorId, DateTime.UtcNow, CancellationToken.None)
                : TransitionAsync(writer, store, CancellationToken.None));
            await Assert.That(refusal!.Message).IsEqualTo("human_security_source_gate_missing");
            await Assert.That(probe.SourceWrites).IsEqualTo(0);
            await Assert.That(writer.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }
        await store.AssertUnchangedAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LateSettlementFailureRollsBackBulkEffectsAndRetainsBorrowedHumanCustody(bool membership)
    {
        await using var store = await Store.CreateAsync();
        var probe = new ProducerProbe(failAtCommit: true);
        await using var writer = store.Open(probe);
        await using var gate = new HumanSecuritySourceGate(writer);
        await using var held = await gate.AcquireAsync();
        var connector = writer.Database.GetDbConnection();
        await Assert.ThrowsAsync<DeliberateSettlementFailure>(() => new EfCoreUnitOfWork(writer)
            .ExecuteInTransactionAsync(async token =>
            {
                bool changed = membership
                    ? await new TenantUserRepository(writer).TryRemoveMembershipAsync(store.TenantId, store.MemberId,
                        store.AdministratorId, DateTime.UtcNow, token)
                    : await TransitionAsync(writer, store, token);
                await Assert.That(changed).IsTrue();
                gate.RequireCurrent();
                await Assert.That(writer.Database.GetDbConnection()).IsSameReferenceAs(connector);
            }));
        await Assert.That(probe.ReachedCommit).IsTrue();
        await Assert.That(probe.SourceWrites > 0).IsTrue();
        gate.RequireCurrent();
        await Assert.That(writer.Database.GetDbConnection()).IsSameReferenceAs(connector);
        await Assert.That(writer.Database.CurrentTransaction).IsNull();
        await store.AssertUnchangedAsync();
    }

    private static Task<bool> TransitionAsync(ExploreDbContext context, Store store, CancellationToken token) =>
        new TenantRepository(context).TryTransitionStatusAsync(store.TenantId, (int)TenantStatusEnum.Active,
            (int)TenantStatusEnum.Suspended, DateTime.UtcNow, store.AdministratorId, token);

    private static async Task RequireWaitingBeforeBeginAsync(Task operation, ProducerProbe probe, CancellationToken token)
    {
        // A completed invocation is surfaced directly so a missing-custody exception
        // cannot masquerade as successful contention. The short interval witnesses
        // the actual blocked invocation; the release below must then let it settle.
        Task observation = Task.Delay(TimeSpan.FromMilliseconds(150), token);
        if (await Task.WhenAny(operation, observation) == operation) await operation;
        await observation;
        await Assert.That(operation.IsCompleted).IsFalse();
        await Assert.That(probe.Begins).IsEqualTo(0);
        await Assert.That(probe.SourceWrites).IsEqualTo(0);
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        // Cleanup only: the body above owns all success/refusal assertions.
        try { await operation.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (Exception) when (operation.IsCompleted) { }
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"human-bulk-{Guid.CreateVersion7():N}.db");
        internal Guid TenantId { get; } = Guid.CreateVersion7();
        internal Guid AdministratorId { get; } = Guid.CreateVersion7();
        internal Guid MemberId { get; } = Guid.CreateVersion7();
        internal Guid MemberMembershipId { get; } = Guid.CreateVersion7();
        internal Guid AdministratorGrantId { get; } = Guid.CreateVersion7();
        private long _initialRevision;

        internal ExploreDbContext Open(params IInterceptor[] interceptors) => OpenCore(TenantId, interceptors);

        private ExploreDbContext OpenCore(Guid? tenantId, params IInterceptor[] interceptors)
        {
            IInterceptor[] observers = interceptors.SelectMany(interceptor => interceptor is ProducerProbe probe
                ? new IInterceptor[] { probe, probe.Commands }
                : [interceptor]).ToArray();
            var context = EmailDispatchSqliteFixture.CreateContext(_path, observers);
            context.TenantContext = tenantId is { } selected ? new SelectedTenant(selected) : null;
            context.BindTenantLease();
            return context;
        }

        internal ServiceProvider Services(ExploreDbContext context)
        {
            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", AdministratorId.ToString("D"))], "bulk-source-test"))
                }
            };
            var services = new ServiceCollection();
            services.AddSingleton<ITenantUserRepository>(new TenantUserRepository(context));
            services.AddSingleton<ITenantUserRoleGrantRepository>(new TenantUserRoleGrantRepository(context));
            services.AddSingleton<IUnitOfWork>(new EfCoreUnitOfWork(context));
            services.AddSingleton<ITenantContext>(new SelectedTenant(TenantId));
            services.AddSingleton<ICurrentUserService>(new CurrentUserService(accessor));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IHumanSecuritySourceGate>(new HumanSecuritySourceGate(context));
            services.AddSingleton<Func<IHumanSecuritySourceGate>>(provider => () => provider.GetRequiredService<IHumanSecuritySourceGate>());
            return services.BuildServiceProvider();
        }

        internal static async Task<Store> CreateAsync()
        {
            var store = new Store();
            try
            {
                // This is the original global seed owner, not a live selected
                // tenant lease being rebound to escape tracked authority.
                await using var seed = store.OpenCore(null);
                await seed.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(seed, CancellationToken.None);
                await using var gate = new HumanSecuritySourceGate(seed);
                await using var held = await gate.AcquireAsync();
                await using var enrollment = new EventDiscoveryEnrollmentGate(seed);
                await using var enrolled = await enrollment.AcquireAsync();
                var tenant = new Tenant
                {
                    Id = store.TenantId,
                    FullName = "Bulk source tenant",
                    Slug = $"bulk-{store.TenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                };
                seed.AddRange(
                    new TenantStatus { Id = (int)TenantStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true },
                    new TenantStatus { Id = (int)TenantStatusEnum.Suspended, MasterCode = "SUSPENDED", FullName = "Suspended", IsActiveState = false },
                    new RoleScope { Id = (int)RoleScopeEnum.Tenant, MasterCode = "TENANT", FullName = "Tenant" },
                    new Role { Id = (int)RoleEnum.TenantAdmin, MasterCode = "tenant.admin", FullName = "Administrator", Scope = RoleScopeEnum.Tenant },
                    new Role { Id = (int)RoleEnum.TenantMember, MasterCode = "tenant.member", FullName = "Member", Scope = RoleScopeEnum.Tenant },
                    User(store.AdministratorId, "administrator"), User(store.MemberId, "member"), tenant);
                await seed.SaveChangesAsync();
                var adminMembership = Membership(store.TenantId, store.AdministratorId, Guid.CreateVersion7());
                var membership = Membership(store.TenantId, store.MemberId, store.MemberMembershipId);
                seed.AddRange(adminMembership, membership,
                    Grant(store.AdministratorGrantId, adminMembership, (int)RoleEnum.TenantAdmin),
                    Grant(Guid.CreateVersion7(), membership, (int)RoleEnum.TenantMember),
                    new TenantUserProfile
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = store.TenantId,
                        Tenant = null!,
                        TenantUserId = membership.Id,
                        TenantUser = null!,
                        DisplayNameOverride = "Target profile",
                        CreatedAt = DateTime.UtcNow
                    });
                await seed.SaveChangesAsync();
                // Revision metadata retains its selected-tenant filter. Observe
                // the committed seed with a fresh exact lease, never by rebinding
                // the global seed owner or bypassing its named query filter.
                await using var revisionObserver = store.Open();
                store._initialRevision = (await revisionObserver.Set<EventDiscoveryRevision>()
                    .SingleAsync(row => row.TenantId == store.TenantId)).DisclosureEpoch;
                return store;
            }
            catch { await store.DisposeAsync(); throw; }
        }

        internal async Task AssertUnchangedAsync()
        {
            await using var observer = Open();
            await Assert.That((await observer.Tenants.SingleAsync(row => row.Id == TenantId)).TenantStatusId).IsEqualTo((int)TenantStatusEnum.Active);
            var member = await observer.TenantUsers.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).SingleAsync(row => row.Id == MemberMembershipId);
            await Assert.That(member.IsDeleted).IsFalse();
            await Assert.That(member.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
            await Assert.That(await observer.TenantUserProfiles.CountAsync(row => row.TenantUserId == MemberMembershipId)).IsEqualTo(1);
            await Assert.That(await observer.TenantUserRoleGrants.CountAsync(row => row.TenantUserId == MemberMembershipId && row.RevokedAt == null)).IsEqualTo(1);
            await Assert.That((await observer.Set<EventDiscoveryRevision>().SingleAsync(row => row.TenantId == TenantId)).DisclosureEpoch).IsEqualTo(_initialRevision);
        }

        private static User User(Guid id, string label) => new()
        {
            Id = id,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { UserId = id, Email = $"{label}-{id:N}@example.test", FirstName = label, LastName = "Bulk" }
        };
        private static TenantUser Membership(Guid tenantId, Guid userId, Guid id) => new()
        {
            Id = id,
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        private static TenantUserRoleGrant Grant(Guid id, TenantUser membership, int roleId) => new()
        {
            Id = id,
            TenantId = membership.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = null!,
            RoleId = roleId,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
            return ValueTask.CompletedTask;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed class DeliberateSettlementFailure : Exception;

    private sealed class ProducerProbe(bool requireAtBegin = true, bool failAtCommit = false) : DbTransactionInterceptor
    {
        internal SourceDmlProbe Commands { get; } = new();
        internal int Begins { get; private set; }
        internal int SourceWrites => Commands.Writes;
        internal bool ReachedCommit { get; private set; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Begins++;
            if (requireAtBegin) HumanSecuritySourceGate.RequireCurrent((ExploreDbContext)eventData.Context!);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            HumanSecuritySourceGate.RequireCurrent(context);
            if (!ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !ReferenceEquals(transaction.Connection, context.Database.GetDbConnection()))
                throw new InvalidOperationException("The producer changed its original physical transaction.");
            ReachedCommit = true;
            if (failAtCommit) throw new DeliberateSettlementFailure();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SourceDmlProbe : DbCommandInterceptor
    {
        internal int Writes { get; private set; }

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            string sql = command.CommandText.TrimStart();
            if (eventData.Context is not ExploreDbContext context
                || (!sql.StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase)
                    && !sql.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase))) return;
            var generation = context.GetService<ISqlGenerationHelper>();
            Type[] sources = [typeof(Tenant), typeof(TenantUser), typeof(TenantUserProfile), typeof(TenantUserRoleGrant)];
            if (sources.Any(source =>
            {
                var entity = context.Model.FindEntityType(source)!;
                if (!sql.Contains(generation.DelimitIdentifier(entity.GetTableName()!), StringComparison.Ordinal)) return false;
                if (source != typeof(Tenant)) return true;
                var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                string status = generation.DelimitIdentifier(entity.FindProperty(nameof(Tenant.TenantStatusId))!.GetColumnName(table)!);
                return sql.Contains(status, StringComparison.Ordinal);
            })) Writes++;
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

    private sealed class LifecycleKeyProbe(Guid tenantId) : DbCommandInterceptor
    {
        internal ExploreDbContext? ExpectedContext { get; set; }
        internal bool ObservedStatusSource { get; private set; }
        internal bool ObservedAudit { get; private set; }
        private DbTransaction? _originalTransaction;
        private DbConnection? _originalConnection;

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            if (eventData.Context is not ExploreDbContext context) return;
            var generation = context.GetService<ISqlGenerationHelper>();
            string tenantTable = generation.DelimitIdentifier(context.Model.FindEntityType(typeof(Tenant))!.GetTableName()!);
            string auditTable = generation.DelimitIdentifier(context.Model.FindEntityType(typeof(TenantLifecycleLog))!.GetTableName()!);
            bool tenant = command.CommandText.Contains(tenantTable, StringComparison.Ordinal);
            bool audit = command.CommandText.Contains(auditTable, StringComparison.Ordinal);
            if (!tenant && !audit) return;

            HumanSecuritySourceGate.RequireCurrent(context);
            var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("Lifecycle source observation escaped its original transaction.");
            _originalTransaction ??= transaction;
            _originalConnection ??= command.Connection;
            string key = RelationalSettingMutationLock.NormalizeSettingKeys(
                [TenantDirectoryOperatorIdentityMutationLockKeys.ForTenant(tenantId)]).Single();
            if (!ReferenceEquals(context, ExpectedContext)
                || !ReferenceEquals(transaction, _originalTransaction)
                || !ReferenceEquals(command.Transaction, transaction)
                || !ReferenceEquals(command.Connection, _originalConnection)
                || !ReferenceEquals(command.Connection, context.Database.GetDbConnection())
                || !SqliteNamedLockTransactionInterceptor.Instance.IsTracked(transaction, $"explore:setting-mutation:{key}"))
                throw new InvalidOperationException("Lifecycle source/audit lost its original transaction-bound identity key.");
            ObservedStatusSource |= tenant;
            ObservedAudit |= audit;
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
