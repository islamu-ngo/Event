using System.Collections.Immutable;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class CoordinatedSettingsNativeValidationTests(PostgreSqlContainerFixture postgres)
{
    private readonly List<string> revokeOwnedGrants = [];
    private static readonly DateTime OccurredAt = new(2026, 8, 25, 12, 30, 0, DateTimeKind.Utc);
    private static readonly Guid Earlier = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a001");
    private static readonly Guid Requested = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a002");
    private static readonly Guid Later = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a003");
    private const string ProviderValue = "provider-row-must-not-change";
    private static string[] Keys => PublicationPolicySettingKeys.All.ToArray();

    [Test]
    public async Task ReadTenantSnapshotReturnsOnlyGuardedRowsForRequestedTenantAndPreservesJsonAndLocks()
    {
        await SeedAsync();
        await using var native = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, Requested);
        await EnrollTenantSettingsAsync();
        await using var ambient = postgres.CreateEnrolledTenantDbContext(
            new NativeTenant(Later), PostgresTenantSessionInterceptor.RuntimeInstance);
        _ = ambient.BindTenantLease();
        await native.InOriginalRequestAsync(async () =>
        {
            var preparation = native.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var proof = await preparation.PrepareAsync(new UpdateSettingCommand
            { Key = Keys[1], Value = "true", Scope = SettingScope.Tenant }, "true");
            var snapshot = await preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                var observed = await native.Services.GetRequiredService<ICoordinatedSettingMutationStore>()
                    .ReadTenantSnapshotAsync(Requested, token);
                // Finish the real original action after observing its pre-write snapshot.
                var written = await native.Services.GetRequiredService<IPublicationPolicyMutationBoundary>()
                    .ApplyTenantAsync(new(Requested, native.Subject.PrincipalId, OccurredAt,
                        [SetTenant(Requested, Keys[1], "true")], PublicationPolicyLockedSystemBehavior.Reject), token);
                await Assert.That(written.Success).IsTrue();
                return observed;
            });
            await Assert.That(snapshot.SystemValues.SequenceEqual(new PublicationPolicySystemValueSnapshot[]
            {
                new(Keys[0], " true \n", true), new(Keys[1], "false", false),
                new(Keys[2], "true", false), new(Keys[3], "false", true), new(Keys[4], "true", false)
            })).IsTrue();
            await Assert.That(snapshot.TenantValues.SequenceEqual(new PublicationPolicyTenantValueSnapshot[]
            {
                new(Requested, Keys[0], " false \n"), new(Requested, Keys[2], "true")
            })).IsTrue();
            await Assert.That(snapshot.TenantValues.All(row => row.TenantId == Requested)).IsTrue();
            await Assert.That(snapshot.TenantValues.Any(row => row.Key ==
                GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider)).IsFalse();
            return true;
        });
        await Assert.That(ambient.TenantFilterTenantId).IsEqualTo((Guid?)Later);
        await Assert.That(await ambient.TenantSettingOverrides.Where(row => row.SettingKey == Keys[0])
            .Select(row => row.Value).SingleAsync()).IsEqualTo("true");
        await AssertProvidersAsync();
    }

    [Test]
    public async Task SnapshotReadsUseDatabaseValuesInsteadOfTrackedOrResolverState()
    {
        await SeedAsync();
        await using var native = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, Requested);
        await EnrollTenantSettingsAsync();
        await native.InOriginalRequestAsync(async () =>
        {
            var owner = native.Owner;
            var trackedSystem = await owner.SystemSettings.SingleAsync(row => row.SettingKey == Keys[0]);
            var trackedTenant = await owner.TenantSettingOverrides.SingleAsync(row =>
                row.TenantId == Requested && row.SettingKey == Keys[0]);
            await using (var writer = postgres.CreateDbContext())
            {
                await writer.SystemSettings.Where(row => row.SettingKey == Keys[0])
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Value, "false"));
                await writer.TenantSettingOverrides.Where(row => row.TenantId == Requested && row.SettingKey == Keys[0])
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Value, "true"));
            }
            await Assert.That(trackedSystem.Value).IsEqualTo(" true \n");
            await Assert.That(trackedTenant.Value).IsEqualTo(" false \n");
            var preparation = native.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var proof = await preparation.PrepareAsync(new UpdateSettingCommand
            { Key = Keys[1], Value = "true", Scope = SettingScope.Tenant }, "true");
            var snapshot = await preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                var observed = await native.Services.GetRequiredService<ICoordinatedSettingMutationStore>()
                    .ReadTenantSnapshotAsync(Requested, token);
                var written = await native.Services.GetRequiredService<IPublicationPolicyMutationBoundary>()
                    .ApplyTenantAsync(new(Requested, native.Subject.PrincipalId, OccurredAt,
                        [SetTenant(Requested, Keys[1], "true")], PublicationPolicyLockedSystemBehavior.Reject), token);
                await Assert.That(written.Success).IsTrue();
                return observed;
            });
            await Assert.That(snapshot.SystemValues.Single(row => row.Key == Keys[0]).JsonValue).IsEqualTo("false");
            await Assert.That(snapshot.TenantValues.Single(row => row.Key == Keys[0]).JsonValue).IsEqualTo("true");
            return true;
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WriteOutsideTransactionFailsBeforeMutation(bool instanceWrite)
    {
        await SeedAsync();
        await using var native = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, Requested);
        await native.InOriginalRequestAsync(async () =>
        {
            var store = native.Services.GetRequiredService<ICoordinatedSettingMutationStore>();
            var failure = instanceWrite
                ? await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteInstanceAsync(
                    [SetSystem(Keys[0], "true", true)], native.Subject.PrincipalId, OccurredAt, default))
                : await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteTenantAsync(
                    Requested, [SetTenant(Requested, Keys[0], "true")], native.Subject.PrincipalId, OccurredAt, default));
            await Assert.That(failure!.Message).IsEqualTo("Coordinated setting writes require an active transaction.");
            await Assert.That(native.Owner.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
            return true;
        });
        await AssertUnchangedAsync();
    }

    [Test]
    public async Task RemoveAbsentTenantRowReturnsNoChangesAndDoesNotSave()
    {
        await SeedAsync();
        var observation = new NativeObservation();
        await using var native = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, Requested, observation);
        await EnrollTenantSettingsAsync();
        await native.InOriginalRequestAsync(async () =>
        {
            var preparation = native.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var proof = await preparation.PrepareAsync(new ResetSettingCommand { Key = Keys[4], Scope = SettingScope.Tenant });
            var result = await preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                observation.Armed = true;
                try
                {
                    return await native.Services.GetRequiredService<IPublicationPolicyMutationBoundary>().ApplyTenantAsync(
                        new(Requested, native.Subject.PrincipalId, OccurredAt,
                            [new(Keys[4], PublicationPolicyMutationKind.Remove, null, Requested, null)],
                            PublicationPolicyLockedSystemBehavior.Reject), token);
                }
                finally { observation.Armed = false; }
            });
            await Assert.That(result.Success).IsTrue();
            await Assert.That(result.DeferredNotifications).IsEmpty();
            await Assert.That(observation.Saves).IsEqualTo(0);
            return true;
        });
        await using var verification = postgres.CreateDbContext();
        await Assert.That(await verification.TenantSettingOverrides
            .AnyAsync(row => row.TenantId == Requested && row.SettingKey == Keys[4])).IsFalse();
        await AssertUnchangedAsync();
    }

    [Test]
    public async Task TenantBatchValidFirstAndWrongTenantSecondRejectsWholeBatchBeforeEfMutation()
    {
        await AssertInvalidAsync(async (store, actor, token) =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteTenantAsync(Requested,
                [SetTenant(Requested, Keys[0], "true"), SetTenant(Later, Keys[1], "false")], actor, OccurredAt, token));
        });
    }

    [Test]
    public async Task InstanceBatchValidFirstAndInvalidDistinctSecondRejectsWholeBatchBeforeEfMutation()
    {
        await AssertInvalidAsync(async (store, actor, token) =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteInstanceAsync(
                [RemoveSystem(Keys[0]), new(Keys[1], (PublicationPolicyMutationKind)999, "true", null, false)],
                actor, OccurredAt, token));
        });
    }

    [Test]
    public async Task InvalidKeysShapesDuplicateKeysAndWrongTenantIdsAreRejectedBeforeEfMutation()
    {
        await AssertInvalidAsync(async (store, actor, token) =>
        {
            ImmutableArray<PublicationPolicySettingMutation>[] tenantBatches =
            [
                default, [null!],
                [new(null!, PublicationPolicyMutationKind.Set, "true", Requested, null)],
                [new("", PublicationPolicyMutationKind.Set, "true", Requested, null)],
                [new("   ", PublicationPolicyMutationKind.Set, "true", Requested, null)],
                [new(Keys[0], (PublicationPolicyMutationKind)999, "true", Requested, null)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, null, Requested, null)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "{", Requested, null)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "1", Requested, null)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "\"true\"", Requested, null)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "true", Requested, false)],
                [new(Keys[0], PublicationPolicyMutationKind.Remove, "true", Requested, null)],
                [new(Keys[0], PublicationPolicyMutationKind.Remove, null, Requested, true)],
                [SetTenant(Later, Keys[0], "true")],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "true", null, null)],
                [SetTenant(Guid.Empty, Keys[0], "true")],
                [SetTenant(Requested, Keys[0], "true"), new(Keys[0], PublicationPolicyMutationKind.Remove, null, Requested, null)],
                [SetTenant(Requested, GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, "false")]
            ];
            foreach (var batch in tenantBatches)
                await Assert.ThrowsAsync<ArgumentException>(() => store.WriteTenantAsync(Requested, batch, actor, OccurredAt, token));
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteTenantAsync(Guid.Empty,
                [SetTenant(Guid.Empty, Keys[0], "true")], actor, OccurredAt, token));
            ImmutableArray<PublicationPolicySettingMutation>[] instanceBatches =
            [
                default, [null!],
                [new(null!, PublicationPolicyMutationKind.Set, "true", null, false)],
                [new("", PublicationPolicyMutationKind.Set, "true", null, false)],
                [new("   ", PublicationPolicyMutationKind.Set, "true", null, false)],
                [new(Keys[0], (PublicationPolicyMutationKind)999, "true", null, false)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, null, null, false)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "{", null, false)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "1", null, false)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "\"false\"", null, false)],
                [new(Keys[0], PublicationPolicyMutationKind.Set, "true", null, null)],
                [SetTenant(Requested, Keys[0], "true")], [SetTenant(Guid.Empty, Keys[0], "true")],
                [new(Keys[0], PublicationPolicyMutationKind.Remove, "true", null, null)],
                [new(Keys[0], PublicationPolicyMutationKind.Remove, null, null, false)],
                [SetSystem(Keys[0], "true", false), RemoveSystem(Keys[0])],
                [SetSystem(GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, "false", false)]
            ];
            foreach (var batch in instanceBatches)
                await Assert.ThrowsAsync<ArgumentException>(() => store.WriteInstanceAsync(batch, actor, OccurredAt, token));
        });
    }

    private async Task AssertInvalidAsync(Func<ICoordinatedSettingMutationStore, Guid, CancellationToken, Task> attack)
    {
        await SeedAsync();
        SystemState[] before;
        await using (var baseline = postgres.CreateDbContext())
            before = await baseline.SystemSettings.Where(row => Keys.Contains(row.SettingKey))
                .Select(row => new SystemState(row.Id, row.SettingKey, row.Value, row.IsLocked)).ToArrayAsync();
        var observer = new NativeObservation();
        await using var native = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, Requested, observer);
        await native.InOriginalRequestAsync(async () =>
        {
            var store = native.Services.GetRequiredService<ICoordinatedSettingMutationStore>();
            await native.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                observer.Armed = true;
                try
                {
                    await attack(store, native.Subject.PrincipalId, token);
                    await Assert.That(observer.Commands).IsEqualTo(0);
                    await Assert.That(observer.Saves).IsEqualTo(0);
                    await Assert.That(native.Owner.ChangeTracker.Entries().Any(entry =>
                        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
                }
                finally { observer.Armed = false; }
                return true;
            });
            return true;
        });
        await using (var verification = postgres.CreateDbContext())
            await Assert.That(await verification.SystemSettings.Where(row => Keys.Contains(row.SettingKey))
                .Select(row => new SystemState(row.Id, row.SettingKey, row.Value, row.IsLocked)).ToArrayAsync())
                .IsEquivalentTo(before);
        await AssertUnchangedAsync();
    }

    private async Task SeedAsync()
    {
        await postgres.ResetAsync();
        await using var seed = postgres.CreateDbContext();
        string[] values = [" true \n", "false", "true", "false", "true"];
        for (int index = 0; index < Keys.Length; index++)
        {
            string key = Keys[index];
            var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
            if (row is null)
                seed.SystemSettings.Add(row = new SystemSetting { SettingKey = key, Value = values[index] });
            row.Value = values[index];
            row.ValueType = SettingValueType.Boolean;
            row.IsLocked = index is 0 or 3;
        }
        var provider = await seed.SystemSettings.SingleOrDefaultAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider);
        if (provider is null)
            seed.SystemSettings.Add(provider = new SystemSetting
            { SettingKey = GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, Value = ProviderValue });
        provider.Value = ProviderValue;
        provider.IsLocked = true;
        foreach (var (id, slug) in new[] { (Earlier, "coordinated-earlier"), (Requested, "coordinated-requested"), (Later, "coordinated-later") })
            seed.Tenants.Add(new Tenant
            {
                Id = id,
                Slug = slug,
                FullName = slug,
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
        await seed.SaveChangesAsync();
        Add(Earlier, Keys[1], "false");
        Add(Requested, Keys[0], " false \n");
        Add(Requested, Keys[2], "true");
        Add(Later, Keys[0], "true");
        Add(Requested, GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, ProviderValue);
        Add(Later, GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, ProviderValue);
        await seed.SaveChangesAsync();
        void Add(Guid tenant, string key, string value) => seed.TenantSettingOverrides.Add(new TenantSetting
        { TenantId = tenant, Tenant = null!, SettingKey = key, Value = value, CreatedAt = OccurredAt });
    }

    private async Task EnrollTenantSettingsAsync()
    {
        await using var setup = postgres.CreateDbContext();
        var sql = setup.GetService<ISqlGenerationHelper>();
        var entity = setup.Model.FindEntityType(typeof(TenantSetting))!;
        // Ordinary table privileges remain bounded by the enrolled Runtime tenant's FORCE RLS.
        string table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string login = postgres.NativeQueueEnrollment.RuntimeLogin;
        foreach (string privilege in new[] { "SELECT", "INSERT", "UPDATE" })
        {
            if (await setup.Database.SqlQuery<bool>(
                    $"SELECT pg_catalog.has_table_privilege({login}, {table}, {privilege}) AS \"Value\"").SingleAsync())
                continue;
            await setup.Database.ExecuteSqlRawAsync($"GRANT {privilege} ON {table} TO {sql.DelimitIdentifier(login)}");
            revokeOwnedGrants.Add($"REVOKE {privilege} ON {table} FROM {sql.DelimitIdentifier(login)}");
        }
    }

    [After(Test)]
    public async Task RestoreNativePrivileges()
    {
        await using var setup = postgres.CreateDbContext();
        for (int index = revokeOwnedGrants.Count - 1; index >= 0; index--)
            await setup.Database.ExecuteSqlRawAsync(revokeOwnedGrants[index]);
        revokeOwnedGrants.Clear();
    }

    private async Task AssertUnchangedAsync()
    {
        await using var observer = postgres.CreateDbContext();
        var systems = await observer.SystemSettings.Where(row => Keys.Contains(row.SettingKey))
            .Select(row => new { row.SettingKey, row.Value, row.IsLocked }).ToArrayAsync();
        string[] expected = [" true \n", "false", "true", "false", "true"];
        for (int index = 0; index < Keys.Length; index++)
        {
            var row = systems.Single(item => item.SettingKey == Keys[index]);
            await Assert.That(row.Value).IsEqualTo(expected[index]);
            await Assert.That(row.IsLocked).IsEqualTo(index is 0 or 3);
        }
        var tenants = await observer.TenantSettingOverrides.Where(row => Keys.Contains(row.SettingKey))
            .Select(row => new { row.TenantId, row.SettingKey, row.Value }).ToArrayAsync();
        await Assert.That(tenants.Length).IsEqualTo(4);
        await Assert.That(tenants.Single(row => row.TenantId == Requested && row.SettingKey == Keys[0]).Value).IsEqualTo(" false \n");
        await Assert.That(tenants.Single(row => row.TenantId == Requested && row.SettingKey == Keys[2]).Value).IsEqualTo("true");
        await Assert.That(tenants.Single(row => row.TenantId == Earlier).Value).IsEqualTo("false");
        await Assert.That(tenants.Single(row => row.TenantId == Later).Value).IsEqualTo("true");
        await AssertProvidersAsync();
    }

    private async Task AssertProvidersAsync()
    {
        await using var observer = postgres.CreateDbContext();
        string key = GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider;
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(ProviderValue);
        await Assert.That(await observer.TenantSettingOverrides.Where(row => row.SettingKey == key)
            .Select(row => row.Value).ToArrayAsync()).IsEquivalentTo([ProviderValue, ProviderValue]);
    }

    private static PublicationPolicySettingMutation SetTenant(Guid tenant, string key, string value) =>
        new(key, PublicationPolicyMutationKind.Set, value, tenant, null);
    private static PublicationPolicySettingMutation SetSystem(string key, string value, bool locked) =>
        new(key, PublicationPolicyMutationKind.Set, value, null, locked);
    private static PublicationPolicySettingMutation RemoveSystem(string key) =>
        new(key, PublicationPolicyMutationKind.Remove, null, null, null);

    private sealed record NativeTenant(Guid TenantId) : ITenantContext;
    private sealed record SystemState(Guid Id, string Key, string Value, bool IsLocked);

    private sealed class NativeObservation : DbCommandInterceptor, ISaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal int Commands { get; private set; }
        internal int Saves { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (Armed) Commands++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (Armed) Commands++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { if (Armed) Commands++; return ValueTask.FromResult(result); }
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (Armed) Saves++; return ValueTask.FromResult(result); }
    }
}
