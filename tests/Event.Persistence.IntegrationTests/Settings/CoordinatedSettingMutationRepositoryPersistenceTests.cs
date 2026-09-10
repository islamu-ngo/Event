namespace Event.Persistence.IntegrationTests.Settings;

using System.Collections.Immutable;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class CoordinatedSettingMutationRepositoryPersistenceTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime OccurredAtUtc =
        new(2026, 8, 25, 12, 30, 0, DateTimeKind.Utc);

    [Test]
    public async Task ReadTenantSnapshotReturnsOnlyGuardedRowsForRequestedTenantAndPreservesJsonAndLocks()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(
            new TestTenantContext(tenants.LaterTenantId));
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        PublicationPolicyMutationSnapshot snapshot = await ExecuteInTransactionAsync(
            context,
            cancellationToken => store.ReadTenantSnapshotAsync(tenants.RequestedTenantId, cancellationToken));

        await AssertSystemSnapshotAsync(snapshot.SystemValues);
        PublicationPolicyTenantValueSnapshot[] expectedTenantValues =
        [
            new(tenants.RequestedTenantId, GuardedKeys[0], " false \n"),
            new(tenants.RequestedTenantId, GuardedKeys[2], "true")
        ];
        await Assert.That(snapshot.TenantValues.SequenceEqual(expectedTenantValues)).IsTrue();
        await Assert.That(snapshot.TenantValues.All(row => row.TenantId == tenants.RequestedTenantId)).IsTrue();
        await Assert.That(snapshot.TenantValues.Any(row => row.TenantId == tenants.LaterTenantId)).IsFalse();
        await Assert.That(snapshot.TenantValues.Any(row => IsProviderReportingKey(row.Key))).IsFalse();
    }

    [Test]
    public async Task ReadInstanceSnapshotReturnsGuardedRowsAcrossTenantsInDeterministicOrderWithoutProviderReporting()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(
            new TestTenantContext(tenants.RequestedTenantId));
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        PublicationPolicyMutationSnapshot snapshot = await ExecuteInTransactionAsync(
            context,
            store.ReadInstanceSnapshotAsync);

        await AssertSystemSnapshotAsync(snapshot.SystemValues);
        PublicationPolicyTenantValueSnapshot[] expectedTenantValues =
        [
            new(tenants.EarlierTenantId, GuardedKeys[1], "false"),
            new(tenants.RequestedTenantId, GuardedKeys[0], " false \n"),
            new(tenants.RequestedTenantId, GuardedKeys[2], "true"),
            new(tenants.LaterTenantId, GuardedKeys[0], "true")
        ];
        await Assert.That(snapshot.TenantValues.SequenceEqual(expectedTenantValues)).IsTrue();
        await Assert.That(snapshot.SystemValues.Any(row => IsProviderReportingKey(row.Key))).IsFalse();
        await Assert.That(snapshot.TenantValues.Any(row => IsProviderReportingKey(row.Key))).IsFalse();
    }

    [Test]
    public async Task TenantWriteBypassesDifferentAmbientTenantOnlyForExactRequestedTenant()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(
            new TestTenantContext(tenants.EarlierTenantId));
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        await ExecuteInTransactionAsync(context, async cancellationToken =>
        {
            CoordinatedSettingMutationWriteResult result = await store.WriteTenantAsync(
                tenants.RequestedTenantId,
                [SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true")],
                tenants.ActorUserId,
                OccurredAtUtc,
                cancellationToken);

            await Assert.That(result.Changes.SequenceEqual(
                [new CoordinatedSettingValueChange(GuardedKeys[0], " false \n", "true")])).IsTrue();
            PublicationPolicyMutationSnapshot requested = await store.ReadTenantSnapshotAsync(
                tenants.RequestedTenantId,
                cancellationToken);
            PublicationPolicyMutationSnapshot ambient = await store.ReadTenantSnapshotAsync(
                tenants.EarlierTenantId,
                cancellationToken);
            PublicationPolicyMutationSnapshot third = await store.ReadTenantSnapshotAsync(
                tenants.LaterTenantId,
                cancellationToken);
            await Assert.That(requested.TenantValues.Single(row => row.Key == GuardedKeys[0]).JsonValue)
                .IsEqualTo("true");
            await Assert.That(ambient.TenantValues.Single(row => row.Key == GuardedKeys[1]).JsonValue)
                .IsEqualTo("false");
            await Assert.That(third.TenantValues.Single(row => row.Key == GuardedKeys[0]).JsonValue)
                .IsEqualTo("true");
        });

        await Assert.That(await ReadTenantValueAsync(tenants.RequestedTenantId, GuardedKeys[0]))
            .IsEqualTo("true");
        await Assert.That(await ReadTenantValueAsync(tenants.EarlierTenantId, GuardedKeys[1]))
            .IsEqualTo("false");
        await Assert.That(await ReadTenantValueAsync(tenants.LaterTenantId, GuardedKeys[0]))
            .IsEqualTo("true");
        await AssertProviderRowsUnchangedAsync(tenants);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WriteOutsideTransactionFailsBeforeMutation(bool instanceWrite)
    {
        SeededTenants tenants = await SeedContractStateAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);
        string originalValue = await ReadTenantValueAsync(tenants.RequestedTenantId, GuardedKeys[0]);

        InvalidOperationException? exception = instanceWrite
            ? await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteInstanceAsync(
                [SetSystem(GuardedKeys[0], "true", isLocked: true)],
                tenants.ActorUserId,
                OccurredAtUtc,
                CancellationToken.None))
            : await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteTenantAsync(
                tenants.RequestedTenantId,
                [SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true")],
                tenants.ActorUserId,
                OccurredAtUtc,
                CancellationToken.None));

        await Assert.That(exception!.Message)
            .IsEqualTo("Coordinated setting writes require an active transaction.");
        await Assert.That(context.ChangeTracker.Entries().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
        await Assert.That(await ReadTenantValueAsync(tenants.RequestedTenantId, GuardedKeys[0]))
            .IsEqualTo(originalValue);
    }

    [Test]
    public async Task WriteTenantBatchAppliesSetAndRemoveOnceAndReturnsExactNeutralChanges()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(saveObserver);
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        CoordinatedSettingMutationWriteResult result = await ExecuteInTransactionAsync(
            context,
            cancellationToken => store.WriteTenantAsync(
                tenants.RequestedTenantId,
                [
                    SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true"),
                    SetTenant(tenants.RequestedTenantId, GuardedKeys[1], "false"),
                    RemoveTenant(tenants.RequestedTenantId, GuardedKeys[2])
                ],
                tenants.ActorUserId,
                OccurredAtUtc,
                cancellationToken));

        CoordinatedSettingValueChange[] expectedChanges =
        [
            new(GuardedKeys[0], " false \n", "true"),
            new(GuardedKeys[1], null, "false"),
            new(GuardedKeys[2], "true", null)
        ];
        await Assert.That(result.Changes.SequenceEqual(expectedChanges)).IsTrue();
        await Assert.That(saveObserver.SaveCount).IsEqualTo(1);

        await using ExploreDbContext verifyContext = fixture.CreateDbContext();
        TenantSetting[] rows = await verifyContext.TenantSettingOverrides
            .AsNoTracking()
            .Where(row => row.TenantId == tenants.RequestedTenantId && GuardedKeys.Contains(row.SettingKey))
            .OrderBy(row => row.SettingKey)
            .ToArrayAsync();
        await Assert.That(rows.Single(row => row.SettingKey == GuardedKeys[0]).Value).IsEqualTo("true");
        await Assert.That(rows.Single(row => row.SettingKey == GuardedKeys[1]).Value).IsEqualTo("false");
        await Assert.That(rows.Any(row => row.SettingKey == GuardedKeys[2])).IsFalse();
        await Assert.That(rows.All(row => row.CreatedBy == tenants.ActorUserId || row.UpdatedBy == tenants.ActorUserId)).IsTrue();
        await Assert.That(rows.All(row => row.CreatedAt == OccurredAtUtc || row.UpdatedAt == OccurredAtUtc)).IsTrue();
        await Assert.That(await ReadTenantValueAsync(tenants.LaterTenantId, GuardedKeys[0])).IsEqualTo("true");
        await AssertProviderRowsUnchangedAsync(tenants);
    }

    [Test]
    public async Task WriteInstanceBatchReplacesValueAndLockAndRemovesRowsToRevealRegistryDefaults()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(saveObserver);
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        CoordinatedSettingMutationWriteResult result = await ExecuteInTransactionAsync(
            context,
            cancellationToken => store.WriteInstanceAsync(
                [
                    SetSystem(GuardedKeys[0], "false", isLocked: true),
                    RemoveSystem(GuardedKeys[1])
                ],
                tenants.ActorUserId,
                OccurredAtUtc,
                cancellationToken));

        CoordinatedSettingValueChange[] expectedChanges =
        [
            new(GuardedKeys[0], " true \n", "false"),
            new(GuardedKeys[1], "false", null)
        ];
        await Assert.That(result.Changes.SequenceEqual(expectedChanges)).IsTrue();
        await Assert.That(saveObserver.SaveCount).IsEqualTo(1);

        await using ExploreDbContext verifyContext = fixture.CreateDbContext();
        SystemSetting updated = await verifyContext.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GuardedKeys[0]);
        await Assert.That(updated.Value).IsEqualTo("false");
        await Assert.That(updated.IsLocked).IsTrue();
        await Assert.That(updated.ValueType).IsEqualTo(SettingValueType.Boolean);
        await Assert.That(updated.UpdatedBy).IsEqualTo(tenants.ActorUserId);
        await Assert.That(updated.UpdatedAt).IsEqualTo(OccurredAtUtc);
        await Assert.That(await verifyContext.SystemSettings.AnyAsync(row => row.SettingKey == GuardedKeys[1])).IsFalse();
        await AssertProviderRowsUnchangedAsync(tenants);
    }

    [Test]
    public async Task WriteInstanceSetRecreatesMissingGuardedRowWithCanonicalDefinitionMetadata()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        string key = GuardedKeys[4];
        await using (ExploreDbContext deleteContext = fixture.CreateDbContext())
        {
            await deleteContext.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        }

        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(saveObserver);
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        CoordinatedSettingMutationWriteResult result = await ExecuteInTransactionAsync(
            context,
            cancellationToken => store.WriteInstanceAsync(
                [SetSystem(key, "false", isLocked: true)],
                tenants.ActorUserId,
                OccurredAtUtc,
                cancellationToken));

        await Assert.That(result.Changes.SequenceEqual(
            [new CoordinatedSettingValueChange(key, null, "false")])).IsTrue();
        await Assert.That(saveObserver.SaveCount).IsEqualTo(1);
        await using ExploreDbContext verifyContext = fixture.CreateDbContext();
        SystemSetting inserted = await verifyContext.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == key);
        SettingDefinition definition = SettingRegistry.Get(key)!;
        await Assert.That(inserted.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(inserted.Value).IsEqualTo("false");
        await Assert.That(inserted.IsLocked).IsTrue();
        await Assert.That(inserted.ValueType).IsEqualTo(definition.ValueType);
        await Assert.That(inserted.Category).IsEqualTo(definition.Category);
        await Assert.That(inserted.Description).IsEqualTo(definition.Description);
        await Assert.That(inserted.AllowedValues).IsNull();
        await Assert.That(inserted.CreatedBy).IsEqualTo(tenants.ActorUserId);
        await Assert.That(inserted.CreatedAt).IsEqualTo(OccurredAtUtc);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RemoveAbsentRowReturnsNoChangesAndDoesNotSave(bool instanceWrite)
    {
        SeededTenants tenants = await SeedContractStateAsync();
        string key = GuardedKeys[4];
        if (instanceWrite)
        {
            await using ExploreDbContext deleteContext = fixture.CreateDbContext();
            await deleteContext.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        }

        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(saveObserver);
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        CoordinatedSettingMutationWriteResult result = await ExecuteInTransactionAsync(
            context,
            cancellationToken => instanceWrite
                ? store.WriteInstanceAsync(
                    [RemoveSystem(key)],
                    tenants.ActorUserId,
                    OccurredAtUtc,
                    cancellationToken)
                : store.WriteTenantAsync(
                    tenants.RequestedTenantId,
                    [RemoveTenant(tenants.RequestedTenantId, key)],
                    tenants.ActorUserId,
                    OccurredAtUtc,
                    cancellationToken));

        await Assert.That(result.Changes).IsEmpty();
        await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
        await using ExploreDbContext verifyContext = fixture.CreateDbContext();
        bool rowExists = instanceWrite
            ? await verifyContext.SystemSettings.AnyAsync(row => row.SettingKey == key)
            : await verifyContext.TenantSettingOverrides.AnyAsync(row =>
                row.TenantId == tenants.RequestedTenantId && row.SettingKey == key);
        await Assert.That(rowExists).IsFalse();
    }

    [Test]
    public async Task SuccessfulStoreWriteIsUndoneByOuterTransactionRollback()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        await using (ExploreDbContext context = fixture.CreateDbContext())
        {
            ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);
            InjectedRollbackException? thrown = await Assert.ThrowsAsync<InjectedRollbackException>(() =>
                ExecuteInTransactionAsync(context, async cancellationToken =>
                {
                    await store.WriteTenantAsync(
                        tenants.RequestedTenantId,
                        [SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true")],
                        tenants.ActorUserId,
                        OccurredAtUtc,
                        cancellationToken);
                    await Assert.That((await context.TenantSettingOverrides.AsNoTracking().SingleAsync(
                            row => row.TenantId == tenants.RequestedTenantId
                                && row.SettingKey == GuardedKeys[0],
                            cancellationToken)).Value)
                        .IsEqualTo("true");
                    throw new InjectedRollbackException();
                }));
            await Assert.That(thrown).IsNotNull();
        }

        await Assert.That(await ReadTenantValueAsync(tenants.RequestedTenantId, GuardedKeys[0]))
            .IsEqualTo(" false \n");
    }

    [Test]
    public async Task SaveFailureAfterTrackingBatchLeavesNoCommittedPartialRows()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        var failure = new DbUpdateException("Deterministic coordinated-setting save failure.");
        var interceptor = new ThrowingSaveInterceptor(failure);
        await using (ExploreDbContext context = fixture.CreateDbContext(interceptor))
        {
            ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);
            DbUpdateException? thrown = await Assert.ThrowsAsync<DbUpdateException>(() =>
                ExecuteInTransactionAsync(
                    context,
                    cancellationToken => store.WriteTenantAsync(
                        tenants.RequestedTenantId,
                        [
                            SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true"),
                            SetTenant(tenants.RequestedTenantId, GuardedKeys[1], "false")
                        ],
                        tenants.ActorUserId,
                        OccurredAtUtc,
                        cancellationToken)));

            await Assert.That(thrown).IsSameReferenceAs(failure);
            await Assert.That(interceptor.SaveCount).IsEqualTo(1);
            await Assert.That(interceptor.SawTrackedChanges).IsTrue();
        }

        await Assert.That(await ReadTenantValueAsync(tenants.RequestedTenantId, GuardedKeys[0]))
            .IsEqualTo(" false \n");
        await using ExploreDbContext verifyContext = fixture.CreateDbContext();
        await Assert.That(await verifyContext.TenantSettingOverrides.AnyAsync(row =>
            row.TenantId == tenants.RequestedTenantId && row.SettingKey == GuardedKeys[1])).IsFalse();
    }

    [Test]
    public async Task EveryReadQueryAndWriteSaveReceivesTheCallerCancellationToken()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        using var cancellationSource = new CancellationTokenSource();
        CancellationToken callerToken = cancellationSource.Token;
        var commandObserver = new CommandTokenObserver();
        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(commandObserver, saveObserver);
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        await ExecuteInTransactionAsync(context, async cancellationToken =>
        {
            await store.ReadTenantSnapshotAsync(tenants.RequestedTenantId, cancellationToken);
            await store.ReadInstanceSnapshotAsync(cancellationToken);
            await store.WriteTenantAsync(
                tenants.RequestedTenantId,
                [SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true")],
                tenants.ActorUserId,
                OccurredAtUtc,
                cancellationToken);
        }, callerToken);

        await Assert.That(commandObserver.CommandTokens.Count).IsGreaterThanOrEqualTo(5);
        await Assert.That(commandObserver.CommandTokens.All(token => token == callerToken)).IsTrue();
        await Assert.That(saveObserver.SaveCount).IsEqualTo(1);
        await Assert.That(saveObserver.CancellationTokens.Single()).IsEqualTo(callerToken);
        await Assert.That(await ReadTenantValueAsync(tenants.RequestedTenantId, GuardedKeys[0]))
            .IsEqualTo("true");
    }

    [Test]
    public async Task SnapshotReadsUseDatabaseValuesInsteadOfTrackedOrResolverState()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        await using ExploreDbContext staleContext = fixture.CreateDbContext();
        _ = await staleContext.SystemSettings.SingleAsync(row => row.SettingKey == GuardedKeys[0]);
        _ = await staleContext.TenantSettingOverrides.SingleAsync(row =>
            row.TenantId == tenants.RequestedTenantId && row.SettingKey == GuardedKeys[0]);

        await using (ExploreDbContext writerContext = fixture.CreateDbContext())
        {
            await writerContext.SystemSettings
                .Where(row => row.SettingKey == GuardedKeys[0])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "false"));
            await writerContext.TenantSettingOverrides
                .Where(row => row.TenantId == tenants.RequestedTenantId && row.SettingKey == GuardedKeys[0])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "true"));
        }

        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(staleContext);
        PublicationPolicyMutationSnapshot snapshot = await ExecuteInTransactionAsync(
            staleContext,
            cancellationToken => store.ReadTenantSnapshotAsync(tenants.RequestedTenantId, cancellationToken));

        await Assert.That(snapshot.SystemValues.Single(row => row.Key == GuardedKeys[0]).JsonValue)
            .IsEqualTo("false");
        await Assert.That(snapshot.TenantValues.Single(row => row.Key == GuardedKeys[0]).JsonValue)
            .IsEqualTo("true");
    }

    [Test]
    public async Task TenantBatchValidFirstAndWrongTenantSecondRejectsWholeBatchBeforeEfMutation()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        string originalRequestedValue = await ReadTenantValueAsync(
            tenants.RequestedTenantId,
            GuardedKeys[0]);
        var commandObserver = new CommandTokenObserver();
        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(commandObserver, saveObserver);
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        await ExecuteInTransactionAsync(context, async cancellationToken =>
        {
            commandObserver.CommandTokens.Clear();
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteTenantAsync(
                tenants.RequestedTenantId,
                [
                    SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true"),
                    SetTenant(tenants.LaterTenantId, GuardedKeys[1], "false")
                ],
                tenants.ActorUserId,
                OccurredAtUtc,
                cancellationToken));

            await Assert.That(commandObserver.CommandTokens).IsEmpty();
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
        });
        await Assert.That(await ReadTenantValueAsync(tenants.RequestedTenantId, GuardedKeys[0]))
            .IsEqualTo(originalRequestedValue);
        await using ExploreDbContext verifyContext = fixture.CreateDbContext();
        await Assert.That(await verifyContext.TenantSettingOverrides.AnyAsync(row =>
            row.TenantId == tenants.LaterTenantId && row.SettingKey == GuardedKeys[1])).IsFalse();
    }

    [Test]
    public async Task InstanceBatchValidFirstAndInvalidDistinctSecondRejectsWholeBatchBeforeEfMutation()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        SystemRowState originalFirst = await ReadSystemRowStateAsync(GuardedKeys[0]);
        SystemRowState originalSecond = await ReadSystemRowStateAsync(GuardedKeys[1]);
        var commandObserver = new CommandTokenObserver();
        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(commandObserver, saveObserver);
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        await ExecuteInTransactionAsync(context, async cancellationToken =>
        {
            commandObserver.CommandTokens.Clear();
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteInstanceAsync(
                [
                    RemoveSystem(GuardedKeys[0]),
                    new PublicationPolicySettingMutation(
                        GuardedKeys[1],
                        (PublicationPolicyMutationKind)999,
                        "true",
                        TenantId: null,
                        IsLocked: false)
                ],
                tenants.ActorUserId,
                OccurredAtUtc,
                cancellationToken));

            await Assert.That(commandObserver.CommandTokens).IsEmpty();
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
        });
        await Assert.That(await ReadSystemRowStateAsync(GuardedKeys[0])).IsEqualTo(originalFirst);
        await Assert.That(await ReadSystemRowStateAsync(GuardedKeys[1])).IsEqualTo(originalSecond);
    }

    [Test]
    public async Task InvalidKeysShapesDuplicateKeysAndWrongTenantIdsAreRejectedBeforeEfMutation()
    {
        SeededTenants tenants = await SeedContractStateAsync();
        var commandObserver = new CommandTokenObserver();
        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(commandObserver, saveObserver);
        ICoordinatedSettingMutationStore store = new CoordinatedSettingMutationRepository(context);

        await ExecuteInTransactionAsync(context, async cancellationToken =>
        {
            commandObserver.CommandTokens.Clear();

            ImmutableArray<PublicationPolicySettingMutation>[] invalidTenantBatches =
        [
            default,
            [null!],
            [new(null!, PublicationPolicyMutationKind.Set, "true", tenants.RequestedTenantId, null)],
            [new("", PublicationPolicyMutationKind.Set, "true", tenants.RequestedTenantId, null)],
            [new("   ", PublicationPolicyMutationKind.Set, "true", tenants.RequestedTenantId, null)],
            [new(GuardedKeys[0], (PublicationPolicyMutationKind)999, "true", tenants.RequestedTenantId, null)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, null, tenants.RequestedTenantId, null)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "{", tenants.RequestedTenantId, null)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "1", tenants.RequestedTenantId, null)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "\"true\"", tenants.RequestedTenantId, null)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "true", tenants.RequestedTenantId, false)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Remove, "true", tenants.RequestedTenantId, null)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Remove, null, tenants.RequestedTenantId, true)],
            [SetTenant(tenants.LaterTenantId, GuardedKeys[0], "true")],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "true", TenantId: null, IsLocked: null)],
            [SetTenant(Guid.Empty, GuardedKeys[0], "true")],
            [
                SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true"),
                RemoveTenant(tenants.RequestedTenantId, GuardedKeys[0])
            ],
            [SetTenant(
                tenants.RequestedTenantId,
                GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider,
                "false")]
        ];

            foreach (ImmutableArray<PublicationPolicySettingMutation> mutations in invalidTenantBatches)
            {
                await Assert.ThrowsAsync<ArgumentException>(() => store.WriteTenantAsync(
                    tenants.RequestedTenantId,
                    mutations,
                    tenants.ActorUserId,
                    OccurredAtUtc,
                    cancellationToken));
            }
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteTenantAsync(
                Guid.Empty,
                [SetTenant(Guid.Empty, GuardedKeys[0], "true")],
                tenants.ActorUserId,
                OccurredAtUtc,
                cancellationToken));

            ImmutableArray<PublicationPolicySettingMutation>[] invalidInstanceBatches =
        [
            default,
            [null!],
            [new(null!, PublicationPolicyMutationKind.Set, "true", null, false)],
            [new("", PublicationPolicyMutationKind.Set, "true", null, false)],
            [new("   ", PublicationPolicyMutationKind.Set, "true", null, false)],
            [new(GuardedKeys[0], (PublicationPolicyMutationKind)999, "true", null, false)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, null, null, false)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "{", null, false)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "1", null, false)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "\"false\"", null, false)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Set, "true", null, IsLocked: null)],
            [SetTenant(tenants.RequestedTenantId, GuardedKeys[0], "true")],
            [SetTenant(Guid.Empty, GuardedKeys[0], "true")],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Remove, "true", null, null)],
            [new(GuardedKeys[0], PublicationPolicyMutationKind.Remove, null, null, false)],
            [
                SetSystem(GuardedKeys[0], "true", isLocked: false),
                RemoveSystem(GuardedKeys[0])
            ],
            [SetSystem(
                GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider,
                "false",
                isLocked: false)]
        ];
            foreach (ImmutableArray<PublicationPolicySettingMutation> mutations in invalidInstanceBatches)
            {
                await Assert.ThrowsAsync<ArgumentException>(() => store.WriteInstanceAsync(
                    mutations,
                    tenants.ActorUserId,
                    OccurredAtUtc,
                    cancellationToken));
            }

            await Assert.That(commandObserver.CommandTokens).IsEmpty();
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();
        });

        await Assert.That(await ReadTenantValueAsync(tenants.RequestedTenantId, GuardedKeys[0]))
            .IsEqualTo(" false \n");
        await AssertProviderRowsUnchangedAsync(tenants);
    }

    [Test]
    public async Task LookupSeedMaterializesCanonicalReportingIntakeSystemSettingAndDefinitionSemantics()
    {
        await fixture.ResetAsync();
        await using (ExploreDbContext resetContext = fixture.CreateDbContext())
        {
            await resetContext.SystemSettings
                .Where(row => row.SettingKey == GuardedKeys[0])
                .ExecuteDeleteAsync();
            await LookupTableSeeder.SeedAsync(resetContext);
        }

        await using ExploreDbContext context = fixture.CreateDbContext();
        SystemSetting setting = await context.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GuardedKeys[0]);
        SettingDefinition definition = EventReportingIntakeSettingDefinitions.IntakeEnabled;
        Guid unusedDeterministicId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000554");

        await Assert.That(SeedIds.SystemSettingEventReportingIntakeEnabledId)
            .IsEqualTo(unusedDeterministicId);
        await Assert.That(setting.Id).IsEqualTo(unusedDeterministicId);
        await Assert.That(setting.Value).IsEqualTo("true");
        await Assert.That(setting.ValueType).IsEqualTo(SettingValueType.Boolean);
        await Assert.That(setting.IsLocked).IsFalse();
        await Assert.That(definition.DefaultValue).IsEqualTo("true");
        await Assert.That(definition.IsSensitive).IsFalse();
        await Assert.That(definition.IsLockable).IsTrue();
        await Assert.That(definition.RequiresCoordinatedMutation).IsTrue();
    }

    private async Task<SeededTenants> SeedContractStateAsync()
    {
        await fixture.ResetAsync();
        Guid actorUserId = Guid.CreateVersion7();
        Guid earlierTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a001");
        Guid requestedTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a002");
        Guid laterTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-00000000a003");

        await using ExploreDbContext context = fixture.CreateDbContext();
        Dictionary<string, SystemSetting> existingSystems = await context.SystemSettings
            .Where(row => GuardedKeys.Contains(row.SettingKey)
                || row.SettingKey == GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider)
            .ToDictionaryAsync(row => row.SettingKey);
        string[] values = [" true \n", "false", "true", "false", "true"];
        for (int index = 0; index < GuardedKeys.Length; index++)
        {
            string key = GuardedKeys[index];
            if (!existingSystems.TryGetValue(key, out SystemSetting? setting))
            {
                setting = new SystemSetting
                {
                    Id = key == GuardedKeys[0]
                        ? SeedIds.SystemSettingEventReportingIntakeEnabledId
                        : Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = values[index],
                    ValueType = SettingValueType.Boolean,
                    CreatedAt = OccurredAtUtc
                };
                context.SystemSettings.Add(setting);
            }

            setting.Value = values[index];
            setting.ValueType = SettingValueType.Boolean;
            setting.IsLocked = index is 0 or 3;
            setting.UpdatedAt = null;
            setting.UpdatedBy = null;
        }

        const string providerValue = "provider-row-must-not-change";
        if (!existingSystems.TryGetValue(
                GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider,
                out SystemSetting? providerSetting))
        {
            providerSetting = new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider,
                Value = providerValue,
                ValueType = SettingValueType.String,
                CreatedAt = OccurredAtUtc
            };
            context.SystemSettings.Add(providerSetting);
        }
        providerSetting.Value = providerValue;
        providerSetting.IsLocked = true;

        Tenant[] tenantRows =
        [
            CreateTenant(earlierTenantId, "coordinated-earlier"),
            CreateTenant(requestedTenantId, "coordinated-requested"),
            CreateTenant(laterTenantId, "coordinated-later")
        ];
        context.Tenants.AddRange(tenantRows);
        await context.SaveChangesAsync();

        context.TenantSettingOverrides.AddRange(
            CreateTenantSetting(earlierTenantId, GuardedKeys[1], "false"),
            CreateTenantSetting(requestedTenantId, GuardedKeys[0], " false \n"),
            CreateTenantSetting(requestedTenantId, GuardedKeys[2], "true"),
            CreateTenantSetting(laterTenantId, GuardedKeys[0], "true"),
            CreateTenantSetting(requestedTenantId, GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, providerValue),
            CreateTenantSetting(laterTenantId, GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, providerValue));
        await context.SaveChangesAsync();

        return new SeededTenants(earlierTenantId, requestedTenantId, laterTenantId, actorUserId, providerValue);
    }

    private static Tenant CreateTenant(Guid id, string slug) => new()
    {
        Id = id,
        FullName = slug,
        Slug = slug,
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static TenantSetting CreateTenantSetting(Guid tenantId, string key, string value) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        SettingKey = key,
        Value = value,
        CreatedAt = OccurredAtUtc
    };

    private static Task ExecuteInTransactionAsync(
        ExploreDbContext context,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default) =>
        new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(operation, cancellationToken);

    private static Task<T> ExecuteInTransactionAsync<T>(
        ExploreDbContext context,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default) =>
        new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(operation, cancellationToken);

    private async Task<string> ReadTenantValueAsync(Guid tenantId, string key)
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        return await context.TenantSettingOverrides.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.SettingKey == key)
            .Select(row => row.Value)
            .SingleAsync();
    }

    private async Task<SystemRowState> ReadSystemRowStateAsync(string key)
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        return await context.SystemSettings.AsNoTracking()
            .Where(row => row.SettingKey == key)
            .Select(row => new SystemRowState(row.Id, row.Value, row.IsLocked))
            .SingleAsync();
    }

    private async Task AssertProviderRowsUnchangedAsync(SeededTenants tenants)
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        string systemValue = await context.SystemSettings.AsNoTracking()
            .Where(row => row.SettingKey == GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider)
            .Select(row => row.Value)
            .SingleAsync();
        string[] tenantValues = await context.TenantSettingOverrides.AsNoTracking()
            .Where(row => row.SettingKey == GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider)
            .OrderBy(row => row.TenantId)
            .Select(row => row.Value)
            .ToArrayAsync();
        await Assert.That(systemValue).IsEqualTo(tenants.ProviderValue);
        await Assert.That(tenantValues.SequenceEqual([tenants.ProviderValue, tenants.ProviderValue])).IsTrue();
    }

    private static async Task AssertSystemSnapshotAsync(
        ImmutableArray<PublicationPolicySystemValueSnapshot> snapshots)
    {
        PublicationPolicySystemValueSnapshot[] expected =
        [
            new(GuardedKeys[0], " true \n", true),
            new(GuardedKeys[1], "false", false),
            new(GuardedKeys[2], "true", false),
            new(GuardedKeys[3], "false", true),
            new(GuardedKeys[4], "true", false)
        ];
        await Assert.That(snapshots.SequenceEqual(expected)).IsTrue();
    }

    private static PublicationPolicySettingMutation SetTenant(Guid tenantId, string key, string jsonValue) =>
        new(key, PublicationPolicyMutationKind.Set, jsonValue, tenantId, IsLocked: null);

    private static PublicationPolicySettingMutation RemoveTenant(Guid tenantId, string key) =>
        new(key, PublicationPolicyMutationKind.Remove, JsonValue: null, tenantId, IsLocked: null);

    private static PublicationPolicySettingMutation SetSystem(string key, string jsonValue, bool isLocked) =>
        new(key, PublicationPolicyMutationKind.Set, jsonValue, TenantId: null, isLocked);

    private static PublicationPolicySettingMutation RemoveSystem(string key) =>
        new(key, PublicationPolicyMutationKind.Remove, JsonValue: null, TenantId: null, IsLocked: null);

    private static bool IsProviderReportingKey(string key) =>
        key.StartsWith("reporting.", StringComparison.Ordinal)
        || key.StartsWith("tenant_delegation.lock_reporting", StringComparison.Ordinal);

    private static string[] GuardedKeys => PublicationPolicySettingKeys.All.ToArray();

    private sealed record SeededTenants(
        Guid EarlierTenantId,
        Guid RequestedTenantId,
        Guid LaterTenantId,
        Guid ActorUserId,
        string ProviderValue);

    private sealed record SystemRowState(Guid Id, string Value, bool IsLocked);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class InjectedRollbackException : Exception;

    private sealed class SaveObserver : SaveChangesInterceptor
    {
        public int SaveCount { get; private set; }
        public List<CancellationToken> CancellationTokens { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            CancellationTokens.Add(cancellationToken);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingSaveInterceptor(DbUpdateException failure) : SaveChangesInterceptor
    {
        public int SaveCount { get; private set; }
        public bool SawTrackedChanges { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SawTrackedChanges = eventData.Context?.ChangeTracker.Entries().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified) == true;
            return ValueTask.FromException<InterceptionResult<int>>(failure);
        }
    }

    private sealed class CommandTokenObserver : DbCommandInterceptor
    {
        public List<CancellationToken> CommandTokens { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandTokens.Add(cancellationToken);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CommandTokens.Add(cancellationToken);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            CommandTokens.Add(cancellationToken);
            return ValueTask.FromResult(result);
        }
    }
}
