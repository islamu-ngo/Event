using System.Data;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

public sealed partial class OrdinarySettingsNativeContractTests
{
    private const string DisclosureKey = "deployment.mode";
    private const string TenantDisclosureKey = "custom_properties.projection_discovery_enabled";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GlobalDisclosureSettlesOnceWithoutAdvancingIdentity(bool immediate)
    {
        await fixture.ResetAsync();
        await InstallAsync();
        Guid first = Guid.CreateVersion7(), second = Guid.CreateVersion7();
        await using var migration = fixture.CreateDbContext();
        migration.Tenants.AddRange(Tenant(first), Tenant(second));
        await migration.SaveChangesAsync();
        migration.Set<EventDiscoveryRevision>().Add(new EventDiscoveryRevision
        {
            Id = Guid.CreateVersion7(),
            TenantId = first,
            DisclosureEpoch = 4,
            IdentityEpoch = 31
        });
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
            var row = revisions.Single(value => value.TenantId == tenant);
            await Assert.That(row.DisclosureEpoch).IsEqualTo(epochs.GetValueOrDefault(tenant).DisclosureEpoch + 1);
            await Assert.That(row.IdentityEpoch).IsEqualTo(epochs.GetValueOrDefault(tenant).IdentityEpoch);
        }
        await Assert.That(JsonNode.DeepEquals(expected, (await ReadAsync(caller, keys: request["Keys"]))["Image"])).IsTrue();
    }

    [Test]
    [Arguments(SettingScope.Tenant, TenantDisclosureKey, "true", true)]
    [Arguments(SettingScope.User, "federation.atproto_publish_my_events", "true", false)]
    [Arguments(SettingScope.Instance, Key, "\"native-contract\"", false)]
    public async Task ClassificationInvalidatesOnlyCanonicalNonUserDisclosureEffects(
        SettingScope scope, string key, string value, bool invalidates)
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
            await Assert.That(current.DisclosureEpoch).IsEqualTo(previous.DisclosureEpoch + (invalidates && id == own ? 1 : 0));
            await Assert.That(current.IdentityEpoch).IsEqualTo(previous.IdentityEpoch);
            if (!invalidates || id != own)
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
    public async Task NativePendingMarkerSealsAndCannotMixMutationOwners(bool nativeFirst, string genericOwner)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var pending = caller.DisclosureMutations;
            if (nativeFirst)
            {
                pending.EnlistNativeOrdinarySettings();
                Assert.Throws<InvalidOperationException>(() => pending.Enlist([Guid.CreateVersion7()]));
                Assert.Throws<InvalidOperationException>(() => pending.EnlistIdentityRevision(Guid.CreateVersion7()));
                Assert.Throws<InvalidOperationException>(() => pending.RecordTenantDeletion(Guid.CreateVersion7()));
                await pending.FlushAsync(CancellationToken.None);
                await pending.FlushAsync(CancellationToken.None);
                Assert.Throws<InvalidOperationException>(pending.EnlistNativeOrdinarySettings);
            }
            else
            {
                switch (genericOwner)
                {
                    case "disclosure": pending.Enlist([Guid.CreateVersion7()]); break;
                    case "identity": pending.EnlistIdentityRevision(Guid.CreateVersion7()); break;
                    case "deletion": pending.RecordTenantDeletion(Guid.CreateVersion7()); break;
                    default: throw new ArgumentOutOfRangeException(nameof(genericOwner));
                }
                Assert.Throws<InvalidOperationException>(pending.EnlistNativeOrdinarySettings);
            }
            await transaction.RollbackAsync();
        }
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            caller.DisclosureMutations.EnlistNativeOrdinarySettings();
            await caller.DisclosureMutations.FlushAsync(CancellationToken.None);
            await transaction.RollbackAsync();
        }
    }

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
        await Assert.That(await migration.Set<EventDiscoveryRevision>().AnyAsync(row => row.TenantId == tenant)).IsFalse();
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
