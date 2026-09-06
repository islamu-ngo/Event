// ABOUTME: PostgreSQL integration tests for fenced AT Protocol outbox settlement and atomic Jetstream cursor application.
// ABOUTME: Covers stale-worker rollback, idempotent replay, UUID allocation, and tenant presentation isolation.

using System.Data.Common;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Federation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoFederationPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task PdsOutboxSchema_ModelMigrationAndLivePostgresStayInParity()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var sourceVersionIndex = context.Model.FindEntityType(typeof(PdsSyncOutbox))!
            .GetIndexes()
            .Single(index => index.IsUnique && index.Properties
                .Select(property => property.Name)
                .SequenceEqual([
                    nameof(PdsSyncOutbox.TenantId),
                    nameof(PdsSyncOutbox.SourceEntityType),
                    nameof(PdsSyncOutbox.SourceEntityId),
                    nameof(PdsSyncOutbox.SourceVersion),
                    nameof(PdsSyncOutbox.Operation),
                    nameof(PdsSyncOutbox.PayloadHash)
                ]));
        string[] modelIndexProperties = sourceVersionIndex
            .Properties
            .Select(property => property.Name)
            .ToArray();
        string modelIndexName = sourceVersionIndex.GetDatabaseName();
        string? modelFilter = sourceVersionIndex.GetFilter();
        await context.Database.OpenConnectionAsync();
        await using DbCommand columnCommand = context.Database.GetDbConnection().CreateCommand();
        columnCommand.CommandText = """
            SELECT character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'pds_sync_outbox'
              AND column_name = 'depends_on_cid'
            """;
        object? columnLength = await columnCommand.ExecuteScalarAsync();
        await using DbCommand indexCommand = context.Database.GetDbConnection().CreateCommand();
        indexCommand.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = current_schema()
              AND tablename = 'pds_sync_outbox'
              AND indexname = @index_name
            """;
        DbParameter indexNameParameter = indexCommand.CreateParameter();
        indexNameParameter.ParameterName = "index_name";
        indexNameParameter.Value = modelIndexName;
        indexCommand.Parameters.Add(indexNameParameter);
        string indexDefinition = (string)(await indexCommand.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("PDS source-attempt index was not created."));

        await Assert.That(await context.Database.GetPendingMigrationsAsync()).IsEmpty();
        await Assert.That(modelIndexProperties).IsEquivalentTo([
            nameof(PdsSyncOutbox.TenantId),
            nameof(PdsSyncOutbox.SourceEntityType),
            nameof(PdsSyncOutbox.SourceEntityId),
            nameof(PdsSyncOutbox.SourceVersion),
            nameof(PdsSyncOutbox.Operation),
            nameof(PdsSyncOutbox.PayloadHash)
        ]);
        await Assert.That(modelFilter).IsEqualTo("status IN (1, 2) AND superseded_at IS NULL");
        await Assert.That(Convert.ToInt32(columnLength)).IsEqualTo(255);
        await Assert.That(indexDefinition).Contains("payload_hash");
        await Assert.That(indexDefinition).Contains("WHERE");
        await Assert.That(indexDefinition).Contains("status");
        await Assert.That(indexDefinition).Contains("superseded_at IS NULL");
    }

    [Test]
    public async Task JetstreamApply_AllocatesIdentityAndAdvancesDuplicateReplayWithoutDuplication()
    {
        await fixture.ResetAsync();
        var scope = await SeedScopeAsync("jetstream-replay");
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        var now = CurrentUtc();
        var claim = await repository.TryClaimAsync(
            "wss://jetstream.example/subscribe",
            "worker-a",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        var record = IncomingRecord(sourceVersion: 1, now);

        var applied = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            ExpectedCursor: 0,
            NextCursor: 1,
            record,
            [new AtprotoRecordTenantPresentation { TenantId = scope.TenantId, IsVisible = true }],
            Quarantine: null,
            now));
        var replayed = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            ExpectedCursor: 1,
            NextCursor: 2,
            IncomingRecord(sourceVersion: 1, now.AddSeconds(1)),
            [],
            Quarantine: null,
            now.AddSeconds(1)));

        context.ChangeTracker.Clear();
        var persistedRecord = await context.AtprotoRecords.AsNoTracking().SingleAsync();
        var presentation = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        var state = await context.AtprotoJetstreamConsumerStates.AsNoTracking().SingleAsync();
        await Assert.That(applied).IsTrue();
        await Assert.That(replayed).IsTrue();
        await Assert.That(persistedRecord.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(presentation.AtprotoRecordId).IsEqualTo(persistedRecord.Id);
        await Assert.That(state.Cursor).IsEqualTo(2);
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task IdentityRepositoryUsesTypedDidAndOrdinalScalarLookup()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("typed-did-lookup");
        AtprotoDid did = AtprotoDid.Parse("did:plc:ExactIdentity");
        Actor actor = CreateActor(scope.UserId, "Typed DID owner", Utc(10));
        var identity = new AtprotoIdentity(did)
        {
            Id = Guid.CreateVersion7(),
            ActorId = actor.Id,
            Actor = actor,
            PdsHost = "https://pds.example.test",
            IsActive = true,
            LastResolvedAt = Utc(10),
            LastSeenAt = Utc(10),
            CreatedAt = Utc(10),
        };
        await using ExploreDbContext context = fixture.CreateDbContext();
        context.AtprotoIdentities.Add(identity);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new AtprotoIdentityRepository(context);

        AtprotoIdentity? exact = await repository.GetByDid(did);
        AtprotoIdentity? differentCase = await repository.GetByDid(
            AtprotoDid.Parse("did:plc:exactidentity"));

        await Assert.That(exact?.Id).IsEqualTo(identity.Id);
        await Assert.That(differentCase).IsNull();
    }

    [Test]
    public async Task JetstreamApply_InvalidOutOfRangeCursorStoresQuarantineWithoutPoisoningCheckpoint()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        var now = CurrentUtc();
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "worker-a",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        var quarantine = new AtprotoJetstreamQuarantine
        {
            Id = Guid.CreateVersion7(),
            ReasonCode = "invalid_cursor",
            EnvelopeHash = new string('a', 64),
            EventAt = now,
            QuarantinedAt = now
        };

        bool quarantined = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            ExpectedCursor: 0,
            NextCursor: long.MaxValue,
            Record: null,
            Presentations: [],
            quarantine,
            now,
            AdvanceCursor: false));
        bool applied = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            ExpectedCursor: 0,
            NextCursor: 100,
            IncomingRecord(sourceVersion: 100, now.AddSeconds(1)),
            Presentations: [],
            Quarantine: null,
            now.AddSeconds(1)));

        context.ChangeTracker.Clear();
        long cursor = await context.AtprotoJetstreamConsumerStates.AsNoTracking().Select(value => value.Cursor).SingleAsync();
        long quarantinedCursor = await context.AtprotoJetstreamQuarantines.AsNoTracking().Select(value => value.Cursor).SingleAsync();
        await Assert.That(quarantined).IsTrue();
        await Assert.That(applied).IsTrue();
        await Assert.That(cursor).IsEqualTo(100);
        await Assert.That(quarantinedCursor).IsEqualTo(long.MaxValue);
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task AtprotoRecordRepository_ExposesOnlyCurrentTenantPresentations()
    {
        await fixture.ResetAsync();
        var tenantA = await SeedScopeAsync("presentation-a");
        var tenantB = await SeedScopeAsync("presentation-b");
        Guid recordId;
        await using (var seedContext = fixture.CreateDbContext())
        {
            var record = IncomingRecord(sourceVersion: 1, Utc(10));
            record.Id = Guid.CreateVersion7();
            recordId = record.Id;
            seedContext.AtprotoRecords.Add(record);
            seedContext.AtprotoRecordTenantPresentations.Add(new AtprotoRecordTenantPresentation
            {
                TenantId = tenantA.TenantId,
                AtprotoRecordId = record.Id,
                IsVisible = true,
                SourceVersion = 1,
                EvaluatedAt = Utc(10)
            });
            await seedContext.SaveChangesAsync();
        }

        await using var contextA = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenantA.TenantId));
        await using var contextB = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenantB.TenantId));
        var visible = await new AtprotoRecordRepository(contextA).GetById(recordId);
        var hidden = await new AtprotoRecordRepository(contextB).GetById(recordId);
        await Assert.That(visible).IsNotNull();
        await Assert.That(hidden).IsNull();
    }

    [Test]
    public async Task AtprotoEventProjectionRepositoryEnforcesTenantPresentationIsolation()
    {
        await fixture.ResetAsync();
        FederationScope tenantA = await SeedScopeAsync("projection-a");
        FederationScope tenantB = await SeedScopeAsync("projection-b");
        Guid recordId = Guid.CreateVersion7();
        await using (var seedContext = fixture.CreateDbContext())
        {
            AtprotoRecord record = IncomingRecord(1, Utc(10));
            record.Id = recordId;
            Actor actor = CreateActor(tenantA.UserId, "Projection owner", Utc(10));
            seedContext.AtprotoRecords.Add(record);
            seedContext.AtprotoEventProjections.Add(Projection(recordId, 1, "Visible event"));
            seedContext.AtprotoRecordTenantPresentations.Add(new AtprotoRecordTenantPresentation
            {
                TenantId = tenantA.TenantId,
                AtprotoRecordId = recordId,
                IsVisible = true,
                SourceVersion = 1,
                EvaluatedAt = Utc(10)
            });
            seedContext.AtprotoIdentities.Add(new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse(record.Did))
            {
                Id = Guid.CreateVersion7(),

                ActorId = actor.Id,
                Actor = actor,
                PdsHost = "https://pds.example.test",
                IsActive = true,
                LastResolvedAt = Utc(10),
                LastSeenAt = Utc(10),
                CreatedAt = Utc(10)
            });
            seedContext.Events.Add(new Explore.Domain.Event(EventStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                Title = "Visible event",
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated,
                PublicCode = "ATPROTO",
                ActorId = actor.Id,
                Actor = actor,
                TenantId = tenantA.TenantId,
                Tenant = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormatId = (int)EventFormatEnum.Digital,
                EventFormat = null!,
                AtprotoRecordId = recordId,
                AtprotoRecord = record,
                CreatedAt = Utc(10),
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            await seedContext.SaveChangesAsync();
        }

        var query = new AtprotoEventProjectionQuery(
            20,
            null,
            null,
            null,
            null,
            AtprotoEventTemporalFilter.All,
            AtprotoEventDiscoverySort.Date,
            false,
            new DateTimeOffset(Utc(10)));
        await using ExploreDbContext contextA = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenantA.TenantId));
        await using ExploreDbContext contextB = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenantB.TenantId));

        (IReadOnlyList<AtprotoEventProjection> visible, int visibleCount) =
            await new AtprotoEventProjectionRepository(contextA).GetPublicWindowAsync(query, CancellationToken.None);
        (IReadOnlyList<AtprotoEventProjection> hidden, int hiddenCount) =
            await new AtprotoEventProjectionRepository(contextB).GetPublicWindowAsync(query, CancellationToken.None);

        await Assert.That(visible).HasSingleItem();
        await Assert.That(visibleCount).IsEqualTo(1);
        await Assert.That(hidden).IsEmpty();
        await Assert.That(hiddenCount).IsEqualTo(0);
    }

    [Test]
    public async Task JetstreamApply_EventTombstoneSuppressesDependentRsvpPresentationAtomically()
    {
        await fixture.ResetAsync();
        var scope = await SeedScopeAsync("jetstream-tombstone");
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        var now = CurrentUtc();
        var claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "worker-a",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        AtprotoRecord calendarEvent = IncomingRecord(sourceVersion: 1, now);
        var eventPresentation = new AtprotoRecordTenantPresentation { TenantId = scope.TenantId, IsVisible = true };
        await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            0,
            1,
            calendarEvent,
            [eventPresentation],
            null,
            now,
            EventProjection: Projection(calendarEvent.Id, 1, "Materialized event")));
        await Assert.That(await context.AtprotoEventProjections.CountAsync()).IsEqualTo(1);
        var rsvp = new AtprotoRecord
        {
            Did = "did:plc:rsvp-owner",
            Collection = "community.lexicon.calendar.rsvp",
            RecordKey = "3m7rsvp",
            Cid = "bafyreirsvp",
            Uri = "at://did:plc:rsvp-owner/community.lexicon.calendar.rsvp/3m7rsvp",
            SourceVersion = 2,
            SubjectUri = calendarEvent.Uri,
            SubjectCid = calendarEvent.Cid,
            RecordJson = "{\"status\":\"community.lexicon.calendar.rsvp#going\"}",
            RecordHash = new string('c', 64),
            IndexedAt = now.AddSeconds(1),
            UpdatedAt = now.AddSeconds(1)
        };
        await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            1,
            2,
            rsvp,
            [new AtprotoRecordTenantPresentation { TenantId = scope.TenantId, IsVisible = true }],
            null,
            now.AddSeconds(1)));
        AtprotoRecord tombstone = IncomingRecord(sourceVersion: 3, now.AddSeconds(2));
        tombstone.RecordJson = null;
        tombstone.RecordHash = null;
        tombstone.Cid = null;
        tombstone.TombstonedAt = now.AddSeconds(2);

        bool applied = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim, 2, 3, tombstone, [], null, now.AddSeconds(2)));

        context.ChangeTracker.Clear();
        AtprotoRecord persistedEvent = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.Collection == "community.lexicon.calendar.event");
        bool[] visibility = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(value => value.AtprotoRecordId)
            .Select(value => value.IsVisible)
            .ToArrayAsync();
        long cursor = await context.AtprotoJetstreamConsumerStates.AsNoTracking().Select(value => value.Cursor).SingleAsync();
        await Assert.That(applied).IsTrue();
        await Assert.That(persistedEvent.TombstonedAt).IsEqualTo(now.AddSeconds(2));
        await Assert.That(visibility).IsEquivalentTo([false, false]);
        await Assert.That(await context.AtprotoEventProjections.CountAsync()).IsEqualTo(0);
        await Assert.That(cursor).IsEqualTo(3);
    }

    [Test]
    public async Task JetstreamApply_RepeatedLocalEchoRemainsReconciled()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var now = CurrentUtc();
        AtprotoRecord local = IncomingRecord(sourceVersion: 0, now);
        local.Id = Guid.CreateVersion7();
        local.Direction = AtprotoRecordDirection.Outbound;
        local.Provenance = AtprotoRecordProvenance.LocalLifecycle;
        context.AtprotoRecords.Add(local);
        await context.SaveChangesAsync();
        var repository = new AtprotoJetstreamRepository(context);
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "worker-a",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");

        await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim, 0, 1, IncomingRecord(sourceVersion: 1, now.AddSeconds(1)), [], null, now.AddSeconds(1)));
        await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim, 1, 2, IncomingRecord(sourceVersion: 2, now.AddSeconds(2)), [], null, now.AddSeconds(2)));

        context.ChangeTracker.Clear();
        AtprotoRecord persisted = await context.AtprotoRecords.AsNoTracking().SingleAsync();
        await Assert.That(persisted.Direction).IsEqualTo(AtprotoRecordDirection.Reconciled);
        await Assert.That(persisted.Provenance).IsEqualTo(AtprotoRecordProvenance.JetstreamEcho);
        await Assert.That(persisted.SourceVersion).IsEqualTo(2);
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task PdsSnapshotReconcile_AllDidsCommitAtomicallyWithoutAdvancingCursor()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("snapshot-all-dids");
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            0,
            100,
            IncomingRecord(100, now),
            [],
            null,
            now));
        const string didA = "did:plc:snapshot-a";
        const string didB = "did:plc:snapshot-b";
        AtprotoRecord eventA = SnapshotRecord(didA, "3msnapshota22", now);
        AtprotoRecord eventB = SnapshotRecord(didB, "3msnapshotb22", now);
        var request = new AtprotoPdsSnapshotApplyRequest(
            claim,
            [didA, didB],
            [
                Snapshot(didA, eventA, "Event A", now),
                Snapshot(didB, eventB, "Event B", now)
            ],
            [scope.TenantId],
            SnapshotVersion: 200,
            ObservedAt: now.AddSeconds(1));

        bool applied = await repository.TryReconcileAsync(request, CancellationToken.None);
        bool replayed = await repository.TryReconcileAsync(request, CancellationToken.None);

        context.ChangeTracker.Clear();
        AtprotoRecord[] recovered = await context.AtprotoRecords.AsNoTracking()
            .Where(value => value.Did == didA || value.Did == didB)
            .OrderBy(value => value.Did)
            .ToArrayAsync();
        await Assert.That(applied).IsTrue();
        await Assert.That(replayed).IsTrue();
        await Assert.That(recovered).Count().IsEqualTo(2);
        await Assert.That(recovered.All(value => value.SourceVersion == 200 && value.SourceCursor is null)).IsTrue();
        await Assert.That(await context.AtprotoEventProjections.CountAsync()).IsEqualTo(2);
        await Assert.That(await context.AtprotoRecordTenantPresentations.IgnoreQueryFilters().CountAsync()).IsEqualTo(2);
        await Assert.That(await context.Events.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EventRegistrations.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AtprotoJetstreamConsumerStates.Select(value => value.Cursor).SingleAsync())
            .IsEqualTo(100);
    }

    [Test]
    public async Task PdsSnapshotReconcile_NewerJetstreamWinsAndCompleteAbsenceTombstonesSafely()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("snapshot-reconcile");
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string did = "did:plc:snapshot-owner";
        const string dependentDid = "did:plc:rsvp-owner";
        AtprotoRecord missingEvent = SnapshotRecord(did, "missing-event", now, sourceVersion: 100);
        AtprotoRecord rejectedEvent = SnapshotRecord(did, "rejected-event", now, sourceVersion: 100);
        AtprotoRecord equalVersionEvent = SnapshotRecord(did, "equal-version-event", now, sourceVersion: 200);
        AtprotoRecord newerEvent = SnapshotRecord(did, "3mnewereven22", now, sourceVersion: 300);
        AtprotoRecord dependentRsvp = SnapshotRecord(
            dependentDid,
            "dependent-rsvp",
            now,
            sourceVersion: 100,
            collection: "community.lexicon.calendar.rsvp");
        dependentRsvp.SubjectUri = missingEvent.Uri;
        dependentRsvp.SubjectCid = missingEvent.Cid;
        AtprotoRecord equalDependentRsvp = SnapshotRecord(
            dependentDid,
            "equal-dependent-rsvp",
            now,
            sourceVersion: 200,
            collection: "community.lexicon.calendar.rsvp");
        equalDependentRsvp.SubjectUri = missingEvent.Uri;
        equalDependentRsvp.SubjectCid = missingEvent.Cid;
        AtprotoRecord newerDependentRsvp = SnapshotRecord(
            dependentDid,
            "newer-dependent-rsvp",
            now,
            sourceVersion: 300,
            collection: "community.lexicon.calendar.rsvp");
        newerDependentRsvp.SubjectUri = missingEvent.Uri;
        newerDependentRsvp.SubjectCid = missingEvent.Cid;
        AtprotoRecord movedDependentRsvp = SnapshotRecord(
            did,
            "3mmovedrsvp22",
            now,
            sourceVersion: 100,
            collection: "community.lexicon.calendar.rsvp");
        movedDependentRsvp.SubjectUri = missingEvent.Uri;
        movedDependentRsvp.SubjectCid = missingEvent.Cid;
        AtprotoRecord localOnly = SnapshotRecord(did, "local-only", now, sourceVersion: 0);
        localOnly.Direction = AtprotoRecordDirection.Outbound;
        localOnly.Provenance = AtprotoRecordProvenance.LocalLifecycle;
        AtprotoRecord missingEcho = SnapshotRecord(did, "missing-echo", now, sourceVersion: 100);
        missingEcho.Direction = AtprotoRecordDirection.Reconciled;
        missingEcho.Provenance = AtprotoRecordProvenance.JetstreamEcho;
        context.AtprotoRecords.AddRange(
            missingEvent,
            rejectedEvent,
            equalVersionEvent,
            newerEvent,
            dependentRsvp,
            equalDependentRsvp,
            newerDependentRsvp,
            movedDependentRsvp,
            localOnly,
            missingEcho);
        context.AtprotoEventProjections.AddRange(
            Projection(missingEvent.Id, 100, "Missing event"),
            Projection(rejectedEvent.Id, 100, "Rejected event"),
            Projection(equalVersionEvent.Id, 200, "Equal-version event"),
            Projection(newerEvent.Id, 300, "Newer event"));
        foreach (AtprotoRecord record in new[]
                 {
                     missingEvent,
                     rejectedEvent,
                     equalVersionEvent,
                     newerEvent,
                     dependentRsvp,
                     equalDependentRsvp,
                     newerDependentRsvp,
                     movedDependentRsvp
                 })
        {
            context.AtprotoRecordTenantPresentations.Add(new()
            {
                TenantId = scope.TenantId,
                AtprotoRecordId = record.Id,
                IsVisible = true,
                SourceVersion = record.SourceVersion,
                EvaluatedAt = now
            });
        }
        await context.SaveChangesAsync();
        AtprotoRecord staleNewerSnapshot = SnapshotRecord(did, newerEvent.RecordKey, now, sourceVersion: 200);
        staleNewerSnapshot.RecordJson = "{\"name\":\"Stale snapshot\"}";
        AtprotoRecord movedDependentSnapshot = SnapshotRecord(
            did,
            movedDependentRsvp.RecordKey,
            now,
            sourceVersion: 200,
            collection: "community.lexicon.calendar.rsvp");
        movedDependentSnapshot.SubjectUri = newerEvent.Uri;
        movedDependentSnapshot.SubjectCid = newerEvent.Cid;
        var snapshot = new AtprotoPdsSnapshot(
            did,
            [
                new("community.lexicon.calendar.event", rejectedEvent.RecordKey),
                new("community.lexicon.calendar.event", equalVersionEvent.RecordKey),
                new("community.lexicon.calendar.event", newerEvent.RecordKey),
                new("community.lexicon.calendar.rsvp", movedDependentSnapshot.RecordKey)
            ],
            [
                new(staleNewerSnapshot, Projection(staleNewerSnapshot.Id, 200, "Stale snapshot")),
                new(movedDependentSnapshot, null)
            ]);

        bool applied = await repository.TryReconcileAsync(
            new(
                claim,
                [did],
                [snapshot],
                [scope.TenantId],
                SnapshotVersion: 200,
                ObservedAt: now.AddSeconds(1)),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        AtprotoRecord persistedMissing = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == missingEvent.RecordKey);
        AtprotoRecord persistedRejected = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == rejectedEvent.RecordKey);
        AtprotoRecord persistedEqualVersion = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == equalVersionEvent.RecordKey);
        AtprotoRecord persistedNewer = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == newerEvent.RecordKey);
        AtprotoRecord persistedDependent = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == dependentRsvp.RecordKey);
        AtprotoRecord persistedEqualDependent = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == equalDependentRsvp.RecordKey);
        AtprotoRecord persistedNewerDependent = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == newerDependentRsvp.RecordKey);
        AtprotoRecord persistedMovedDependent = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == movedDependentRsvp.RecordKey);
        AtprotoRecord persistedLocal = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == localOnly.RecordKey);
        AtprotoRecord persistedEcho = await context.AtprotoRecords.AsNoTracking()
            .SingleAsync(value => value.RecordKey == missingEcho.RecordKey);
        Dictionary<Guid, AtprotoRecordTenantPresentation> presentations = await context
            .AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToDictionaryAsync(value => value.AtprotoRecordId);
        await Assert.That(applied).IsTrue();
        await Assert.That(persistedMissing.TombstonedAt).IsNotNull();
        await Assert.That(persistedMissing.SourceVersion).IsEqualTo(200);
        await Assert.That(persistedMissing.SourceCursor).IsNull();
        await Assert.That(presentations[missingEvent.Id].IsVisible).IsFalse();
        await Assert.That(presentations[dependentRsvp.Id].IsVisible).IsFalse();
        await Assert.That(persistedDependent.SourceVersion).IsEqualTo(100);
        await Assert.That(presentations[dependentRsvp.Id].SourceVersion).IsEqualTo(200);
        await Assert.That(presentations[equalDependentRsvp.Id].IsVisible).IsTrue();
        await Assert.That(persistedEqualDependent.SourceVersion).IsEqualTo(200);
        await Assert.That(presentations[equalDependentRsvp.Id].SourceVersion).IsEqualTo(200);
        await Assert.That(presentations[newerDependentRsvp.Id].IsVisible).IsTrue();
        await Assert.That(persistedNewerDependent.SourceVersion).IsEqualTo(300);
        await Assert.That(presentations[newerDependentRsvp.Id].SourceVersion).IsEqualTo(300);
        await Assert.That(presentations[movedDependentRsvp.Id].IsVisible).IsTrue();
        await Assert.That(persistedMovedDependent.SourceVersion).IsEqualTo(200);
        await Assert.That(persistedMovedDependent.SubjectUri).IsEqualTo(newerEvent.Uri);
        await Assert.That(presentations[movedDependentRsvp.Id].SourceVersion).IsEqualTo(200);
        await Assert.That(persistedRejected.TombstonedAt).IsNull();
        await Assert.That(persistedRejected.SourceVersion).IsEqualTo(100);
        await Assert.That(presentations[rejectedEvent.Id].IsVisible).IsFalse();
        await Assert.That(persistedEqualVersion.SourceVersion).IsEqualTo(200);
        await Assert.That(persistedEqualVersion.SourceCursor).IsEqualTo(200);
        await Assert.That(presentations[equalVersionEvent.Id].IsVisible).IsTrue();
        await Assert.That(persistedNewer.SourceVersion).IsEqualTo(300);
        using JsonDocument persistedNewerJson = JsonDocument.Parse(persistedNewer.RecordJson!);
        using JsonDocument expectedNewerJson = JsonDocument.Parse(newerEvent.RecordJson!);
        await Assert.That(JsonElement.DeepEquals(persistedNewerJson.RootElement, expectedNewerJson.RootElement)).IsTrue();
        await Assert.That(presentations[newerEvent.Id].IsVisible).IsTrue();
        await Assert.That(persistedLocal.Direction).IsEqualTo(AtprotoRecordDirection.Outbound);
        await Assert.That(persistedLocal.Provenance).IsEqualTo(AtprotoRecordProvenance.LocalLifecycle);
        await Assert.That(persistedLocal.TombstonedAt).IsNull();
        await Assert.That(persistedEcho.Direction).IsEqualTo(AtprotoRecordDirection.Reconciled);
        await Assert.That(persistedEcho.Provenance).IsEqualTo(AtprotoRecordProvenance.JetstreamEcho);
        await Assert.That(persistedEcho.TombstonedAt).IsNotNull();
        await Assert.That(await context.AtprotoEventProjections.CountAsync()).IsEqualTo(2);
        await Assert.That(await context.AtprotoJetstreamConsumerStates.Select(value => value.Cursor).SingleAsync())
            .IsEqualTo(0);
    }

    [Test]
    public async Task PdsSnapshotReconcile_IncompleteRunOrExpiredFenceWritesNothing()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim = await repository.TryClaimAsync(
            "https://jetstream.example",
            "snapshot-worker",
            now,
            TimeSpan.FromSeconds(1)) ?? throw new InvalidOperationException("Claim was not acquired.");
        const string didA = "did:plc:incomplete-a";
        const string didB = "did:plc:incomplete-b";
        AtprotoPdsSnapshot snapshot = Snapshot(
            didA,
            SnapshotRecord(didA, "3mincomplete2", now),
            "Event A",
            now);

        bool incomplete = await repository.TryReconcileAsync(
            new(claim, [didA, didB], [snapshot], [], 100, now),
            CancellationToken.None);
        bool expired = await repository.TryReconcileAsync(
            new(claim, [didA], [snapshot], [], 100, now.AddSeconds(2)),
            CancellationToken.None);

        await Assert.That(incomplete).IsFalse();
        await Assert.That(expired).IsFalse();
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AtprotoEventProjections.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AtprotoRecordTenantPresentations.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PdsSnapshotReconcile_FenceExpiryDuringSaveRollsBackEverySnapshotWrite()
    {
        await fixture.ResetAsync();
        DateTime now = DateTime.UtcNow;
        AtprotoJetstreamClaim claim;
        await using (var claimContext = fixture.CreateDbContext())
        {
            claim = await new AtprotoJetstreamRepository(claimContext).TryClaimAsync(
                "https://jetstream.example",
                "snapshot-worker",
                now,
                TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        }

        var interceptor = new ReclaimOnSaveInterceptor(async () =>
        {
            await using ExploreDbContext expiryContext = fixture.CreateDbContext();
            await expiryContext.AtprotoJetstreamConsumerStates
                .Where(value => value.Id == claim.ConsumerStateId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.LeaseExpiresAt, now.AddSeconds(-1)));
        });
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(value => value.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("ATProto snapshot settlement fence race test.");
        const string did = "did:plc:expiry-during-save";
        AtprotoPdsSnapshot snapshot = Snapshot(
            did,
            SnapshotRecord(did, "3mfenceevent2", now),
            "Event A",
            now);

        bool reconciled = await new AtprotoJetstreamRepository(context).TryReconcileAsync(
            new(claim, [did], [snapshot], [], 100, now),
            CancellationToken.None);

        await using ExploreDbContext verifyContext = fixture.CreateDbContext();
        await Assert.That(reconciled).IsFalse();
        await Assert.That(await verifyContext.AtprotoRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.AtprotoEventProjections.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.AtprotoRecordTenantPresentations.IgnoreQueryFilters().CountAsync())
            .IsEqualTo(0);
    }

    [Test]
    public async Task PdsSettlement_ReclaimedFenceRollsBackCanonicalAndOwnershipWrites()
    {
        await fixture.ResetAsync();
        var scope = await SeedScopeAsync("pds-fence");
        var now = Utc(10);
        var outbox = CreateOutbox(scope, now);
        await using (var seedContext = fixture.CreateDbContext())
        {
            seedContext.PdsSyncOutbox.Add(outbox);
            await seedContext.SaveChangesAsync();
        }

        PdsSyncClaim staleClaim;
        await using (var claimContext = fixture.CreateDbContext())
        {
            staleClaim = (await new PdsSyncOutboxRepository(claimContext).ClaimDueAsync(
                1,
                "worker-a",
                now,
                TimeSpan.FromMinutes(1))).Single();
        }

        var interceptor = new ReclaimOnSaveInterceptor(async () =>
        {
            await using var reclaimContext = fixture.CreateDbContext();
            var reclaimed = await new PdsSyncOutboxRepository(reclaimContext).ClaimDueAsync(
                1,
                "worker-b",
                now.AddMinutes(2),
                TimeSpan.FromMinutes(5));
            await Assert.That(reclaimed).HasSingleItem();
        });
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(value => value.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(interceptor)
            .Options;
        await using var staleContext = new ExploreDbContext(options);
        staleContext.EnableTenantFilterBypass("ATProto fenced settlement race test.");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            new PdsSyncOutboxRepository(staleContext).TrySettleAsync(
                staleClaim,
                $"at://{outbox.Did}/{outbox.Collection}/{outbox.RecordKey}",
                "bafyreifenced",
                now.AddSeconds(30)));

        await using var verifyContext = fixture.CreateDbContext();
        var persistedOutbox = await verifyContext.PdsSyncOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        await Assert.That(persistedOutbox.Status).IsEqualTo(PdsSyncStatus.Processing);
        await Assert.That(persistedOutbox.LeaseOwner).IsEqualTo("worker-b");
        await Assert.That(persistedOutbox.LeaseFence).IsEqualTo(staleClaim.LeaseFence + 1);
        await Assert.That(await verifyContext.AtprotoRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.AtprotoOutboundRecordOwnerships.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.AtprotoRecordTenantPresentations.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PdsSettlement_SupersededCreateCannotSettleAfterRemoteSuccess()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-supersede");
        DateTime now = Utc(10);
        PdsSyncOutbox stale = CreateOutbox(scope, now);
        PdsSyncOutbox successor = CreateOutbox(
            scope,
            now.AddSeconds(1),
            PdsSyncOperation.Delete,
            sourceEntityId: stale.SourceEntityId,
            sourceVersion: Guid.CreateVersion7());
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.Add(stale);
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(1, "worker-a", now, TimeSpan.FromSeconds(90))).Single();
        context.PdsSyncOutbox.Add(successor);
        await context.SaveChangesAsync();
        await repository.SupersedePriorAsync(
            scope.TenantId,
            stale.SourceEntityType,
            stale.SourceEntityId,
            successor.Id,
            now.AddSeconds(2));
        await context.SaveChangesAsync();

        bool settled = await repository.TrySettleAsync(
            claim,
            $"at://{stale.Did}/{stale.Collection}/{stale.RecordKey}",
            "bafy-stale-success",
            now.AddSeconds(3));

        await Assert.That(settled).IsFalse();
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task EventDeliveryStateRead_ReturnsOnlyLatestRequestedTenantRowWithoutTracking()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-delivery-state");
        DateTime now = Utc(10);
        Guid eventId = Guid.CreateVersion7();
        PdsSyncOutbox completed = CreateOutbox(scope, now, sourceEntityId: eventId);
        completed.Status = PdsSyncStatus.Completed;
        completed.ProcessedAt = now.AddSeconds(1);
        completed.SettledUri = $"at://{completed.Did}/{completed.Collection}/{completed.RecordKey}";
        completed.SettledCid = "bafy-completed";
        PdsSyncOutbox deadLettered = CreateOutbox(
            scope,
            now.AddSeconds(2),
            PdsSyncOperation.Update,
            sourceEntityId: eventId,
            sourceVersion: Guid.CreateVersion7());
        deadLettered.Status = PdsSyncStatus.DeadLettered;
        deadLettered.LastError = "session_unavailable";
        deadLettered.DeadLetteredAt = now.AddSeconds(3);
        PdsSyncOutbox unrelated = CreateOutbox(scope, now.AddSeconds(4));
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.AddRange(completed, deadLettered, unrelated);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        IReadOnlyList<PdsSyncOutbox> rows = await new PdsSyncOutboxRepository(context)
            .GetCurrentEventDeliveryStatesAsync(scope.TenantId, [eventId]);

        await Assert.That(rows).HasSingleItem();
        await Assert.That(rows[0].Id).IsEqualTo(deadLettered.Id);
        await Assert.That(rows[0].LastError).IsEqualTo("session_unavailable");
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    [Category("EventLocationPrivacyExternal")]
    public async Task PdsCorrectionReceiptExistence_IsTenantBoundedAndIncludesSupersededRows()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-correction-receipt");
        PdsSyncOutbox correction = CreateOutbox(scope, Utc(10));
        PdsSyncOutbox successor = CreateOutbox(
            scope,
            Utc(11),
            PdsSyncOperation.Update,
            sourceEntityId: correction.SourceEntityId,
            sourceVersion: Guid.CreateVersion7(),
            payload: "{\"name\":\"Corrected event\",\"createdAt\":\"2026-07-18T11:00:00Z\"}");
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.AddRange(correction, successor);
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);

        int superseded = await repository.SupersedePriorAsync(
            scope.TenantId,
            correction.SourceEntityType,
            correction.SourceEntityId,
            successor.Id,
            Utc(11));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        bool exists = await repository.ExistsAsync(scope.TenantId, correction.Id);
        bool crossTenantExists = await repository.ExistsAsync(Guid.CreateVersion7(), correction.Id);

        await Assert.That(superseded).IsEqualTo(1);
        await Assert.That(exists).IsTrue();
        await Assert.That(crossTenantExists).IsFalse();
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    public async Task PdsSettlement_LinksCanonicalRecordBackToCommittedLocalEvent()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-event-link");
        DateTime now = Utc(10);
        await using var context = fixture.CreateDbContext();
        Actor actor = CreateActor(scope.UserId, "PDS event owner", now);
        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Committed before PDS delivery",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = actor,
            TenantId = scope.TenantId,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        PdsSyncOutbox outbox = CreateOutbox(scope, now.AddSeconds(1), sourceEntityId: eventEntity.Id);
        context.Actors.Add(actor);
        SetForeignKeyIfPresent(context, actor, "TenantId", scope.TenantId);
        context.AddRange(eventEntity, outbox);
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(
            1,
            "worker-link",
            now.AddSeconds(2),
            TimeSpan.FromSeconds(90))).Single();

        bool settled = await repository.TrySettleAsync(
            claim,
            $"at://{outbox.Did}/{outbox.Collection}/{outbox.RecordKey}",
            "bafy-event-link",
            now.AddSeconds(3));

        await Assert.That(settled).IsTrue();
        Guid recordId = (await context.AtprotoRecords.SingleAsync()).Id;
        await Assert.That((await context.Events.SingleAsync(value => value.Id == eventEntity.Id)).AtprotoRecordId)
            .IsEqualTo(recordId);
    }

    [Test]
    public async Task GlobalModerationOwnership_IncludesSoftDeletedEventWithLiveExactRecord()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("global-moderation-deleted-event");
        DateTime now = Utc(10);
        await using var context = fixture.CreateDbContext();
        Actor actor = CreateActor(scope.UserId, "Global moderation event owner", now);
        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Deleted local event with live remote record",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = actor,
            TenantId = scope.TenantId,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        PdsSyncOutbox outbox = CreateOutbox(scope, now.AddSeconds(1), sourceEntityId: eventEntity.Id);
        context.Actors.Add(actor);
        SetForeignKeyIfPresent(context, actor, "TenantId", scope.TenantId);
        context.AddRange(eventEntity, outbox);
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository outboxRepository = new(context);
        PdsSyncClaim claim = (await outboxRepository.ClaimDueAsync(
            1,
            "worker-global-moderation",
            now.AddSeconds(2),
            TimeSpan.FromSeconds(90))).Single();
        bool settled = await outboxRepository.TrySettleAsync(
            claim,
            $"at://{outbox.Did}/{outbox.Collection}/{outbox.RecordKey}",
            "bafy-global-moderation",
            now.AddSeconds(3));
        eventEntity.IsDeleted = true;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        AtprotoRecord liveRecord = await context.AtprotoRecords.SingleAsync();
        var repository = new AtprotoRecordRepository(context);

        List<AtprotoOutboundRecordOwnership> byActor = await repository.GetLiveGroundedEventOwnershipsForActorAsync(
            actor.Id,
            CancellationToken.None);
        List<AtprotoOutboundRecordOwnership> byActorAndDid =
            await repository.GetLiveGroundedEventOwnershipsForActorAndDidAsync(
                actor.Id,
                outbox.Did,
                CancellationToken.None);

        await Assert.That(settled).IsTrue();
        await Assert.That(liveRecord.TombstonedAt).IsNull();
        await Assert.That(byActor).HasSingleItem();
        await Assert.That(byActor[0].TenantId).IsEqualTo(scope.TenantId);
        await Assert.That(byActor[0].SourceEntityType).IsEqualTo("Event");
        await Assert.That(byActor[0].SourceEntityId).IsEqualTo(eventEntity.Id);
        await Assert.That(byActorAndDid).HasSingleItem();
        await Assert.That(byActorAndDid[0].AtprotoRecordId).IsEqualTo(byActor[0].AtprotoRecordId);
    }

    [Test]
    public async Task PdsSettlement_ExactCreateEchoSettlesWithoutOverwritingDifferentState()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-create-echo");
        DateTime now = Utc(10);
        PdsSyncOutbox outbox = CreateOutbox(scope, now);
        string uri = $"at://{outbox.Did}/{outbox.Collection}/{outbox.RecordKey}";
        const string cid = "bafy-create-echo";
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.Add(outbox);
        AtprotoRecord reorderedEcho = Echo(outbox, uri, cid, now.AddSeconds(1));
        reorderedEcho.RecordJson = "{\"createdAt\":\"2026-07-18T10:00:00Z\",\"name\":\"Fenced event\"}";
        reorderedEcho.RecordHash = new string('f', 64);
        context.AtprotoRecords.Add(reorderedEcho);
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(1, "worker-a", now, TimeSpan.FromSeconds(90))).Single();

        bool settled = await repository.TrySettleAsync(claim, uri, cid, now.AddSeconds(2));

        await Assert.That(settled).IsTrue();
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.AtprotoOutboundRecordOwnerships.IgnoreQueryFilters().CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task PdsSettlement_SameCidWithAlteredEchoPayloadIsRejected()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-altered-echo");
        DateTime now = Utc(10);
        PdsSyncOutbox outbox = CreateOutbox(scope, now);
        string uri = $"at://{outbox.Did}/{outbox.Collection}/{outbox.RecordKey}";
        const string cid = "bafy-altered-echo";
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.Add(outbox);
        AtprotoRecord alteredEcho = Echo(outbox, uri, cid, now.AddSeconds(1));
        alteredEcho.RecordJson = "{\"name\":\"Unrelated event\"}";
        context.AtprotoRecords.Add(alteredEcho);
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(1, "worker-a", now, TimeSpan.FromSeconds(90))).Single();

        bool settled = await repository.TrySettleAsync(claim, uri, cid, now.AddSeconds(2));

        await Assert.That(settled).IsFalse();
        await Assert.That(await context.AtprotoOutboundRecordOwnerships.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PdsSettlement_CompensationUpdateAcceptsObservedCanonicalBaseOnFirstAttempt()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-compensation-first");
        DateTime now = Utc(10);
        (PdsSyncOutbox predecessor, PdsSyncOutbox successor) = CompensationPair(scope, now);
        string uri = $"at://{successor.Did}/{successor.Collection}/{successor.RecordKey}";
        const string oldCid = "bafy-predecessor";
        const string newCid = "bafy-successor";
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.AddRange(predecessor, successor);
        context.AtprotoRecords.Add(Echo(predecessor, uri, oldCid, now));
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(1, "worker-a", now.AddSeconds(2), TimeSpan.FromSeconds(90))).Single();

        bool settled = await repository.TrySettleAsync(
            claim,
            uri,
            newCid,
            now.AddSeconds(3),
            observedBaseCid: oldCid);

        await Assert.That(settled).IsTrue();
        await Assert.That((await context.AtprotoRecords.SingleAsync()).Cid).IsEqualTo(newCid);
    }

    [Test]
    public async Task PdsSettlement_CompensationRetryAcceptsSemanticPredecessorEchoButRejectsUnrelatedThirdPayload()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-compensation-retry");
        DateTime now = Utc(10);
        (PdsSyncOutbox predecessor, PdsSyncOutbox successor) = CompensationPair(scope, now);
        string uri = $"at://{successor.Did}/{successor.Collection}/{successor.RecordKey}";
        const string remoteDesiredCid = "bafy-successor";
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.AddRange(predecessor, successor);
        AtprotoRecord reorderedPredecessorEcho = Echo(predecessor, uri, "bafy-predecessor", now);
        reorderedPredecessorEcho.RecordJson = "{\"createdAt\":\"2026-07-18T10:00:00Z\",\"name\":\"Fenced event\"}";
        context.AtprotoRecords.Add(reorderedPredecessorEcho);
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(1, "worker-a", now.AddSeconds(2), TimeSpan.FromSeconds(90))).Single();

        bool retrySettled = await repository.TrySettleAsync(
            claim,
            uri,
            remoteDesiredCid,
            now.AddSeconds(3),
            observedBaseCid: remoteDesiredCid);

        await Assert.That(retrySettled).IsTrue();

        await fixture.ResetAsync();
        scope = await SeedScopeAsync("pds-compensation-third");
        (predecessor, successor) = CompensationPair(scope, now);
        uri = $"at://{successor.Did}/{successor.Collection}/{successor.RecordKey}";
        await using var unrelatedContext = fixture.CreateDbContext();
        unrelatedContext.PdsSyncOutbox.AddRange(predecessor, successor);
        AtprotoRecord unrelatedEcho = Echo(predecessor, uri, "bafy-third", now);
        unrelatedEcho.RecordJson = "{\"name\":\"Unrelated third update\",\"createdAt\":\"2026-07-18T10:00:00Z\"}";
        unrelatedContext.AtprotoRecords.Add(unrelatedEcho);
        await unrelatedContext.SaveChangesAsync();
        PdsSyncOutboxRepository unrelatedRepository = new(unrelatedContext);
        claim = (await unrelatedRepository.ClaimDueAsync(1, "worker-a", now.AddSeconds(2), TimeSpan.FromSeconds(90))).Single();

        bool unrelatedSettled = await unrelatedRepository.TrySettleAsync(
            claim,
            uri,
            remoteDesiredCid,
            now.AddSeconds(3),
            observedBaseCid: remoteDesiredCid);

        await Assert.That(unrelatedSettled).IsFalse();
        await Assert.That(await unrelatedContext.AtprotoOutboundRecordOwnerships.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PdsDelivery_CompensationLineageBeyondMaximumDepthFailsClosedBeforeGatewayMutation()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-compensation-overflow");
        DateTime now = Utc(10);
        Guid sourceEntityId = Guid.CreateVersion7();
        PdsSyncOutbox successor = CreateOutbox(
            scope,
            now.AddSeconds(34),
            PdsSyncOperation.Update,
            sourceEntityId: sourceEntityId,
            sourceVersion: Guid.CreateVersion7(),
            payload: "{\"name\":\"Current desired event\",\"createdAt\":\"2026-07-18T10:00:00Z\"}");
        var predecessors = Enumerable.Range(0, 33)
            .Select(index => CreateOutbox(
                scope,
                now.AddSeconds(index),
                PdsSyncOperation.Create,
                sourceEntityId: sourceEntityId,
                sourceVersion: Guid.CreateVersion7(),
                payload: $"{{\"name\":\"Predecessor {index}\",\"createdAt\":\"2026-07-18T10:00:00Z\"}}"))
            .ToArray();
        for (var index = 0; index < predecessors.Length; index++)
        {
            predecessors[index].Status = PdsSyncStatus.Superseded;
            predecessors[index].SupersededById = index == predecessors.Length - 1
                ? successor.Id
                : predecessors[index + 1].Id;
            predecessors[index].SupersededAt = now.AddSeconds(index + 1);
        }

        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.AddRange(predecessors);
        context.PdsSyncOutbox.Add(successor);
        await context.SaveChangesAsync();
        var repository = new PdsSyncOutboxRepository(context);
        PdsSyncCompensationEvidence evidence = await repository.GetCompensationEvidenceAsync(successor);
        await Assert.That(evidence.IsComplete).IsFalse();

        DateTime claimedAt = now.AddMinutes(2);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(
            1,
            "worker-overflow",
            claimedAt,
            TimeSpan.FromSeconds(90))).Single();
        var gate = new PermittingDeliveryGate();
        var gateway = new CountingDeliveryGateway();
        var processor = new AtprotoPdsDeliveryProcessor(
            repository,
            gate,
            gateway,
            new FixedTimeProvider(claimedAt.AddSeconds(1)));

        AtprotoPdsClaimResult result = await processor.ProcessAsync(
            claim,
            TimeSpan.FromSeconds(90),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(AtprotoPdsClaimOutcome.DeliveryFailed);
        await Assert.That(result.FailureCode).IsEqualTo("record_conflict");
        await Assert.That(result.FailureDisposition).IsEqualTo(AtprotoPdsFailureDisposition.DeadLettered);
        await Assert.That(gate.CallCount).IsEqualTo(1);
        await Assert.That(gateway.CallCount).IsEqualTo(0);
        await Assert.That((await context.PdsSyncOutbox.SingleAsync(value => value.Id == successor.Id)).Status)
            .IsEqualTo(PdsSyncStatus.DeadLettered);
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PdsSettlement_ExactUpdateEchoAcceptsReturnedCidInsteadOfOldExpectedCid()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-update-echo");
        DateTime now = Utc(10);
        PdsSyncOutbox outbox = CreateOutbox(
            scope,
            now,
            PdsSyncOperation.Update,
            expectedCid: "bafy-old");
        string uri = $"at://{outbox.Did}/{outbox.Collection}/{outbox.RecordKey}";
        const string returnedCid = "bafy-new";
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.Add(outbox);
        context.AtprotoRecords.Add(Echo(outbox, uri, returnedCid, now.AddSeconds(1)));
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);
        PdsSyncClaim claim = (await repository.ClaimDueAsync(1, "worker-a", now, TimeSpan.FromSeconds(90))).Single();

        bool settled = await repository.TrySettleAsync(claim, uri, returnedCid, now.AddSeconds(2));

        await Assert.That(settled).IsTrue();
        await Assert.That((await context.AtprotoRecords.SingleAsync()).Cid).IsEqualTo(returnedCid);
    }

    [Test]
    public async Task PdsSettlement_DeleteEchoAndAbsentRecordBothCompleteWithoutFabricatedLiveRecord()
    {
        await fixture.ResetAsync();
        FederationScope scope = await SeedScopeAsync("pds-delete-echo");
        DateTime now = Utc(10);
        PdsSyncOutbox echoedDelete = CreateOutbox(
            scope,
            now,
            PdsSyncOperation.Delete,
            expectedCid: "bafy-before-delete");
        Guid absentSourceId = Guid.CreateVersion7();
        PdsSyncOutbox absentDelete = CreateOutbox(
            scope,
            now.AddSeconds(1),
            PdsSyncOperation.Delete,
            sourceEntityId: absentSourceId,
            sourceVersion: Guid.CreateVersion7(),
            recordKey: "3m7absent");
        PdsSyncOutbox absentPredecessor = CreateOutbox(
            scope,
            now,
            sourceEntityId: absentSourceId,
            sourceVersion: Guid.CreateVersion7(),
            recordKey: absentDelete.RecordKey);
        absentPredecessor.Status = PdsSyncStatus.Superseded;
        absentPredecessor.SupersededById = absentDelete.Id;
        absentPredecessor.SupersededAt = now.AddSeconds(1);
        string echoedUri = $"at://{echoedDelete.Did}/{echoedDelete.Collection}/{echoedDelete.RecordKey}";
        AtprotoRecord tombstone = Echo(echoedDelete, echoedUri, "bafy-before-delete", now);
        tombstone.Cid = null;
        tombstone.RecordJson = null;
        tombstone.RecordHash = null;
        tombstone.TombstonedAt = now.AddSeconds(1);
        await using var context = fixture.CreateDbContext();
        context.PdsSyncOutbox.AddRange(echoedDelete, absentPredecessor, absentDelete);
        context.AtprotoRecords.Add(tombstone);
        await context.SaveChangesAsync();
        PdsSyncOutboxRepository repository = new(context);
        IReadOnlyList<PdsSyncClaim> claims = await repository.ClaimDueAsync(2, "worker-a", now.AddSeconds(2), TimeSpan.FromSeconds(90));

        bool echoedSettled = await repository.TrySettleAsync(
            claims.Single(value => value.OutboxId == echoedDelete.Id),
            echoedUri,
            "bafy-before-delete",
            now.AddSeconds(3));
        bool absentSettled = await repository.TrySettleAsync(
            claims.Single(value => value.OutboxId == absentDelete.Id),
            $"at://{absentDelete.Did}/{absentDelete.Collection}/{absentDelete.RecordKey}",
            AtprotoPdsDeliveryResult.AbsentRecordCid,
            now.AddSeconds(3));

        await Assert.That(echoedSettled).IsTrue();
        await Assert.That(absentSettled).IsTrue();
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(1);
        await Assert.That((await context.AtprotoRecords.SingleAsync()).TombstonedAt).IsNotNull();
    }

    private async Task<FederationScope> SeedScopeAsync(string slug)
    {
        await using var context = fixture.CreateDbContext();
        var now = Utc(9);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = slug,
            Slug = $"{slug}-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"{slug}@example.test",
                FirstName = "ATProto",
                LastName = "Owner"
            },
            EmailVerified = true,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = now
        };
        var tenantUser = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now,
            CreatedAt = now
        };
        context.TenantUsers.Add(tenantUser);
        await context.SaveChangesAsync();
        return new FederationScope(tenant.Id, user.Id);
    }

    private static AtprotoEventProjection Projection(
        Guid atprotoRecordId,
        long sourceVersion,
        string name) => new()
        {
            AtprotoRecordId = atprotoRecordId,
            Name = name,
            CreatedAt = new DateTimeOffset(Utc(10)),
            StartsAt = new DateTimeOffset(Utc(11)),
            SourceUrl = "https://events.example/source",
            SourceVersion = sourceVersion,
            MaterializedAt = Utc(10)
        };

    private static AtprotoPdsSnapshot Snapshot(
        string did,
        AtprotoRecord record,
        string name,
        DateTime observedAt)
    {
        AtprotoEventProjection projection = Projection(record.Id, record.SourceVersion, name);
        projection.MaterializedAt = observedAt;
        return new(
            did,
            [new(record.Collection, record.RecordKey)],
            [new(record, projection)]);
    }

    private static AtprotoRecord SnapshotRecord(
        string did,
        string recordKey,
        DateTime observedAt,
        long sourceVersion = 0,
        string collection = "community.lexicon.calendar.event") => new()
        {
            Id = Guid.CreateVersion7(),
            Did = did,
            Collection = collection,
            RecordKey = recordKey,
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            Cid = $"bafy-{recordKey}",
            Uri = $"at://{did}/{collection}/{recordKey}",
            SourceVersion = sourceVersion,
            SourceCursor = sourceVersion,
            RecordJson = collection == "community.lexicon.calendar.event"
                ? $"{{\"name\":\"{recordKey}\",\"createdAt\":\"2026-07-18T10:00:00Z\"}}"
                : "{\"status\":\"community.lexicon.calendar.rsvp#going\"}",
            RecordHash = new string('d', 64),
            IndexedAt = observedAt,
            UpdatedAt = observedAt
        };

    private static PdsSyncOutbox CreateOutbox(
        FederationScope scope,
        DateTime createdAt,
        PdsSyncOperation operation = PdsSyncOperation.Create,
        string? expectedCid = null,
        Guid? sourceEntityId = null,
        Guid? sourceVersion = null,
        string recordKey = "3m7fenced",
        string? payload = null) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            UserId = scope.UserId,
            Did = "did:plc:fenced-owner",
            Collection = "community.lexicon.calendar.event",
            RecordKey = recordKey,
            Operation = operation,
            Payload = operation == PdsSyncOperation.Delete
            ? null
            : payload ?? "{\"name\":\"Fenced event\",\"createdAt\":\"2026-07-18T10:00:00Z\"}",
            PayloadHash = new string('a', 64),
            IdempotencyKey = $"event:{Guid.CreateVersion7():N}:create",
            PdsHost = "https://pds.example",
            SourceEntityType = "Event",
            SourceEntityId = sourceEntityId ?? Guid.CreateVersion7(),
            SourceVersion = sourceVersion ?? Guid.CreateVersion7(),
            ExpectedCid = expectedCid,
            Status = PdsSyncStatus.Pending,
            CreatedAt = createdAt,
            MaxRetries = 3
        };

    private static (PdsSyncOutbox Predecessor, PdsSyncOutbox Successor) CompensationPair(
        FederationScope scope,
        DateTime now)
    {
        Guid sourceEntityId = Guid.CreateVersion7();
        var successor = CreateOutbox(
            scope,
            now.AddSeconds(1),
            PdsSyncOperation.Update,
            sourceEntityId: sourceEntityId,
            sourceVersion: Guid.CreateVersion7(),
            payload: "{\"name\":\"Updated event\",\"createdAt\":\"2026-07-18T10:00:00Z\"}");
        var predecessor = CreateOutbox(
            scope,
            now,
            sourceEntityId: sourceEntityId,
            sourceVersion: Guid.CreateVersion7());
        predecessor.Status = PdsSyncStatus.Superseded;
        predecessor.SupersededById = successor.Id;
        predecessor.SupersededAt = now.AddSeconds(1);
        return (predecessor, successor);
    }

    private static AtprotoRecord Echo(
        PdsSyncOutbox outbox,
        string uri,
        string cid,
        DateTime observedAt) => new()
        {
            Did = outbox.Did,
            Collection = outbox.Collection,
            RecordKey = outbox.RecordKey,
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            Uri = uri,
            Cid = cid,
            RecordJson = outbox.Payload,
            RecordHash = outbox.PayloadHash,
            IndexedAt = observedAt,
            UpdatedAt = observedAt
        };

    [Test]
    public async Task JetstreamApply_AccountPurgeTombstonesInboundRecordsAndHidesPresentations()
    {
        await fixture.ResetAsync();
        var scope = await SeedScopeAsync("jetstream-account-purge");
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        var now = CurrentUtc();
        var claim = await repository.TryClaimAsync(
            "wss://jetstream.example/subscribe",
            "worker-purge",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");

        bool seeded = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            ExpectedCursor: 0,
            NextCursor: 1,
            IncomingRecord(sourceVersion: 1, now),
            [new AtprotoRecordTenantPresentation { TenantId = scope.TenantId, IsVisible = true }],
            Quarantine: null,
            now));

        bool purged = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            ExpectedCursor: 1,
            NextCursor: 2,
            Record: null,
            [],
            Quarantine: null,
            now.AddSeconds(1))
        {
            AccountPurge = new AtprotoAccountPurge("did:plc:remote-owner", SourceVersion: 5, "deleted")
        });

        context.ChangeTracker.Clear();
        var persistedRecord = await context.AtprotoRecords.AsNoTracking().SingleAsync();
        var presentation = await context.AtprotoRecordTenantPresentations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync();
        var state = await context.AtprotoJetstreamConsumerStates.AsNoTracking().SingleAsync();

        await Assert.That(seeded).IsTrue();
        await Assert.That(purged).IsTrue();
        // Tombstoned rather than deleted, so replay of the same seq stays idempotent.
        await Assert.That(persistedRecord.TombstonedAt).IsEqualTo(now.AddSeconds(1));
        await Assert.That(presentation.IsVisible).IsFalse();
        await Assert.That(presentation.SourceVersion).IsEqualTo(5);
        await Assert.That(state.Cursor).IsEqualTo(2);
    }

    [Test]
    public async Task JetstreamApply_AccountPurgeLeavesOutboundRecordsUntouched()
    {
        await fixture.ResetAsync();
        await SeedScopeAsync("jetstream-account-purge-outbound");
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        var now = CurrentUtc();
        var claim = await repository.TryClaimAsync(
            "wss://jetstream.example/subscribe",
            "worker-purge-outbound",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");

        AtprotoRecord outbound = IncomingRecord(sourceVersion: 1, now);
        outbound.Direction = AtprotoRecordDirection.Outbound;
        outbound.Provenance = AtprotoRecordProvenance.LocalLifecycle;
        outbound.Id = Guid.CreateVersion7();
        context.AtprotoRecords.Add(outbound);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        bool purged = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            ExpectedCursor: 0,
            NextCursor: 1,
            Record: null,
            [],
            Quarantine: null,
            now.AddSeconds(1))
        {
            AccountPurge = new AtprotoAccountPurge("did:plc:remote-owner", SourceVersion: 5, "deleted")
        });

        context.ChangeTracker.Clear();
        var persisted = await context.AtprotoRecords.AsNoTracking().SingleAsync();

        // A remote account signal must never retire locally authored records we published ourselves.
        await Assert.That(purged).IsTrue();
        await Assert.That(persisted.TombstonedAt).IsNull();
    }

    [Test]
    public async Task JetstreamApply_RejectsRequestsThatDoNotCarryExactlyOneEffect()
    {
        await fixture.ResetAsync();
        var scope = await SeedScopeAsync("jetstream-effect-guard");
        await using var context = fixture.CreateDbContext();
        var repository = new AtprotoJetstreamRepository(context);
        var now = CurrentUtc();
        var claim = await repository.TryClaimAsync(
            "wss://jetstream.example/subscribe",
            "worker-guard",
            now,
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("Claim was not acquired.");
        var purge = new AtprotoAccountPurge("did:plc:remote-owner", SourceVersion: 5, "deleted");

        bool none = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim, 0, 1, Record: null, [], Quarantine: null, now));
        bool recordAndPurge = await repository.TryApplyAndAdvanceAsync(new AtprotoJetstreamApplyRequest(
            claim,
            0,
            1,
            IncomingRecord(sourceVersion: 1, now),
            [new AtprotoRecordTenantPresentation { TenantId = scope.TenantId, IsVisible = true }],
            Quarantine: null,
            now)
        {
            AccountPurge = purge
        });

        await Assert.That(none).IsFalse();
        await Assert.That(recordAndPurge).IsFalse();
        await Assert.That(await context.AtprotoRecords.CountAsync()).IsEqualTo(0);
    }

    private static AtprotoRecord IncomingRecord(long sourceVersion, DateTime observedAt) => new()
    {
        Did = "did:plc:remote-owner",
        Collection = "community.lexicon.calendar.event",
        RecordKey = "3m7remote",
        Direction = AtprotoRecordDirection.Inbound,
        Provenance = AtprotoRecordProvenance.Jetstream,
        Cid = $"bafyreiv{sourceVersion}",
        Uri = "at://did:plc:remote-owner/community.lexicon.calendar.event/3m7remote",
        SourceVersion = sourceVersion,
        RecordJson = "{\"name\":\"Remote event\",\"createdAt\":\"2026-07-18T10:00:00Z\"}",
        RecordHash = new string('b', 64),
        IndexedAt = observedAt,
        UpdatedAt = observedAt
    };

    private static Actor CreateActor(Guid userId, string displayName, DateTime createdAt)
    {
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = userId,
            Pii = new ActorPii { DisplayName = displayName },
            CreatedAt = createdAt,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        return actor;
    }

    private static void SetForeignKeyIfPresent(
        ExploreDbContext context,
        object entity,
        string propertyName,
        object value)
    {
        if (context.Model.FindEntityType(entity.GetType())?.FindProperty(propertyName) is not null)
        {
            context.Entry(entity).Property(propertyName).CurrentValue = value;
        }
    }

    private static DateTime Utc(int hour) => new(2026, 7, 18, hour, 0, 0, DateTimeKind.Utc);

    private static DateTime CurrentUtc()
    {
        DateTime now = DateTime.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % 10), DateTimeKind.Utc);
    }

    private sealed record FederationScope(Guid TenantId, Guid UserId);
    private sealed record StaticTenantContext(Guid TenantId) : ITenantContext;

    private sealed class PermittingDeliveryGate : IAtprotoDeliveryGate
    {
        public int CallCount { get; private set; }

        public Task<AtprotoDeliveryGateResult> CheckDeliveryAsync(
            PdsSyncOutbox outbox,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(AtprotoDeliveryGateResult.Permit());
        }
    }

    private sealed class CountingDeliveryGateway : IAtprotoPdsDeliveryGateway
    {
        public int CallCount { get; private set; }

        public Task<AtprotoPdsDeliveryResult> DeliverAsync(
            AtprotoPdsDeliveryRequest command,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(AtprotoPdsDeliveryResult.Failed("unexpected_gateway_call", false));
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class ReclaimOnSaveInterceptor(Func<Task> reclaim) : SaveChangesInterceptor
    {
        private bool _invoked;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_invoked)
            {
                _invoked = true;
                await reclaim();
            }

            return result;
        }
    }
}
