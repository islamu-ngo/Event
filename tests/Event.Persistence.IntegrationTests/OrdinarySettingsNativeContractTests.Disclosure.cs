using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class OrdinarySettingsNativeContractTests
{
    private const string DisclosureKey = "deployment.mode";
    private const string TenantDisclosureKey = "custom_properties.projection_discovery_enabled";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeCompletionDoesNotAdvanceOrCreateManagedDisclosureEpochs(bool immediate)
    {
        await fixture.ResetAsync();
        await InstallAsync();
        Guid first = Guid.CreateVersion7(), second = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.AddRange(Tenant(first), Tenant(second));
        await migration.SaveChangesAsync();
        var seeded = await migration.Set<EventDiscoveryRevision>().SingleOrDefaultAsync(row => row.TenantId == first);
        if (seeded is null)
        {
            seeded = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = first };
            migration.Add(seeded);
        }
        seeded.DisclosureEpoch = 4;
        seeded.IdentityEpoch = 31;
        await migration.SaveChangesAsync();
        var epochs = await migration.Set<EventDiscoveryRevision>().AsNoTracking()
            .ToDictionaryAsync(row => row.TenantId, row => (row.DisclosureEpoch, row.IdentityEpoch));
        Guid[] allTenants = await migration.Tenants.Select(row => row.Id).ToArrayAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance, key: DisclosureKey, value: "\"MultiTenant\"");
        JsonObject additional = Request(SettingScope.Instance, key: TenantDisclosureKey, value: "true");
        request["Keys"]!.AsArray().Add(TenantDisclosureKey);
        request["Effects"]!.AsArray().Add(additional["Effects"]![0]!.DeepClone());
        request["NewIds"]![TenantDisclosureKey] = additional["NewIds"]![TenantDisclosureKey]!.DeepClone();
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        JsonObject after = SystemAfter(before, request, DisclosureKey);
        JsonObject additionalAfter = SystemAfter(before, request, TenantDisclosureKey);
        JsonNode expected = before["Image"]!.DeepClone();
        expected["System"]![DisclosureKey] = after.DeepClone();
        expected["System"]![TenantDisclosureKey] = additionalAfter.DeepClone();
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await AcquireAsync(caller, before, request);
            await WriteAsync(caller, "System", 0, before["Image"]!["System"]![DisclosureKey], after, DisclosureKey);
            await WriteAsync(caller, "System", 0, before["Image"]!["System"]![TenantDisclosureKey], additionalAfter, TenantDisclosureKey);
            await CheckAsync(caller, "complete", expected);
            await CheckAsync(caller, "complete", expected);
            if (immediate)
            {
                await caller.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
                await caller.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL DEFERRED");
                await caller.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            }
            await transaction.CommitAsync();
        }
        var revisions = await migration.Set<EventDiscoveryRevision>().AsNoTracking().ToListAsync();
        foreach (Guid tenant in allTenants)
        {
            var row = revisions.SingleOrDefault(value => value.TenantId == tenant);
            await Assert.That(row is not null).IsEqualTo(epochs.ContainsKey(tenant));
            if (row is not null)
            {
                await Assert.That(row.DisclosureEpoch).IsEqualTo(epochs[tenant].DisclosureEpoch);
                await Assert.That(row.IdentityEpoch).IsEqualTo(epochs[tenant].IdentityEpoch);
            }
        }
        await Assert.That(JsonNode.DeepEquals(expected, (await ReadAsync(caller, keys: request["Keys"]))["Image"])).IsTrue();
    }

    [Test]
    [Arguments(SettingScope.Tenant, TenantDisclosureKey, "true")]
    [Arguments(SettingScope.User, "federation.atproto_publish_my_events", "true")]
    [Arguments(SettingScope.Instance, Key, "\"native-contract\"")]
    public async Task NativeEffectsCannotIndependentlyFinalizeAnyDisclosureClassification(
        SettingScope scope, string key, string value)
    {
        await InstallAsync();
        Guid own = Guid.CreateVersion7(), foreign = Guid.CreateVersion7(), user = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.AddRange(Tenant(own), Tenant(foreign));
        await migration.SaveChangesAsync();
        await migration.SystemSettings.Where(row => row.SettingKey == key)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.IsLocked, false));
        var epochs = await migration.Set<EventDiscoveryRevision>().AsNoTracking()
            .ToDictionaryAsync(row => row.TenantId, row => (row.DisclosureEpoch, row.IdentityEpoch));
        Guid? tenant = scope == SettingScope.Instance ? null : own;
        Guid? subject = scope == SettingScope.User ? user : null;
        await using var caller = Open(tenant.HasValue ? PrimaryDatabaseRole.Runtime : PrimaryDatabaseRole.Global, tenant);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(scope, tenant, subject, key, value);
        JsonObject before = await ReadAsync(caller, tenant, subject, request["Keys"]);
        string group = scope == SettingScope.Instance ? "System" : scope == SettingScope.Tenant ? "Tenant" : "User";
        JsonObject after = scope == SettingScope.Instance ? SystemAfter(before, request, key) : new JsonObject
        {
            ["Id"] = request["NewIds"]![key]!.DeepClone(),
            ["TenantId"] = own,
            ["Key"] = key,
            ["Value"] = value,
            ["CreatedAt"] = NativeInstant,
            ["CreatedBy"] = request["Actor"]!.DeepClone(),
            ["UpdatedAt"] = null,
            ["UpdatedBy"] = null
        };
        if (scope == SettingScope.Tenant) after["Locked"] = false;
        if (scope == SettingScope.User) after["UserId"] = user;
        JsonNode expected = before["Image"]!.DeepClone();
        expected[group]![key] = after.DeepClone();
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await AcquireAsync(caller, before, request, tenant, subject);
            await WriteAsync(caller, group, 0, before["Image"]![group]![key], after, key);
            await CheckAsync(caller, "complete", expected);
            await transaction.CommitAsync();
        }
        await Assert.That(JsonNode.DeepEquals(expected, (await ReadAsync(caller, tenant, subject, request["Keys"]))["Image"])).IsTrue();
        var persisted = await migration.Set<EventDiscoveryRevision>().AsNoTracking()
            .ToDictionaryAsync(row => row.TenantId, row => (row.DisclosureEpoch, row.IdentityEpoch));
        foreach (Guid id in await migration.Tenants.Select(row => row.Id).ToArrayAsync())
        {
            var previous = epochs.GetValueOrDefault(id);
            var current = persisted.GetValueOrDefault(id);
            await Assert.That(current.DisclosureEpoch).IsEqualTo(previous.DisclosureEpoch);
            await Assert.That(current.IdentityEpoch).IsEqualTo(previous.IdentityEpoch);
            await Assert.That(persisted.ContainsKey(id)).IsEqualTo(epochs.ContainsKey(id));
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    public async Task OrdinaryServiceCannotExecuteTheNestedDisclosureCapabilities(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.Add(Tenant(tenant));
        await migration.SaveChangesAsync();
        await using var caller = Open(role, role == PrimaryDatabaseRole.Runtime ? tenant : null);
        await caller.Database.OpenConnectionAsync();
        foreach (string expression in new[]
        {
            $"{PostgresDiscoveryDisclosureContract.FunctionSql(caller, "dependencies")}('all',ARRAY[]::uuid[])",
            $"{PostgresDiscoveryDisclosureContract.FunctionSql(caller, "fence_tenants")}(ARRAY[]::uuid[])",
            $"{PostgresDiscoveryDisclosureContract.FunctionSql(caller, "finalize")}(ARRAY[]::uuid[],ARRAY[]::uuid[],ARRAY[]::boolean[],ARRAY[]::bigint[],ARRAY[]::bigint[])"
        })
        {
            await using var command = caller.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT {expression}";
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Test]
    [Arguments(false, "disclosure")]
    [Arguments(false, "identity")]
    [Arguments(false, "deletion")]
    [Arguments(true, "disclosure")]
    public async Task OriginalPreparedOrdinarySourceCannotMixGenericMutationOwners(bool nativeFirst, string genericOwner)
    {
        var injection = new GenericDisclosureInjection(genericOwner);
        await using var authentication = await CreateManagedDisclosureOwnerAsync(injection);
        bool entered = false;
        async Task ExecuteAsync() => await authentication.RunAsync(null, async session =>
        {
            var prepared = await session.Preparation.PrepareAsync(
                new UpdateSettingCommand { Key = TenantDisclosureKey, Scope = SettingScope.Instance, Value = "true" }, "true");
            if (!nativeFirst) injection.Arm(session.Context);
            return await session.MutationLock.ExecuteOrderedGroupsAsync([[TenantDisclosureKey]],
                token => session.Preparation.ExecutePreparedAsync(prepared, async (_, ownerToken) =>
                {
                    entered = true;
                    await Assert.That(session.Preparation.CurrentOrdinarySource).IsNotNull();
                    var pending = session.Context.DisclosureMutations;
                    Assert.Throws<InvalidOperationException>(() => pending.Enlist([Guid.CreateVersion7()]));
                    Assert.Throws<InvalidOperationException>(() => pending.EnlistIdentityRevision(Guid.CreateVersion7()));
                    Assert.Throws<InvalidOperationException>(() => pending.RecordTenantDeletion(Guid.CreateVersion7()));
                    await WriteManagedDisclosureAsync(session, ownerToken);
                    return true;
                }, token));
        });
        if (nativeFirst)
        {
            await ExecuteAsync();
            await Assert.That(entered).IsTrue();
        }
        else
        {
            var rejected = await Assert.ThrowsAsync<InvalidOperationException>(ExecuteAsync);
            await Assert.That(rejected!.Message).Contains("cannot mix discovery mutation owners");
            await Assert.That(injection.Injected).IsTrue();
            await Assert.That(entered).IsFalse();
        }
        await using var observer = Open(PrimaryDatabaseRole.Migrator);
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == TenantDisclosureKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(nativeFirst ? "true" : "false");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ManagedOrdinaryRootFinalizesEveryStatusOnceAndRollsBackAtomically(bool rollback)
    {
        var commit = new ManagedDisclosureCommitProbe(rollback);
        await using var authentication = await CreateManagedDisclosureOwnerAsync(commit);
        await using var observer = Open(PrimaryDatabaseRole.Migrator);
        var before = await observer.Set<EventDiscoveryRevision>().AsNoTracking()
            .ToDictionaryAsync(row => row.TenantId, row => (row.Id, row.DisclosureEpoch, row.IdentityEpoch));
        var tenants = await observer.Tenants.AsNoTracking().ToListAsync();
        await Assert.That(tenants.Any(row => row.TenantStatusId == (int)TenantStatusEnum.Suspended)).IsTrue();
        await Assert.That(before.Keys.Order().ToArray()).IsEquivalentTo(tenants.Select(row => row.Id).Order().ToArray());
        async Task ExecuteAsync() => await authentication.RunAsync(null, async session =>
        {
            var prepared = await session.Preparation.PrepareAsync(
                new UpdateSettingCommand { Key = TenantDisclosureKey, Scope = SettingScope.Instance, Value = "true" }, "true");
            commit.Arm(session.Context);
            return await session.MutationLock.ExecuteOrderedGroupsAsync([[TenantDisclosureKey]],
                token => session.Preparation.ExecutePreparedAsync(prepared, async (_, ownerToken) =>
                {
                    await Assert.That(session.Preparation.CurrentOrdinarySource).IsNotNull();
                    await WriteManagedDisclosureAsync(session, ownerToken);
                    return true;
                }, token));
        });
        if (rollback) await Assert.ThrowsAsync<ManagedDisclosureRollbackException>(ExecuteAsync);
        else await ExecuteAsync();
        await Assert.That(commit.Reached).IsTrue();
        var after = await observer.Set<EventDiscoveryRevision>().AsNoTracking()
            .ToDictionaryAsync(row => row.TenantId, row => (row.Id, row.DisclosureEpoch, row.IdentityEpoch));
        await Assert.That(after.Count).IsEqualTo(before.Count);
        foreach (var (id, previous) in before)
        {
            await Assert.That(after[id].Id).IsEqualTo(previous.Id);
            await Assert.That(after[id].IdentityEpoch).IsEqualTo(previous.IdentityEpoch);
            await Assert.That(after[id].DisclosureEpoch).IsEqualTo(previous.DisclosureEpoch + (rollback ? 0 : 1));
        }
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == TenantDisclosureKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(rollback ? "false" : "true");
    }

    private async Task<AuthenticatedSettingsFixture> CreateManagedDisclosureOwnerAsync(IInterceptor interceptor)
    {
        await fixture.ResetAsync();
        var authentication = await AuthenticatedSettingsFixture.CreateAsync(
            () => Open(PrimaryDatabaseRole.Migrator), fixture, interceptor);
        try
        {
            await using var seed = Open(PrimaryDatabaseRole.Migrator);
            var suspended = Tenant(Guid.CreateVersion7());
            suspended.TenantStatusId = (int)TenantStatusEnum.Suspended;
            seed.Tenants.AddRange(Tenant(Guid.CreateVersion7()), suspended);
            await seed.SaveChangesAsync();
            var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == TenantDisclosureKey);
            if (setting is null)
            {
                var definition = SettingRegistry.Get(TenantDisclosureKey)
                    ?? throw new InvalidOperationException("The ordinary disclosure fixture requires its registered setting.");
                setting = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = TenantDisclosureKey,
                    Value = "false",
                    ValueType = definition.ValueType,
                    Description = definition.Description,
                    Category = definition.Category,
                    AllowedValues = definition.AllowedValues is null ? null : JsonSerializer.Serialize(definition.AllowedValues),
                    CreatedAt = Instant
                };
                seed.SystemSettings.Add(setting);
            }
            setting.Value = "false";
            setting.IsLocked = false;
            var revisions = await seed.Set<EventDiscoveryRevision>().ToListAsync();
            foreach (Guid tenant in await seed.Tenants.Select(row => row.Id).ToArrayAsync())
            {
                var revision = revisions.SingleOrDefault(row => row.TenantId == tenant);
                if (revision is null)
                {
                    revision = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = tenant };
                    seed.Add(revision);
                }
                revision.DisclosureEpoch = 4;
                revision.IdentityEpoch = 31;
            }
            await seed.SaveChangesAsync();
            return authentication;
        }
        catch
        {
            await authentication.DisposeAsync();
            throw;
        }
    }

    private static async Task WriteManagedDisclosureAsync(AuthenticatedSettingsFixture.Session session, CancellationToken token)
    {
        var repository = session.Services.GetRequiredService<ISystemSettingRepository>();
        var setting = await repository.GetByKey(TenantDisclosureKey, token)
            ?? throw new InvalidOperationException("The seeded ordinary disclosure setting is missing.");
        setting.Value = "true";
        setting.UpdatedBy = session.ActorId;
        setting.UpdatedAt = DateTime.UtcNow;
        await repository.UpsertInCurrentTransactionAsync(setting, token);
    }

    // Inject a competing real accumulator only after the real owner's transaction exists;
    // no SQL matching or fabricated Settings source is used to schedule the conflict.
    private sealed class GenericDisclosureInjection(string owner) : DbCommandInterceptor
    {
        private ExploreDbContext? selected;
        internal bool Injected { get; private set; }
        internal void Arm(ExploreDbContext context) => selected = context;
        private void Inject(CommandEventData data)
        {
            if (Injected || selected is null || !ReferenceEquals(data.Context, selected)
                || selected.Database.CurrentTransaction is null || !selected.HumanCommitAttemptOwnsTransaction) return;
            Injected = true;
            var pending = selected.DisclosureMutations;
            switch (owner)
            {
                case "disclosure": pending.Enlist([Guid.CreateVersion7()]); break;
                case "identity": pending.EnlistIdentityRevision(Guid.CreateVersion7()); break;
                case "deletion": pending.RecordTenantDeletion(Guid.CreateVersion7()); break;
                default: throw new ArgumentOutOfRangeException(nameof(owner));
            }
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inject(eventData); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inject(eventData); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { Inject(eventData); return ValueTask.FromResult(result); }
    }

    private sealed class ManagedDisclosureCommitProbe(bool rollback) : DbTransactionInterceptor
    {
        private ExploreDbContext? selected;
        internal bool Reached { get; private set; }
        internal void Arm(ExploreDbContext context) => selected = context;
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (selected is null || !ReferenceEquals(eventData.Context, selected)) return result;
            // Production completion and the first UoW flush have already run at this point.
            await selected.DisclosureMutations.FlushAsync(cancellationToken);
            await selected.DisclosureMutations.FlushAsync(cancellationToken);
            Assert.Throws<InvalidOperationException>(() => selected.DisclosureMutations.Enlist([Guid.CreateVersion7()]));
            Reached = true;
            if (rollback) throw new ManagedDisclosureRollbackException();
            return result;
        }
    }

    private sealed class ManagedDisclosureRollbackException : Exception;

    [Test]
    [Arguments(HumanSettingsMutationKind.SetLock)]
    [Arguments(HumanSettingsMutationKind.Remove)]
    public async Task CanonicalAbsentTenantEffectDoesNotInvalidateDiscovery(HumanSettingsMutationKind kind)
    {
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.Add(Tenant(tenant));
        await migration.SaveChangesAsync();
        var originalRevision = await migration.Set<EventDiscoveryRevision>().AsNoTracking()
            .Where(row => row.TenantId == tenant)
            .Select(row => new { row.Id, row.DisclosureEpoch, row.IdentityEpoch }).SingleOrDefaultAsync();
        await migration.SystemSettings.Where(row => row.SettingKey == TenantDisclosureKey)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.IsLocked, false));
        await using var caller = Open(PrimaryDatabaseRole.Runtime, tenant);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Tenant, tenant, key: TenantDisclosureKey, value: "true");
        request["Effects"]![0]!["Kind"] = (int)kind;
        request["Effects"]![0]!["Value"] = null;
        request["Effects"]![0]!["IsLocked"] = kind == HumanSettingsMutationKind.SetLock ? JsonValue.Create(true) : null;
        JsonObject before = await ReadAsync(caller, tenant, keys: request["Keys"]);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await AcquireAsync(caller, before, request, tenant);
            await WriteAsync(caller, "Tenant", (int)kind, null, null, TenantDisclosureKey);
            await CheckAsync(caller, "complete", before["Image"]!);
            await transaction.CommitAsync();
        }
        await Assert.That(JsonNode.DeepEquals(before["Image"], (await ReadAsync(caller, tenant, keys: request["Keys"]))["Image"])).IsTrue();
        var persistedRevision = await migration.Set<EventDiscoveryRevision>().AsNoTracking()
            .Where(row => row.TenantId == tenant)
            .Select(row => new { row.Id, row.DisclosureEpoch, row.IdentityEpoch }).SingleOrDefaultAsync();
        await Assert.That(persistedRevision).IsEqualTo(originalRevision);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisclosureEffectRollsBackWithMissingCompletionOrExplicitRollback(bool omitCompletion)
    {
        await fixture.ResetAsync();
        await InstallAsync();
        Guid tenant = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.Add(Tenant(tenant));
        await migration.SaveChangesAsync();
        var epochs = await migration.Set<EventDiscoveryRevision>().AsNoTracking()
            .ToDictionaryAsync(row => row.TenantId, row => (row.DisclosureEpoch, row.IdentityEpoch));
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        JsonObject request = Request(SettingScope.Instance, key: DisclosureKey, value: "\"MultiTenant\"");
        JsonObject before = await ReadAsync(caller, keys: request["Keys"]);
        JsonObject after = SystemAfter(before, request, DisclosureKey);
        JsonNode expected = before["Image"]!.DeepClone();
        expected["System"]![DisclosureKey] = after.DeepClone();
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await AcquireAsync(caller, before, request);
            await WriteAsync(caller, "System", 0, before["Image"]!["System"]![DisclosureKey], after, DisclosureKey);
            if (omitCompletion)
                await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            else
            {
                await CheckAsync(caller, "complete", expected);
                await caller.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
                await transaction.RollbackAsync();
            }
        }
        await Assert.That(JsonNode.DeepEquals(before["Image"], (await ReadAsync(caller, keys: request["Keys"]))["Image"])).IsTrue();
        var persisted = await migration.Set<EventDiscoveryRevision>().AsNoTracking()
            .ToDictionaryAsync(row => row.TenantId, row => (row.DisclosureEpoch, row.IdentityEpoch));
        await Assert.That(persisted.Count).IsEqualTo(epochs.Count);
        foreach (var (id, epoch) in epochs)
            await Assert.That(persisted[id]).IsEqualTo(epoch);
    }
}
