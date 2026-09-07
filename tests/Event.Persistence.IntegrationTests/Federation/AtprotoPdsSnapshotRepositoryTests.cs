// ABOUTME: PostgreSQL integration checks for complete AT Protocol PDS snapshot reconciliation.
// ABOUTME: Covers canonical idempotency, missing-record tombstones, rejected-record invalidation, and cursor isolation.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Federation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoPdsSnapshotRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CompleteSnapshot_ReconcilesCanonicalStateWithoutLocalAggregatesOrCursorMutation()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        AtprotoRecord missing = Record(did, "missing", 100, now);
        AtprotoRecord rejected = Record(did, "rejected", 100, now);
        context.AtprotoRecords.AddRange(missing, rejected);
        context.AtprotoEventProjections.AddRange(
            Projection(missing, 100, now),
            Projection(rejected, 100, now));
        Guid tenantId = Guid.CreateVersion7();
        context.Tenants.Add(Tenant(tenantId));
        context.AtprotoRecordTenantPresentations.AddRange(
            Presentation(tenantId, missing, 100, now),
            Presentation(tenantId, rejected, 100, now));
        await context.SaveChangesAsync();
        AtprotoRecord accepted = Record(did, "3maccepted222", 0, now);
        accepted.Cid = "bafy-accepted";
        accepted.RecordJson = "{\"name\":\"accepted\"}";
        AtprotoEventProjection acceptedProjection = Projection(accepted, 0, now);
        acceptedProjection.Name = "Accepted";
        var request = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [new(accepted.Collection, accepted.RecordKey)],
                [new(accepted, acceptedProjection)])],
            [tenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));

        bool applied = await repository.TryReconcileAsync(request, CancellationToken.None);

        await Assert.That(applied).IsTrue();
        context.ChangeTracker.Clear();
        Dictionary<string, AtprotoRecord> records = await context.AtprotoRecords.AsNoTracking()
            .ToDictionaryAsync(value => value.RecordKey);
        Dictionary<Guid, AtprotoEventProjection> projections = await context.AtprotoEventProjections.AsNoTracking()
            .ToDictionaryAsync(value => value.AtprotoRecordId);
        Dictionary<Guid, AtprotoRecordTenantPresentation> presentations = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(value => value.AtprotoRecordId);
        AtprotoJetstreamConsumerState state = await context.AtprotoJetstreamConsumerStates.AsNoTracking().SingleAsync();
        await Assert.That(records[accepted.RecordKey].SourceVersion).IsEqualTo(200);
        await Assert.That(records[accepted.RecordKey].SourceCursor).IsNull();
        await Assert.That(records[accepted.RecordKey].Cid).IsEqualTo("bafy-accepted");
        await Assert.That(records[missing.RecordKey].TombstonedAt).IsNotNull();
        await Assert.That(records[missing.RecordKey].SourceVersion).IsEqualTo(200);
        await Assert.That(records[rejected.RecordKey].TombstonedAt).IsNotNull();
        await Assert.That(records[rejected.RecordKey].SourceVersion).IsEqualTo(200);
        await Assert.That(projections.ContainsKey(missing.Id)).IsFalse();
        await Assert.That(projections.ContainsKey(rejected.Id)).IsFalse();
        await Assert.That(projections[records[accepted.RecordKey].Id].Name).IsEqualTo("Accepted");
        await Assert.That(presentations[missing.Id].IsVisible).IsFalse();
        await Assert.That(presentations[rejected.Id].IsVisible).IsFalse();
        await Assert.That(presentations[records[accepted.RecordKey].Id].IsVisible).IsTrue();
        await Assert.That(state.Cursor).IsEqualTo(0);
    }

    [Test]
    public async Task DuplicateOrOlderSnapshots_AreIdempotentAndPreserveHigherVersions()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        Guid tenantId = Guid.CreateVersion7();
        context.Tenants.Add(Tenant(tenantId));
        AtprotoRecord canonical = Record(did, "3mcanonical222", 200, now);
        context.AtprotoRecords.Add(canonical);
        context.AtprotoEventProjections.Add(Projection(canonical, 200, now));
        context.AtprotoRecordTenantPresentations.Add(Presentation(tenantId, canonical, 200, now));
        await context.SaveChangesAsync();
        AtprotoRecord older = Record(did, canonical.RecordKey, 0, now);
        older.Cid = "bafy-older";
        older.RecordJson = "{\"name\":\"older\"}";
        AtprotoEventProjection olderProjection = Projection(older, 0, now);
        olderProjection.Name = "Older";
        var request = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [new(older.Collection, older.RecordKey)],
                [new(older, olderProjection)])],
            [tenantId],
            SnapshotVersion: 150,
            ObservedAt: now.AddSeconds(1));

        bool applied = await repository.TryReconcileAsync(request, CancellationToken.None);

        await Assert.That(applied).IsTrue();
        context.ChangeTracker.Clear();
        AtprotoRecord persisted = await context.AtprotoRecords.AsNoTracking().SingleAsync();
        AtprotoEventProjection projection = await context.AtprotoEventProjections.AsNoTracking().SingleAsync();
        AtprotoRecordTenantPresentation presentation = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(persisted.SourceVersion).IsEqualTo(200);
        await Assert.That(persisted.Cid).IsEqualTo(canonical.Cid);
        await Assert.That(projection.SourceVersion).IsEqualTo(200);
        await Assert.That(presentation.SourceVersion).IsEqualTo(200);
    }

    [Test]
    public async Task MissingInboundRecords_AreTombstonedAndProjectionsRemoved()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        Guid tenantId = Guid.CreateVersion7();
        context.Tenants.Add(Tenant(tenantId));
        AtprotoRecord present = Record(did, "3mpresent222", 100, now);
        AtprotoRecord missing = Record(did, "3mmissing222", 100, now);
        context.AtprotoRecords.AddRange(present, missing);
        context.AtprotoEventProjections.AddRange(
            Projection(present, 100, now),
            Projection(missing, 100, now));
        context.AtprotoRecordTenantPresentations.AddRange(
            Presentation(tenantId, present, 100, now),
            Presentation(tenantId, missing, 100, now));
        await context.SaveChangesAsync();
        AtprotoRecord presentSnapshot = Record(did, present.RecordKey, 0, now);
        presentSnapshot.Cid = "bafy-present";
        presentSnapshot.RecordJson = "{\"name\":\"present\"}";
        var request = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [new(presentSnapshot.Collection, presentSnapshot.RecordKey)],
                [new(presentSnapshot, Projection(presentSnapshot, 0, now))])],
            [tenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));

        bool applied = await repository.TryReconcileAsync(request, CancellationToken.None);

        await Assert.That(applied).IsTrue();
        context.ChangeTracker.Clear();
        Dictionary<string, AtprotoRecord> records = await context.AtprotoRecords.AsNoTracking()
            .ToDictionaryAsync(value => value.RecordKey);
        Dictionary<Guid, AtprotoRecordTenantPresentation> presentations = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(value => value.AtprotoRecordId);
        await Assert.That(records[present.RecordKey].TombstonedAt).IsNull();
        await Assert.That(records[present.RecordKey].SourceVersion).IsEqualTo(200);
        await Assert.That(records[missing.RecordKey].TombstonedAt).IsNotNull();
        await Assert.That(records[missing.RecordKey].SourceVersion).IsEqualTo(200);
        await Assert.That(await context.AtprotoEventProjections.AnyAsync(value => value.AtprotoRecordId == missing.Id)).IsFalse();
        await Assert.That(presentations[present.Id].IsVisible).IsTrue();
        await Assert.That(presentations[missing.Id].IsVisible).IsFalse();
    }

    [Test]
    public async Task OlderSnapshot_DoesNotOverwriteRecentTombstone()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        Guid tenantId = Guid.CreateVersion7();
        context.Tenants.Add(Tenant(tenantId));
        AtprotoRecord tombstoned = Record(did, "3mtombstoned222", 300, now);
        tombstoned.TombstonedAt = now;
        context.AtprotoRecords.Add(tombstoned);
        context.AtprotoRecordTenantPresentations.Add(Presentation(tenantId, tombstoned, 300, now));
        await context.SaveChangesAsync();
        AtprotoRecord older = Record(did, tombstoned.RecordKey, 0, now);
        older.Cid = "bafy-revived";
        older.RecordJson = "{\"name\":\"revived\"}";
        var request = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [new(older.Collection, older.RecordKey)],
                [new(older, Projection(older, 0, now))])],
            [tenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));

        bool applied = await repository.TryReconcileAsync(request, CancellationToken.None);

        await Assert.That(applied).IsTrue();
        context.ChangeTracker.Clear();
        AtprotoRecord persisted = await context.AtprotoRecords.AsNoTracking().SingleAsync();
        AtprotoRecordTenantPresentation presentation = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(persisted.TombstonedAt).IsNotNull();
        await Assert.That(persisted.SourceVersion).IsEqualTo(300);
        await Assert.That(await context.AtprotoEventProjections.AnyAsync()).IsFalse();
        await Assert.That(presentation.IsVisible).IsFalse();
    }

    [Test]
    public async Task RetryReconciliation_RollsBackFailureAndRetriesCleanly()
    {
        await fixture.ResetAsync();
        DateTime now = DateTime.UtcNow;
        Guid tenantId = Guid.CreateVersion7();
        Guid stateId = Guid.CreateVersion7();
        Guid leaseToken = Guid.CreateVersion7();
        const string service = "https://jetstream.example";
        const string did = "did:plc:snapshot-owner";
        AtprotoRecord canonical = Record(did, "3mretryrec222", 100, now);
        var failure = new FailAfterSaveChangesInterceptor();
        await using ExploreDbContext fixtureContext = fixture.CreateDbContext(failure);
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>(
                (DbContextOptions<ExploreDbContext>)fixtureContext.GetService<IDbContextOptions>())
            .ReplaceService<IExecutionStrategyFactory, RetryOnceExecutionStrategyFactory>()
            .Options;
        await using (ExploreDbContext seedContext = fixture.CreateDbContext())
        {
            seedContext.Tenants.Add(Tenant(tenantId));
            seedContext.AtprotoJetstreamConsumerStates.Add(new()
            {
                Id = stateId,
                Service = service,
                Cursor = 50,
                LeaseOwner = "snapshot-worker",
                LeaseToken = leaseToken,
                LeaseExpiresAt = now.AddMinutes(5),
                LeaseFence = 1,
                UpdatedAt = now
            });
            seedContext.AtprotoRecords.Add(canonical);
            seedContext.AtprotoEventProjections.Add(Projection(canonical, 100, now));
            seedContext.AtprotoRecordTenantPresentations.Add(Presentation(tenantId, canonical, 100, now));
            await seedContext.SaveChangesAsync();
        }

        AtprotoRecord recovered = Record(did, canonical.RecordKey, 0, now);
        recovered.Cid = "bafy-recovered";
        recovered.RecordJson = "{\"name\":\"recovered\"}";
        AtprotoEventProjection recoveredProjection = Projection(recovered, 0, now);
        recoveredProjection.Name = "Recovered";
        var request = new AtprotoPdsSnapshotApplyRequest(
            new(stateId, service, 50, leaseToken, 1),
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [new(recovered.Collection, recovered.RecordKey)],
                [new(recovered, recoveredProjection)])],
            [tenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));

        await using (var retryContext = new ExploreDbContext(options))
        {
            retryContext.EnableTenantFilterBypass("PostgreSQL ATProto snapshot retry test.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            bool applied = await new AtprotoJetstreamRepository(retryContext)
                .TryReconcileAsync(request, timeout.Token);
            await Assert.That(applied).IsTrue();
        }

        await using ExploreDbContext verifyContext = fixture.CreateDbContext();
        AtprotoRecord persisted = await verifyContext.AtprotoRecords.AsNoTracking().SingleAsync();
        AtprotoEventProjection projection = await verifyContext.AtprotoEventProjections.AsNoTracking().SingleAsync();
        AtprotoRecordTenantPresentation presentation = await verifyContext.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(failure.FailuresInjected).IsEqualTo(1);
        await Assert.That(failure.SavesObserved).IsEqualTo(2);
        await Assert.That(persisted.SourceVersion).IsEqualTo(200);
        await Assert.That(persisted.SourceCursor).IsNull();
        await Assert.That(persisted.Cid).IsEqualTo("bafy-recovered");
        await Assert.That(projection.Name).IsEqualTo("Recovered");
        await Assert.That(projection.SourceVersion).IsEqualTo(200);
        await Assert.That(presentation.IsVisible).IsTrue();
        await Assert.That(presentation.SourceVersion).IsEqualTo(200);
    }

    [Test]
    public async Task CancellationAfterSaveChanges_RollsBackClaimAndAllowsFreshClaim()
    {
        await fixture.ResetAsync();
        DateTime now = DateTime.UtcNow;
        const string service = "https://jetstream.example/cancelled-claim";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancelAfterSave = new CancelAfterSaveChangesInterceptor(cancellation);

        await using (var cancelledContext = fixture.CreateDbContext(cancelAfterSave))
        {
            cancelledContext.EnableTenantFilterBypass("PostgreSQL ATProto cancelled claim test.");
            var repository = new AtprotoJetstreamRepository(cancelledContext);
            bool cancelled = false;
            try
            {
                await repository.TryClaimAsync(service, "worker-a", now, TimeSpan.FromMinutes(5), cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            await Assert.That(cancelled).IsTrue();
            await Assert.That(cancelAfterSave.CancellationsInjected).IsEqualTo(1);
            await Assert.That(cancelledContext.ChangeTracker.Entries()).IsEmpty();
        }

        await using (var freshContext = fixture.CreateDbContext())
        {
            freshContext.EnableTenantFilterBypass("PostgreSQL ATProto fresh claim verification.");
            var repository = new AtprotoJetstreamRepository(freshContext);
            AtprotoJetstreamClaim? freshClaim = await repository.TryClaimAsync(
                service, "worker-b", now.AddSeconds(1), TimeSpan.FromMinutes(5));
            await Assert.That(freshClaim).IsNotNull();
            await Assert.That(freshClaim!.Service).IsEqualTo(service);
            AtprotoJetstreamConsumerState state = await freshContext.AtprotoJetstreamConsumerStates
                .SingleAsync(s => s.Id == freshClaim.ConsumerStateId);
            await Assert.That(state.LeaseOwner).IsEqualTo("worker-b");
        }
    }

    [Test]
    public async Task CancelAfterSaveChanges_CancelsReconciliationAndRollsBackCleanly()
    {
        await fixture.ResetAsync();
        DateTime now = DateTime.UtcNow;
        Guid tenantId = Guid.CreateVersion7();
        Guid stateId = Guid.CreateVersion7();
        Guid leaseToken = Guid.CreateVersion7();
        const string service = "https://jetstream.example/cancelled-reconcile";
        const string did = "did:plc:snapshot-cancel";
        AtprotoRecord canonical = Record(did, "3mcancelrec222", 100, now);

        await using (ExploreDbContext seedContext = fixture.CreateDbContext())
        {
            seedContext.Tenants.Add(Tenant(tenantId));
            seedContext.AtprotoJetstreamConsumerStates.Add(new()
            {
                Id = stateId,
                Service = service,
                Cursor = 0,
                LeaseOwner = "snapshot-worker",
                LeaseToken = leaseToken,
                LeaseExpiresAt = now.AddMinutes(5),
                LeaseFence = 1,
                UpdatedAt = now
            });
            seedContext.AtprotoRecords.Add(canonical);
            seedContext.AtprotoEventProjections.Add(Projection(canonical, 100, now));
            seedContext.AtprotoRecordTenantPresentations.Add(Presentation(tenantId, canonical, 100, now));
            await seedContext.SaveChangesAsync();
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancelAfterSave = new CancelAfterSaveChangesInterceptor(cancellation);
        AtprotoRecord recovered = Record(did, canonical.RecordKey, 0, now);
        recovered.Cid = "bafy-recovered";
        recovered.RecordJson = "{\"name\":\"recovered\"}";
        AtprotoEventProjection recoveredProjection = Projection(recovered, 0, now);
        recoveredProjection.Name = "Recovered";
        var request = new AtprotoPdsSnapshotApplyRequest(
            new(stateId, service, 0, leaseToken, 1),
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [new(recovered.Collection, recovered.RecordKey)],
                [new(recovered, recoveredProjection)])],
            [tenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));

        await using (var cancelledContext = fixture.CreateDbContext(cancelAfterSave))
        {
            cancelledContext.EnableTenantFilterBypass("PostgreSQL ATProto cancelled reconcile test.");
            bool cancelled = false;
            try
            {
                await new AtprotoJetstreamRepository(cancelledContext)
                    .TryReconcileAsync(request, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            await Assert.That(cancelled).IsTrue();
            await Assert.That(cancelAfterSave.CancellationsInjected).IsEqualTo(1);
            await Assert.That(cancelledContext.ChangeTracker.Entries()).IsEmpty();
        }

        await using (ExploreDbContext freshContext = fixture.CreateDbContext())
        {
            freshContext.EnableTenantFilterBypass("PostgreSQL ATProto fresh reconcile verification.");
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            AtprotoRecord rolledBack = await freshContext.AtprotoRecords.AsNoTracking().SingleAsync();
            AtprotoJetstreamConsumerState rolledBackState = await freshContext.AtprotoJetstreamConsumerStates.SingleAsync();
            await Assert.That(rolledBack.SourceVersion).IsEqualTo(100);
            await Assert.That(rolledBack.SourceCursor).IsEqualTo(100);
            await Assert.That(rolledBack.Cid).IsEqualTo(canonical.Cid);
            await Assert.That(rolledBackState.Cursor).IsEqualTo(0);
            bool applied = await new AtprotoJetstreamRepository(freshContext)
                .TryReconcileAsync(request, recovery.Token);
            freshContext.ChangeTracker.Clear();
            AtprotoRecord persisted = await freshContext.AtprotoRecords.AsNoTracking().SingleAsync();
            AtprotoJetstreamConsumerState recoveredState = await freshContext.AtprotoJetstreamConsumerStates.SingleAsync();
            await Assert.That(applied).IsTrue();
            await Assert.That(persisted.SourceVersion).IsEqualTo(200);
            await Assert.That(persisted.SourceCursor).IsNull();
            await Assert.That(persisted.Cid).IsEqualTo("bafy-recovered");
            await Assert.That(recoveredState.Cursor).IsEqualTo(0);
        }
    }

    [Test]
    public async Task PresentOnlyProtocolRecordKey_AllowsUpTo512WithoutMaterializingAndRejectsOversize()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        Guid tenantId = Guid.CreateVersion7();
        context.Tenants.Add(Tenant(tenantId));
        AtprotoRecord canonical = Record(did, "3mmaterial222", 100, now);
        AtprotoRecord nonTidCanonical = Record(did, "self", 100, now);
        context.AtprotoRecords.AddRange(canonical, nonTidCanonical);
        context.AtprotoEventProjections.AddRange(
            Projection(canonical, 100, now),
            Projection(nonTidCanonical, 100, now));
        context.AtprotoRecordTenantPresentations.AddRange(
            Presentation(tenantId, canonical, 100, now),
            Presentation(tenantId, nonTidCanonical, 100, now));
        await context.SaveChangesAsync();
        AtprotoRecord recovered = Record(did, canonical.RecordKey, 0, now);
        string validPresentOnlyKey = new('a', 512);
        var validRequest = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [
                    new(recovered.Collection, recovered.RecordKey),
                    new(recovered.Collection, validPresentOnlyKey),
                    new(nonTidCanonical.Collection, nonTidCanonical.RecordKey)
                ],
                [new(recovered, Projection(recovered, 0, now))])],
            [tenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));
        AtprotoPdsSnapshotApplyRequest WithPresentOnlyKey(string recordKey) => validRequest with
        {
            Snapshots =
            [
                validRequest.Snapshots[0] with
                {
                    PresentIdentities =
                    [
                        new(recovered.Collection, recovered.RecordKey),
                        new(recovered.Collection, recordKey)
                    ]
                }
            ],
            SnapshotVersion = 201,
            ObservedAt = now.AddSeconds(2)
        };

        bool applied = await repository.TryReconcileAsync(validRequest, CancellationToken.None);
        bool oversizedRejected = await repository.TryReconcileAsync(
            WithPresentOnlyKey(new string('b', 513)),
            CancellationToken.None);
        bool dotRejected = await repository.TryReconcileAsync(
            WithPresentOnlyKey("."),
            CancellationToken.None);
        bool dotDotRejected = await repository.TryReconcileAsync(
            WithPresentOnlyKey(".."),
            CancellationToken.None);
        AtprotoRecord rejectedMaterialization = Record(did, nonTidCanonical.RecordKey, 0, now);
        bool nonTidMaterializationRejected = await repository.TryReconcileAsync(
            validRequest with
            {
                Snapshots =
                [
                    validRequest.Snapshots[0] with
                    {
                        Items =
                        [
                            new(recovered, Projection(recovered, 0, now)),
                            new(rejectedMaterialization, Projection(rejectedMaterialization, 0, now))
                        ]
                    }
                ],
                SnapshotVersion = 201,
                ObservedAt = now.AddSeconds(2)
            },
            CancellationToken.None);

        context.ChangeTracker.Clear();
        Dictionary<string, AtprotoRecord> records = await context.AtprotoRecords.AsNoTracking()
            .ToDictionaryAsync(value => value.RecordKey);
        AtprotoRecordTenantPresentation rejectedPresentation = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(value => value.AtprotoRecordId == nonTidCanonical.Id);
        await Assert.That(applied).IsTrue();
        await Assert.That(oversizedRejected).IsFalse();
        await Assert.That(dotRejected).IsFalse();
        await Assert.That(dotDotRejected).IsFalse();
        await Assert.That(nonTidMaterializationRejected).IsFalse();
        await Assert.That(records).Count().IsEqualTo(2);
        await Assert.That(records[canonical.RecordKey].SourceVersion).IsEqualTo(200);
        await Assert.That(records[canonical.RecordKey].TombstonedAt).IsNull();
        await Assert.That(records[nonTidCanonical.RecordKey].SourceVersion).IsEqualTo(100);
        await Assert.That(records[nonTidCanonical.RecordKey].TombstonedAt).IsNull();
        await Assert.That(await context.AtprotoEventProjections
            .AnyAsync(value => value.AtprotoRecordId == nonTidCanonical.Id)).IsFalse();
        await Assert.That(rejectedPresentation.IsVisible).IsFalse();
        await Assert.That(rejectedPresentation.SourceVersion).IsEqualTo(200);
    }

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        FullName = "Snapshot tenant",
        Slug = $"snapshot-{id:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static AtprotoRecord Record(
        string did,
        string key,
        long version,
        DateTime observedAt,
        string collection = "community.lexicon.calendar.event") => new()
        {
            Id = Guid.CreateVersion7(),
            Did = did,
            Collection = collection,
            RecordKey = key,
            Cid = $"bafy-{key}",
            Uri = $"at://{did}/{collection}/{key}",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = version,
            SourceCursor = version,
            RecordJson = $"{{\"name\":\"{key}\"}}",
            RecordHash = new string('a', 64),
            IndexedAt = observedAt,
            UpdatedAt = observedAt
        };

    private static AtprotoEventProjection Projection(
        AtprotoRecord record,
        long version,
        DateTime observedAt) => new()
        {
            AtprotoRecordId = record.Id,
            Name = record.RecordKey,
            CreatedAt = new DateTimeOffset(observedAt),
            SourceVersion = version,
            MaterializedAt = observedAt
        };

    private static AtprotoRecordTenantPresentation Presentation(
        Guid tenantId,
        AtprotoRecord record,
        long version,
        DateTime observedAt) => new()
        {
            TenantId = tenantId,
            AtprotoRecordId = record.Id,
            IsVisible = true,
            SourceVersion = version,
            EvaluatedAt = observedAt
        };

    private sealed class RetryOnceExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryOnceExecutionStrategy(dependencies.CurrentContext.Context);
    }

    private sealed class RetryOnceExecutionStrategy(DbContext context)
        : ExecutionStrategy(context, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TestTransientException;
    }

    private sealed class TestTransientException : Exception;

    private sealed class FailAfterSaveChangesInterceptor : SaveChangesInterceptor
    {
        public int FailuresInjected { get; private set; }
        public int SavesObserved { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            SavesObserved++;
            await Assert.That(eventData.Context!.Database.CurrentTransaction).IsNotNull();
            if (FailuresInjected == 0)
            {
                // SaveChanges has accepted the tracked state, but the real transaction has not committed.
                // Disposal must roll it back, and the retry must reload instead of reusing that state.
                FailuresInjected++;
                throw new TestTransientException();
            }

            return result;
        }
    }

    private sealed class CancelAfterSaveChangesInterceptor(CancellationTokenSource cancellation)
        : SaveChangesInterceptor
    {
        private int _shouldCancel = 1;

        public int CancellationsInjected { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _shouldCancel, 0) == 0)
            {
                return ValueTask.FromResult(result);
            }

            CancellationsInjected++;
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
