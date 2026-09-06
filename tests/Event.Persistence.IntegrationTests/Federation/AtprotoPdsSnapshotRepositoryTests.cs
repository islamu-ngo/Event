// ABOUTME: Relational SQLite checks for complete AT Protocol PDS snapshot reconciliation.
// ABOUTME: Covers canonical idempotency, missing-record tombstones, rejected-record invalidation, and cursor isolation.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class AtprotoPdsSnapshotRepositoryTests
{
    [Test]
    public async Task CompleteSnapshot_ReconcilesCanonicalStateWithoutLocalAggregatesOrCursorMutation()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using var context = new ExploreDbContext(CreateOptions(connection));
        context.EnableTenantFilterBypass("Provider-neutral ATProto snapshot repository test.");
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
        await SeedTenantAsync(context, tenantId);
        context.AtprotoRecordTenantPresentations.AddRange(
            Presentation(tenantId, missing, 100, now),
            Presentation(tenantId, rejected, 100, now));
        await context.SaveChangesAsync();
        AtprotoRecord accepted = Record(did, "3maccepted222", 0, now);
        var snapshot = new AtprotoPdsSnapshot(
            did,
            [
                new("community.lexicon.calendar.event", accepted.RecordKey),
                new("community.lexicon.calendar.event", rejected.RecordKey)
            ],
            [new(accepted, Projection(accepted, 0, now))]);
        var request = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [did],
            [snapshot],
            [tenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));

        bool applied = await repository.TryReconcileAsync(request, CancellationToken.None);
        bool replayed = await repository.TryReconcileAsync(request, CancellationToken.None);

        context.ChangeTracker.Clear();
        AtprotoRecord[] records = await context.AtprotoRecords.AsNoTracking()
            .OrderBy(value => value.RecordKey)
            .ToArrayAsync();
        Dictionary<string, AtprotoRecord> byKey = records.ToDictionary(value => value.RecordKey);
        Dictionary<Guid, bool> visibility = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .ToDictionaryAsync(value => value.AtprotoRecordId, value => value.IsVisible);
        await Assert.That(applied).IsTrue();
        await Assert.That(replayed).IsTrue();
        await Assert.That(records).Count().IsEqualTo(3);
        await Assert.That(byKey[accepted.RecordKey].SourceVersion).IsEqualTo(200);
        await Assert.That(byKey[accepted.RecordKey].SourceCursor).IsNull();
        await Assert.That(byKey[missing.RecordKey].TombstonedAt).IsNotNull();
        await Assert.That(byKey[rejected.RecordKey].TombstonedAt).IsNull();
        await Assert.That(visibility[accepted.Id]).IsTrue();
        await Assert.That(visibility[missing.Id]).IsFalse();
        await Assert.That(visibility[rejected.Id]).IsFalse();
        await Assert.That(await context.AtprotoEventProjections.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.AtprotoJetstreamConsumerStates.Select(value => value.Cursor).SingleAsync())
            .IsEqualTo(0);
        await Assert.That(await context.Events.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EventRegistrations.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task RejectedPresentRecord_AtEqualSnapshotVersion_PreservesLiveCanonicalProjectionAndPresentation()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using var context = new ExploreDbContext(CreateOptions(connection));
        context.EnableTenantFilterBypass("Provider-neutral ATProto snapshot repository test.");
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        const long version = 200;
        AtprotoRecord live = Record(did, "equal-version-live", version, now);
        Guid tenantId = Guid.CreateVersion7();
        await SeedTenantAsync(context, tenantId);
        context.AtprotoRecords.Add(live);
        context.AtprotoEventProjections.Add(Projection(live, version, now));
        context.AtprotoRecordTenantPresentations.Add(Presentation(tenantId, live, version, now));
        await context.SaveChangesAsync();
        var request = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [new("community.lexicon.calendar.event", live.RecordKey)],
                [])],
            [tenantId],
            SnapshotVersion: version,
            ObservedAt: now.AddSeconds(1));

        bool applied = await repository.TryReconcileAsync(request, CancellationToken.None);

        context.ChangeTracker.Clear();
        AtprotoRecord persisted = await context.AtprotoRecords.AsNoTracking().SingleAsync();
        AtprotoRecordTenantPresentation presentation = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(applied).IsTrue();
        await Assert.That(persisted.SourceVersion).IsEqualTo(version);
        await Assert.That(persisted.SourceCursor).IsEqualTo(version);
        await Assert.That(persisted.TombstonedAt).IsNull();
        await Assert.That(await context.AtprotoEventProjections.CountAsync()).IsEqualTo(1);
        await Assert.That(presentation.IsVisible).IsTrue();
    }

    [Test]
    public async Task MissingEvent_HidesOnlyOlderDependentRsvpPresentations()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using var context = new ExploreDbContext(CreateOptions(connection));
        context.EnableTenantFilterBypass("Provider-neutral ATProto snapshot repository test.");
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        const string dependentDid = "did:plc:rsvp-owner";
        const string rsvpCollection = "community.lexicon.calendar.rsvp";
        const long snapshotVersion = 200;
        AtprotoRecord missingEvent = Record(did, "missing-parent", 100, now);
        AtprotoRecord olderRsvp = Record(dependentDid, "older-rsvp", 100, now, rsvpCollection);
        AtprotoRecord equalRsvp = Record(dependentDid, "equal-rsvp", snapshotVersion, now, rsvpCollection);
        AtprotoRecord newerRsvp = Record(dependentDid, "newer-rsvp", 300, now, rsvpCollection);
        AtprotoRecord movedRsvp = Record(did, "3mmovedrsvp22", 100, now, rsvpCollection);
        foreach (AtprotoRecord rsvp in new[] { olderRsvp, equalRsvp, newerRsvp })
        {
            rsvp.SubjectUri = missingEvent.Uri;
            rsvp.SubjectCid = missingEvent.Cid;
        }
        movedRsvp.SubjectUri = missingEvent.Uri;
        movedRsvp.SubjectCid = missingEvent.Cid;

        Guid tenantId = Guid.CreateVersion7();
        await SeedTenantAsync(context, tenantId);
        context.AtprotoRecords.AddRange(missingEvent, olderRsvp, equalRsvp, newerRsvp, movedRsvp);
        context.AtprotoEventProjections.Add(Projection(missingEvent, 100, now));
        context.AtprotoRecordTenantPresentations.AddRange(
            Presentation(tenantId, missingEvent, 100, now),
            Presentation(tenantId, olderRsvp, 100, now),
            Presentation(tenantId, equalRsvp, snapshotVersion, now),
            Presentation(tenantId, newerRsvp, 300, now),
            Presentation(tenantId, movedRsvp, 100, now));
        await context.SaveChangesAsync();
        AtprotoRecord survivingEvent = Record(did, "3msurviveev22", 0, now);
        AtprotoRecord movedSnapshot = Record(did, movedRsvp.RecordKey, 0, now, rsvpCollection);
        movedSnapshot.SubjectUri = survivingEvent.Uri;
        movedSnapshot.SubjectCid = survivingEvent.Cid;
        var request = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [
                    new(survivingEvent.Collection, survivingEvent.RecordKey),
                    new(movedSnapshot.Collection, movedSnapshot.RecordKey)
                ],
                [
                    new(survivingEvent, Projection(survivingEvent, 0, now)),
                    new(movedSnapshot, null)
                ])],
            [tenantId],
            SnapshotVersion: snapshotVersion,
            ObservedAt: now.AddSeconds(1));

        bool applied = await repository.TryReconcileAsync(request, CancellationToken.None);

        context.ChangeTracker.Clear();
        Dictionary<string, AtprotoRecord> records = await context.AtprotoRecords.AsNoTracking()
            .ToDictionaryAsync(value => value.RecordKey);
        Dictionary<Guid, AtprotoRecordTenantPresentation> presentations = await context
            .AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(value => value.AtprotoRecordId);
        await Assert.That(applied).IsTrue();
        await Assert.That(records[olderRsvp.RecordKey].SourceVersion).IsEqualTo(100);
        await Assert.That(presentations[olderRsvp.Id].IsVisible).IsFalse();
        await Assert.That(presentations[olderRsvp.Id].SourceVersion).IsEqualTo(snapshotVersion);
        await Assert.That(records[equalRsvp.RecordKey].SourceVersion).IsEqualTo(snapshotVersion);
        await Assert.That(presentations[equalRsvp.Id].IsVisible).IsTrue();
        await Assert.That(presentations[equalRsvp.Id].SourceVersion).IsEqualTo(snapshotVersion);
        await Assert.That(records[newerRsvp.RecordKey].SourceVersion).IsEqualTo(300);
        await Assert.That(presentations[newerRsvp.Id].IsVisible).IsTrue();
        await Assert.That(presentations[newerRsvp.Id].SourceVersion).IsEqualTo(300);
        await Assert.That(records[movedRsvp.RecordKey].SourceVersion).IsEqualTo(snapshotVersion);
        await Assert.That(records[movedRsvp.RecordKey].SubjectUri).IsEqualTo(survivingEvent.Uri);
        await Assert.That(presentations[movedRsvp.Id].IsVisible).IsTrue();
        await Assert.That(presentations[movedRsvp.Id].SourceVersion).IsEqualTo(snapshotVersion);
    }

    [Test]
    public async Task TransientFailureAfterSaveChanges_RetryReloadsRolledBackSnapshotState()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        DateTime now = DateTime.UtcNow;
        Guid tenantId = Guid.CreateVersion7();
        Guid stateId = Guid.CreateVersion7();
        Guid leaseToken = Guid.CreateVersion7();
        const string service = "https://jetstream.example";
        const string did = "did:plc:snapshot-owner";
        AtprotoRecord canonical = Record(did, "3mretryrec222", 100, now);
        var rollback = new RollbackSnapshotStateInterceptor();
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create(CreateOptions(connection))
            .ReplaceService<IExecutionStrategyFactory, RetryOnceExecutionStrategyFactory>()
            .AddInterceptors(rollback)
            .Options;
        await using (var seedContext = new ExploreDbContext(options))
        {
            seedContext.EnableTenantFilterBypass("Provider-neutral ATProto snapshot retry seed.");
            await SeedTenantAsync(seedContext, tenantId);
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

        rollback.Arm();
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
            retryContext.EnableTenantFilterBypass("Provider-neutral ATProto snapshot retry test.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            bool applied = await new AtprotoJetstreamRepository(retryContext)
                .TryReconcileAsync(request, timeout.Token);
            await Assert.That(applied).IsTrue();
        }

        await using var verifyContext = new ExploreDbContext(options);
        verifyContext.EnableTenantFilterBypass("Provider-neutral ATProto snapshot retry verification.");
        AtprotoRecord persisted = await verifyContext.AtprotoRecords.AsNoTracking().SingleAsync();
        AtprotoEventProjection projection = await verifyContext.AtprotoEventProjections.AsNoTracking().SingleAsync();
        AtprotoRecordTenantPresentation presentation = await verifyContext.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(rollback.FailuresInjected).IsEqualTo(1);
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
        await using SqliteConnection connection = await OpenConnectionAsync();
        DateTime now = DateTime.UtcNow;
        const string service = "https://jetstream.example/cancelled-claim";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancelAfterSave = new CancelAfterSaveChangesInterceptor(cancellation);
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create(CreateOptions(connection))
            .AddInterceptors(cancelAfterSave)
            .Options;

        await using (var cancelledContext = new ExploreDbContext(options))
        {
            cancelledContext.EnableTenantFilterBypass("Provider-neutral ATProto cancelled claim test.");
            bool cancelled = false;
            try
            {
                await new AtprotoJetstreamRepository(cancelledContext).TryClaimAsync(
                    service,
                    "snapshot-worker",
                    now,
                    TimeSpan.FromMinutes(5),
                    cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                cancelled = true;
            }

            await Assert.That(cancelled).IsTrue();
            await Assert.That(cancelAfterSave.CancellationsInjected).IsEqualTo(1);
            await Assert.That(cancelledContext.ChangeTracker.Entries()).IsEmpty();
        }

        await using var freshContext = new ExploreDbContext(CreateOptions(connection));
        freshContext.EnableTenantFilterBypass("Provider-neutral ATProto fresh claim verification.");
        await Assert.That(await freshContext.AtprotoJetstreamConsumerStates.CountAsync()).IsEqualTo(0);
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        AtprotoJetstreamClaim claim = await new AtprotoJetstreamRepository(freshContext).TryClaimAsync(
            service,
            "snapshot-worker",
            now.AddSeconds(1),
            TimeSpan.FromMinutes(5),
            recovery.Token) ?? throw new InvalidOperationException("Fresh claim was not acquired.");
        AtprotoJetstreamConsumerState state = await freshContext.AtprotoJetstreamConsumerStates.SingleAsync();
        await Assert.That(claim.Cursor).IsEqualTo(0);
        await Assert.That(claim.LeaseFence).IsEqualTo(1);
        await Assert.That(state.Cursor).IsEqualTo(0);
        await Assert.That(state.LeaseFence).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationAfterSaveChanges_RollsBackApplyAndAllowsFreshApply()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        DateTime now = DateTime.UtcNow;
        const string service = "https://jetstream.example/cancelled-apply";
        const string did = "did:plc:cancelled-apply";
        AtprotoJetstreamClaim claim;
        await using (var seedContext = new ExploreDbContext(CreateOptions(connection)))
        {
            seedContext.EnableTenantFilterBypass("Provider-neutral ATProto cancelled apply seed.");
            claim = await new AtprotoJetstreamRepository(seedContext).TryClaimAsync(
                service,
                "snapshot-worker",
                now,
                TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancelAfterSave = new CancelAfterSaveChangesInterceptor(cancellation);
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create(CreateOptions(connection))
            .AddInterceptors(cancelAfterSave)
            .Options;
        var request = new AtprotoJetstreamApplyRequest(
            claim,
            0,
            1,
            Record(did, "3msnapshota22", 1, now.AddSeconds(1)),
            [],
            null,
            now.AddSeconds(1));

        await using (var cancelledContext = new ExploreDbContext(options))
        {
            cancelledContext.EnableTenantFilterBypass("Provider-neutral ATProto cancelled apply test.");
            bool cancelled = false;
            try
            {
                await new AtprotoJetstreamRepository(cancelledContext)
                    .TryApplyAndAdvanceAsync(request, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                cancelled = true;
            }

            await Assert.That(cancelled).IsTrue();
            await Assert.That(cancelAfterSave.CancellationsInjected).IsEqualTo(1);
            await Assert.That(cancelledContext.ChangeTracker.Entries()).IsEmpty();
        }

        await using (var freshContext = new ExploreDbContext(CreateOptions(connection)))
        {
            freshContext.EnableTenantFilterBypass("Provider-neutral ATProto fresh apply verification.");
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            AtprotoJetstreamConsumerState rolledBack = await freshContext.AtprotoJetstreamConsumerStates.SingleAsync();
            await Assert.That(rolledBack.Cursor).IsEqualTo(0);
            await Assert.That(await freshContext.AtprotoRecords.CountAsync()).IsEqualTo(0);
            bool applied = await new AtprotoJetstreamRepository(freshContext).TryApplyAndAdvanceAsync(
                request with { Record = Record(did, "3msnapshota22", 1, now.AddSeconds(1)) },
                recovery.Token);
            freshContext.ChangeTracker.Clear();
            AtprotoJetstreamConsumerState recovered = await freshContext.AtprotoJetstreamConsumerStates.SingleAsync();
            await Assert.That(applied).IsTrue();
            await Assert.That(recovered.Cursor).IsEqualTo(1);
            await Assert.That(await freshContext.AtprotoRecords.CountAsync()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task CancellationAfterSaveChanges_RollsBackReconcileAndAllowsFreshReconcile()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        DateTime now = DateTime.UtcNow;
        const string service = "https://jetstream.example/cancelled-reconcile";
        const string did = "did:plc:cancelled-reconcile";
        Guid tenantId = Guid.CreateVersion7();
        AtprotoJetstreamClaim claim;
        AtprotoRecord canonical = Record(did, "3mretryrec222", 100, now);
        await using (var seedContext = new ExploreDbContext(CreateOptions(connection)))
        {
            seedContext.EnableTenantFilterBypass("Provider-neutral ATProto cancelled reconcile seed.");
            await SeedTenantAsync(seedContext, tenantId);
            claim = await new AtprotoJetstreamRepository(seedContext).TryClaimAsync(
                service,
                "snapshot-worker",
                now,
                TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
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
            claim,
            [did],
            [new AtprotoPdsSnapshot(
                did,
                [new(recovered.Collection, recovered.RecordKey)],
                [new(recovered, recoveredProjection)])],
            [tenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancelAfterSave = new CancelAfterSaveChangesInterceptor(cancellation);
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create(CreateOptions(connection))
            .AddInterceptors(cancelAfterSave)
            .Options;

        await using (var cancelledContext = new ExploreDbContext(options))
        {
            cancelledContext.EnableTenantFilterBypass("Provider-neutral ATProto cancelled reconcile test.");
            bool cancelled = false;
            try
            {
                await new AtprotoJetstreamRepository(cancelledContext)
                    .TryReconcileAsync(request, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                cancelled = true;
            }

            await Assert.That(cancelled).IsTrue();
            await Assert.That(cancelAfterSave.CancellationsInjected).IsEqualTo(1);
            await Assert.That(cancelledContext.ChangeTracker.Entries()).IsEmpty();
        }

        await using (var freshContext = new ExploreDbContext(CreateOptions(connection)))
        {
            freshContext.EnableTenantFilterBypass("Provider-neutral ATProto fresh reconcile verification.");
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
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using var context = new ExploreDbContext(CreateOptions(connection));
        context.EnableTenantFilterBypass("Provider-neutral ATProto snapshot present-key test.");
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        Guid tenantId = Guid.CreateVersion7();
        await SeedTenantAsync(context, tenantId);
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

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(CreateOptions(connection));
        await context.Database.EnsureCreatedAsync();
        return connection;
    }

    private static DbContextOptions<ExploreDbContext> CreateOptions(SqliteConnection connection) =>
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance)
            .Options;

    private static async Task SeedTenantAsync(ExploreDbContext context, Guid tenantId)
    {
        var status = new TenantStatus
        {
            Id = (int)TenantStatusEnum.Active,
            MasterCode = "ACTIVE",
            FullName = "Active",
            IsActiveState = true
        };
        context.Add(new Tenant
        {
            Id = tenantId,
            FullName = "ATProto snapshot tenant",
            Slug = $"atproto-snapshot-{tenantId:N}",
            TenantStatusId = status.Id,
            TenantStatus = status,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

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

    private sealed class RollbackSnapshotStateInterceptor : SaveChangesInterceptor
    {
        private int _shouldFail;

        public int FailuresInjected { get; private set; }

        public void Arm()
        {
            Volatile.Write(ref _shouldFail, 1);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _shouldFail, 0) == 0)
            {
                return result;
            }

            await Assert.That(eventData.Context!.Database.CurrentTransaction).IsNotNull();
            FailuresInjected++;
            throw new TestTransientException();
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
