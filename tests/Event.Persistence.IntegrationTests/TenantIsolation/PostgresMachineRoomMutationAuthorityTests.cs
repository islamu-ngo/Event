using System.Data.Common;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Rows = Event.Persistence.IntegrationTests.TenantIsolation.PostgresAuthenticationLookupAuthorityTests.AuthenticationRows;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresMachineRoomMutationAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(ExternalApiKeyOwnerType.User, false)]
    [Arguments(ExternalApiKeyOwnerType.User, true)]
    [Arguments(ExternalApiKeyOwnerType.Tenant, false)]
    [Arguments(ExternalApiKeyOwnerType.Tenant, true)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, false)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, true)]
    public async Task OrdinaryRuntimeRoomAuthorityHoldsRoomAndLocationsThroughCommit(ExternalApiKeyOwnerType owner, bool move)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var state = await RoomRows.CreateAsync(fixture, rows, owner);
        await using var db = state.Context();
        await rows.AssertOrdinaryRoleAsync(db);
        var authority = state.Authority(db, TimeProvider.System);

        await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
        {
            var room = await authority.GetAuthorizedRoomForUpdateAsync(rows.A, state.RoomId, move ? state.DestinationId : null, token);
            await Assert.That(room?.Id).IsEqualTo(state.RoomId);
            await using var contender = fixture.CreateDbContext();
            var conflict = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                $"SELECT id FROM {state.RoomTable} WHERE id = {{0}} FOR UPDATE NOWAIT", [state.RoomId], token));
            await Assert.That(conflict!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            var parentConflict = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                $"SELECT id FROM {state.LocationTable} WHERE id = {{0}} FOR UPDATE NOWAIT", [move ? state.DestinationId : state.SourceId], token));
            await Assert.That(parentConflict!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await Assert.That(await db.ExternalApiKeys.IgnoreQueryFilters()
                .AnyAsync(key => key.Id == rows.PlatformKey.Id, token)).IsFalse();
            var transaction = db.Database.CurrentTransaction!;
            await transaction.CreateSavepointAsync("runtime_private_key_denial", token);
            try
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                    db.ExternalApiKeys.IgnoreQueryFilters().Where(key => key.Id == rows.PlatformKey.Id)
                        .Select(key => key.SecretHash).SingleOrDefaultAsync(token));
                await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            finally
            {
                await transaction.RollbackToSavepointAsync("runtime_private_key_denial", token);
                await transaction.ReleaseSavepointAsync("runtime_private_key_denial", token);
            }
            return true;
        });
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("scope")]
    [Arguments("owner")]
    [Arguments("expired")]
    [Arguments("snapshot-scope")]
    [Arguments("foreign-destination")]
    public async Task FixedRoomPurposeRejectsCurrentKeyAndTargetDrift(string drift)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var state = await RoomRows.CreateAsync(fixture, rows, ExternalApiKeyOwnerType.InstanceAdmin);
        await using var db = state.Context();
        if (drift == "snapshot-scope") state.Key.Scopes = ExternalApiKeyScopes.EventsRead;
        var authority = state.Authority(db, TimeProvider.System);
        await using (var change = fixture.CreateDbContext())
        {
            change.BindTenantLease();
            var key = await change.ExternalApiKeys.SingleAsync(row => row.Id == state.Key.Id);
            switch (drift)
            {
                case "revoked": key.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked; break;
                case "scope": key.Scopes = ExternalApiKeyScopes.EventsRead; break;
                case "owner": key.OwnerId = rows.OtherUserId; break;
                case "expired": key.ExpiresAt = DateTime.UtcNow.AddDays(-1); break;
                case "foreign-destination":
                    await change.Locations.Where(location => location.Id == state.DestinationId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(location => location.TenantId, rows.B));
                    break;
            }
            await change.SaveChangesAsync();
        }

        var room = await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(
            token => authority.GetAuthorizedRoomForUpdateAsync(rows.A, state.RoomId, state.DestinationId, token));

        await Assert.That(room).IsNull();
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    public async Task RoomExpiryIsCheckedAfterTheLastLocationAcquisition(ExternalApiKeyOwnerType owner)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var state = await RoomRows.CreateAsync(fixture, rows, owner);
        var clock = new RoomClock(DateTimeOffset.UtcNow);
        state.Key.ExpiresAt = clock.GetUtcNow().AddHours(1).UtcDateTime;
        Guid lastLocation = new[] { state.SourceId, state.DestinationId }.Order().Last();
        var entered = new RoomAcquisitionSignal(state.LocationTable, owner == ExternalApiKeyOwnerType.InstanceAdmin, lastLocation);
        await using var db = state.Context(entered);
        var authority = state.Authority(db, clock);
        await using var blocker = fixture.CreateDbContext();
        await using var held = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync(
            $"SELECT id FROM {state.LocationTable} WHERE id = {{0}} FOR UPDATE", lastLocation);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task signal = entered.Entered.Task;

        var decision = new EfCoreUnitOfWork(db).ExecuteSerializableAsync(
            token => authority.GetAuthorizedRoomForUpdateAsync(rows.A, state.RoomId, state.DestinationId, token), deadline.Token);
        try
        {
            await signal.WaitAsync(deadline.Token);
            await Assert.That(decision.IsCompleted).IsFalse();
            clock.Set(state.Key.ExpiresAt.Value.AddTicks(1));
        }
        finally
        {
            await held.RollbackAsync(deadline.Token);
        }

        await Assert.That(await decision.WaitAsync(deadline.Token)).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommittedDestinationOrRoomLifecycleDriftCannotUseAnEarlierSnapshot(bool roomDrift)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var state = await RoomRows.CreateAsync(fixture, rows, ExternalApiKeyOwnerType.InstanceAdmin);
        await using var blocker = fixture.CreateDbContext();
        await using var held = await blocker.Database.BeginTransactionAsync();
        if (roomDrift)
            await blocker.LocationRooms.Where(room => room.Id == state.RoomId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(room => room.IsDeleted, true));
        else
            await blocker.Locations.Where(location => location.Id == state.DestinationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(location => location.TenantId, rows.B));
        var entered = new RoomAcquisitionSignal(roomDrift ? state.RoomTable : state.LocationTable, true);
        await using var db = state.Context(entered);
        var authority = state.Authority(db, TimeProvider.System);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task signal = entered.Entered.Task;

        var decision = new EfCoreUnitOfWork(db).ExecuteSerializableAsync(
            token => authority.GetAuthorizedRoomForUpdateAsync(rows.A, state.RoomId, state.DestinationId, token), deadline.Token);
        await signal.WaitAsync(deadline.Token);
        await held.CommitAsync(deadline.Token);

        await Assert.That(await decision.WaitAsync(deadline.Token)).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RoomExecutorCannotBorrowFooterOrAuthenticationExecution(bool footer)
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var state = await RoomRows.CreateAsync(fixture, rows, ExternalApiKeyOwnerType.InstanceAdmin);
        await using var admin = fixture.CreateDbContext();
        await PostgresMachineMutationAuthorityContract.ApplyAsync(admin);
        await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(admin, fixture.NativeQueueEnrollment);
        await using var db = state.Context();
        await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
        {
            await PostgresMachineRoomMutationAuthorityContract.RequireReadyAsync(db, token);
            return true;
        });
        var sql = new InstanceApiKeyCapabilitySql(db);
        var function = PostgresMachineMutationAuthorityContract.Function(db);
        string signature = sql.Function(function.Name) + "(" + function.ArgumentTypes + ")";
        if (footer)
            await admin.Database.ExecuteSqlRawAsync(
                $"GRANT EXECUTE ON FUNCTION {signature} TO {PostgresMachineRoomMutationAuthorityContract.RuntimeRole}");
        else
            await admin.Database.ExecuteSqlRawAsync(
                $"GRANT SELECT ({sql.Column<ExternalApiKey>(nameof(ExternalApiKey.SecretHash))}) ON {sql.Table<ExternalApiKey>()} TO {PostgresMachineRoomMutationAuthorityContract.RuntimeRole}");
        try
        {
            if (!footer)
                await Assert.That(await admin.Database.SqlQuery<bool>($"""
                    SELECT pg_catalog.has_any_column_privilege(
                        {PostgresMachineRoomMutationAuthorityContract.RuntimeRole}, {sql.Table<ExternalApiKey>()}, 'SELECT') AS "Value"
                    """).SingleAsync()).IsTrue();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
                {
                    await PostgresMachineRoomMutationAuthorityContract.RequireReadyAsync(db, token);
                    return true;
                }));
        }
        finally
        {
            if (footer)
                await admin.Database.ExecuteSqlRawAsync(
                    $"REVOKE EXECUTE ON FUNCTION {signature} FROM {PostgresMachineRoomMutationAuthorityContract.RuntimeRole}");
            else
                await admin.Database.ExecuteSqlRawAsync(
                    $"REVOKE SELECT ({sql.Column<ExternalApiKey>(nameof(ExternalApiKey.SecretHash))}) ON {sql.Table<ExternalApiKey>()} FROM {PostgresMachineRoomMutationAuthorityContract.RuntimeRole}");
        }
    }

    [Test]
    public async Task FooterExecutorCannotBorrowRoomExecutionEvenWhenLoginHasBothPurposes()
    {
        await using var rows = await Rows.CreateAsync(fixture);
        await using var state = await RoomRows.CreateAsync(fixture, rows, ExternalApiKeyOwnerType.InstanceAdmin);
        await using var admin = fixture.CreateDbContext();
        await PostgresMachineMutationAuthorityContract.ApplyAsync(admin);
        string login = $"\"{rows.RuntimeOptions.Username}\"";
        bool ownsFooterMembership = !await rows.HasDirectPurposeMembershipAsync(
            admin, PostgresMachineMutationAuthorityContract.RuntimeRole);
        if (ownsFooterMembership)
            await admin.Database.ExecuteSqlRawAsync(
                $"GRANT {PostgresMachineMutationAuthorityContract.RuntimeRole} TO {login} WITH INHERIT TRUE, SET FALSE");
        await using var db = state.Context();
        var sql = new InstanceApiKeyCapabilitySql(db);
        var function = PostgresMachineRoomMutationAuthorityContract.Function(db);
        string signature = sql.Function(function.Name) + "(" + function.ArgumentTypes + ")";
        bool ownsRoomExecutionGrant = false;
        try
        {
            await new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
            {
                await PostgresMachineMutationAuthorityContract.RequireReadyAsync(db, token);
                await PostgresMachineRoomMutationAuthorityContract.RequireReadyAsync(db, token);
                return true;
            });
            await admin.Database.ExecuteSqlRawAsync(
                $"GRANT EXECUTE ON FUNCTION {signature} TO {PostgresMachineMutationAuthorityContract.RuntimeRole}");
            ownsRoomExecutionGrant = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new EfCoreUnitOfWork(db).ExecuteSerializableAsync(async token =>
                {
                    await PostgresMachineMutationAuthorityContract.RequireReadyAsync(db, token);
                    return true;
                }));
        }
        finally
        {
            if (ownsRoomExecutionGrant)
                await admin.Database.ExecuteSqlRawAsync(
                    $"REVOKE EXECUTE ON FUNCTION {signature} FROM {PostgresMachineMutationAuthorityContract.RuntimeRole}");
            if (ownsFooterMembership)
                await admin.Database.ExecuteSqlRawAsync(
                    $"REVOKE {PostgresMachineMutationAuthorityContract.RuntimeRole} FROM {login}");
        }
    }

    private sealed class RoomClock(DateTimeOffset initial) : TimeProvider
    {
        private long _ticks = initial.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        internal void Set(DateTime time) => Interlocked.Exchange(ref _ticks, time.Ticks);
    }

    private sealed class RoomAcquisitionSignal(string table, bool instance, Guid? target = null) : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private void Observe(DbCommand command)
        {
            if (instance ? command.CommandText.Contains("machine_room_update_authority", StringComparison.Ordinal)
                : command.CommandText.Contains(table, StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                    && (target is null || command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == target)))
                Entered.TrySetResult();
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RoomRows(PostgreSqlContainerFixture fixture, Rows rows, ExternalApiKey key,
        Guid roomId, Guid sourceId, Guid destinationId, string roomTable, string locationTable,
        bool ownsPurposeMembership) : IAsyncDisposable
    {
        internal ExternalApiKey Key { get; } = key;
        internal Guid RoomId { get; } = roomId;
        internal Guid SourceId { get; } = sourceId;
        internal Guid DestinationId { get; } = destinationId;
        internal string RoomTable { get; } = roomTable;
        internal string LocationTable { get; } = locationTable;

        internal static async Task<RoomRows> CreateAsync(PostgreSqlContainerFixture fixture, Rows rows, ExternalApiKeyOwnerType owner)
        {
            await using var seed = fixture.CreateDbContext();
            seed.BindTenantLease();
            await PostgresMachineRoomMutationAuthorityContract.ApplyAsync(seed);
            var key = await seed.ExternalApiKeys.SingleAsync(row => row.Id ==
                (owner == ExternalApiKeyOwnerType.InstanceAdmin ? rows.PlatformKey.Id : rows.KeyA.Id));
            key.OwnerType = owner;
            key.OwnerId = owner == ExternalApiKeyOwnerType.Tenant ? rows.A : rows.UserId;
            key.Scopes = owner == ExternalApiKeyOwnerType.InstanceAdmin ? ExternalApiKeyScopes.AdminInstance : ExternalApiKeyScopes.AdminTenant;
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.UserId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            var source = new Location { Id = Guid.CreateVersion7(), TenantId = rows.A, FullName = "Source", City = "Brussels", Country = "BE" };
            var destination = new Location { Id = Guid.CreateVersion7(), TenantId = rows.A, FullName = "Destination", City = "Brussels", Country = "BE" };
            var room = new LocationRoom
            {
                Id = Guid.CreateVersion7(),
                TenantId = rows.A,
                Tenant = null!,
                LocationId = source.Id,
                Location = source,
                Name = "Room",
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            seed.AddRange(source, destination, room);
            await seed.SaveChangesAsync();
            var helper = seed.GetService<ISqlGenerationHelper>();
            var roomEntity = seed.Model.FindEntityType(typeof(LocationRoom))!;
            var locationEntity = seed.Model.FindEntityType(typeof(Location))!;
            string roomTable = helper.DelimitIdentifier(roomEntity.GetTableName()!, roomEntity.GetSchema());
            string locationTable = helper.DelimitIdentifier(locationEntity.GetTableName()!, locationEntity.GetSchema());
            string login = $"\"{rows.RuntimeOptions.Username}\"";
            bool ownsPurposeMembership = !await rows.HasDirectPurposeMembershipAsync(
                seed, PostgresMachineRoomMutationAuthorityContract.RuntimeRole);
            if (ownsPurposeMembership)
                await seed.Database.ExecuteSqlRawAsync(
                    $"GRANT {PostgresMachineRoomMutationAuthorityContract.RuntimeRole} TO {login} WITH INHERIT TRUE, SET FALSE");
            return new(fixture, rows, key, room.Id, source.Id, destination.Id, roomTable, locationTable, ownsPurposeMembership);
        }

        internal ExploreDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, rows.RuntimeOptions);
            options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var context = new ExploreDbContext(options.Options) { TenantContext = new TenantScope(rows.A) };
            context.BindTenantLease();
            return context;
        }

        internal MachineMutationCommitAuthority Authority(ExploreDbContext db, TimeProvider clock)
        {
            var claims = new List<Claim>
            {
                new(ApiAuthenticationClaimTypes.AuthMethod, "api_key"), new(ApiAuthenticationClaimTypes.ApiKeyId, Key.KeyId),
                new(ApiAuthenticationClaimTypes.OwnerType, Key.OwnerType.ToString()), new(ApiAuthenticationClaimTypes.OwnerId, Key.OwnerId.ToString("D"))
            };
            if (Key.TenantId is { } tenant) claims.Add(new(ApiAuthenticationClaimTypes.TenantId, tenant.ToString("D")));
            if (Key.OwnerType == ExternalApiKeyOwnerType.User) claims.Add(new(PlatformIdentityClaimTypes.InternalUserId, Key.OwnerId.ToString("D")));
            claims.AddRange(Key.Scopes.Split(' ').Select(value => new Claim(ApiAuthenticationClaimTypes.Scope, value)));
            var identity = new ClaimsIdentity(claims, ApiAuthenticationSchemeNames.ApiKey);
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
            http.HttpContext.Features.Set(new PersistedApiKeyAuthenticationProof(identity, Key));
            return new MachineMutationCommitAuthority(db, new MachinePrincipalAccessor(http), http, clock);
        }

        public async ValueTask DisposeAsync()
        {
            if (!ownsPurposeMembership) return;
            await using var admin = fixture.CreateDbContext();
            string login = $"\"{rows.RuntimeOptions.Username}\"";
            await admin.Database.ExecuteSqlRawAsync($"REVOKE {PostgresMachineRoomMutationAuthorityContract.RuntimeRole} FROM {login}");
        }

        private sealed record TenantScope(Guid TenantId) : ITenantContext;
    }
}
