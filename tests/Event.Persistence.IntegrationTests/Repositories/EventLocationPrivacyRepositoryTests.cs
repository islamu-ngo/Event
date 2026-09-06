// ABOUTME: PostgreSQL integration tests for EventLocation repositories, filters, audits, and concurrency.
// ABOUTME: Uses the current EF model without migrations so the expand-migration wave remains independently owned.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<ProjectionTestContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("ProjectionDb")]
[Category("EventLocationPrivacy")]
public sealed class EventLocationPrivacyRepositoryTests(ProjectionTestContainerFixture fixture)
{
    [Test]
    public async Task EventLocationRepository_ReadsAreTenantFilteredTrackedOrUntrackedAndBounded()
    {
        await using var seedContext = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(seedContext);
        EventLocationGraph tenantA = await SeedGraphAsync(seedContext, "repository-a");
        EventLocationGraph tenantB = await SeedGraphAsync(seedContext, "repository-b");

        await using var context = fixture.CreateDbContext(new TestTenantContext(tenantA.TenantId));
        var repository = new EventLocationRepository(context);

        IReadOnlyList<EventLocation> batch = await repository.GetByIdsAsync(
            [tenantA.EventLocationId, tenantB.EventLocationId],
            CancellationToken.None);

        await Assert.That(batch.Select(item => item.Id)).IsEquivalentTo([tenantA.EventLocationId]);
        await Assert.That(context.ChangeTracker.Entries<EventLocation>()).IsEmpty();

        EventLocation? tracked = await repository.GetForUpdateAsync(
            tenantA.EventLocationId,
            CancellationToken.None);

        await Assert.That(tracked).IsNotNull();
        await Assert.That(context.Entry(tracked!).State).IsEqualTo(EntityState.Unchanged);

        Guid[] oversizedBatch = Enumerable.Range(0, IEventLocationRepository.MaximumBatchSize + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.GetByIdsAsync(oversizedBatch, CancellationToken.None));
    }

    [Test]
    public async Task EventLocationRepository_AddAtomicallyPersistsTruthfulInitialPolicyAudit()
    {
        await using var context = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(context);
        EventLocationGraph graph = await SeedGraphAsync(context, "initial-policy");
        context.ChangeTracker.Clear();

        EventLocation aggregate = await context.EventLocations
            .AsNoTracking()
            .SingleAsync(item => item.Id == graph.EventLocationId);
        EventLocationDisclosureAudit audit = await context.EventLocationDisclosureAudits
            .AsNoTracking()
            .SingleAsync(item => item.EventLocationId == graph.EventLocationId);

        await Assert.That(audit.Reason).IsEqualTo(EventLocationDisclosureAuditReasonEnum.AssociationCreated);
        await Assert.That(audit.PreviousPolicyVersion).IsEqualTo(0);
        await Assert.That(audit.NewPolicyVersion).IsEqualTo(aggregate.PolicyVersion).And.IsEqualTo(1);
        await Assert.That(audit.NewFields).IsEqualTo(GetDisclosureFields(aggregate));
        await Assert.That(audit.NewAudienceId).IsEqualTo(aggregate.FullDetailsAudienceId);
        await Assert.That(audit.NewRevealFullDetailsFromUtc).IsEqualTo(aggregate.RevealFullDetailsFromUtc);
        await Assert.That(aggregate.LastPolicyActorUserId).IsNotNull();
        await Assert.That(aggregate.LastPolicyChangedAtUtc).IsNotNull();
        await Assert.That(audit.ActorUserId).IsEqualTo(aggregate.LastPolicyActorUserId!.Value);
        await Assert.That(audit.OccurredAtUtc).IsEqualTo(aggregate.LastPolicyChangedAtUtc!.Value);
    }

    [Test]
    public async Task EventLocationRepository_EnforcesActiveUniquenessAndSoftDeleteFiltering()
    {
        await using var context = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(context);
        EventLocationGraph graph = await SeedGraphAsync(context, "active-unique");
        EventLocation duplicate = EventLocation.CreatePhysical(
            graph.TenantId,
            graph.EventId,
            graph.LocationId,
            graph.ActorUserId,
            DateTime.UtcNow);

        context.EventLocations.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var repository = new EventLocationRepository(context);
        EventLocation existing = await repository.GetForUpdateAsync(graph.EventLocationId, CancellationToken.None)
            ?? throw new InvalidOperationException("Seeded EventLocation was not found.");
        existing.DetachFinalReference(graph.ActorUserId, DateTime.UtcNow);
        await repository.SaveChangesAsync(CancellationToken.None);

        EventLocation replacement = EventLocation.CreatePhysical(
            graph.TenantId,
            graph.EventId,
            graph.LocationId,
            graph.ActorUserId,
            DateTime.UtcNow);
        await repository.AddAsync(replacement, CancellationToken.None);

        EventLocation? active = await repository.FindActivePhysicalAsync(
            graph.EventId,
            graph.LocationId,
            CancellationToken.None);
        await Assert.That(active?.Id).IsEqualTo(replacement.Id);
        await Assert.That(await context.EventLocations.CountAsync(item =>
            item.EventId == graph.EventId && item.LocationId == graph.LocationId)).IsEqualTo(1);
    }

    [Test]
    public async Task EventLocationRepository_RejectsDuplicateActiveTba()
    {
        await using var context = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(context);
        EventLocationGraph graph = await SeedGraphAsync(context, "tba-unique");
        EventLocation first = EventLocation.CreateToBeAnnounced(
            graph.TenantId, graph.EventId, graph.ActorUserId, DateTime.UtcNow);
        EventLocation second = EventLocation.CreateToBeAnnounced(
            graph.TenantId, graph.EventId, graph.ActorUserId, DateTime.UtcNow);

        context.EventLocations.AddRange(first, second);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task PrivacyRepositories_FailClosedWithoutTenantContext()
    {
        await using var setupContext = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(setupContext);
        EventLocationGraph graph = await SeedGraphAsync(setupContext, "tenantless");
        await using var context = CreateTenantlessContext();

        var locationRepository = new EventLocationRepository(context);
        var policyRepository = new EventLocationDisclosureAuditRepository(context);
        var exactRepository = new EventLocationExactReadAuditRepository(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            locationRepository.GetByIdsAsync([graph.EventLocationId], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policyRepository.GetByEventLocationAsync(graph.EventLocationId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exactRepository.GetByEventLocationsAsync([graph.EventLocationId], CancellationToken.None));
    }

    [Test]
    public async Task PrivacyAuditRepositories_AreTenantFilteredBoundedAndNoTracking()
    {
        await using var seedContext = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(seedContext);
        EventLocationGraph tenantA = await SeedGraphAsync(seedContext, "audit-tenant-a");
        EventLocationGraph tenantB = await SeedGraphAsync(seedContext, "audit-tenant-b");
        seedContext.EventLocationExactReadAudits.AddRange(
            CreateExactReadAudit(tenantA),
            CreateExactReadAudit(tenantB));
        await seedContext.SaveChangesAsync();

        await using var context = fixture.CreateDbContext(new TestTenantContext(tenantA.TenantId));
        var policyRepository = new EventLocationDisclosureAuditRepository(context);
        var exactRepository = new EventLocationExactReadAuditRepository(context);

        IReadOnlyList<EventLocationDisclosureAudit> policy =
            await policyRepository.GetByEventLocationAsync(tenantA.EventLocationId, CancellationToken.None);
        IReadOnlyList<EventLocationExactReadAudit> exact =
            await exactRepository.GetByEventLocationsAsync(
                [tenantA.EventLocationId, tenantB.EventLocationId], CancellationToken.None);

        await Assert.That(policy.Count).IsEqualTo(1);
        await Assert.That(exact.Select(item => item.EventLocationId)).IsEquivalentTo([tenantA.EventLocationId]);
        await Assert.That(context.ChangeTracker.Entries<EventLocationDisclosureAudit>()).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries<EventLocationExactReadAudit>()).IsEmpty();

        Guid[] oversizedBatch = Enumerable.Range(
                0,
                IEventLocationExactReadAuditRepository.MaximumBatchSize + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            exactRepository.GetByEventLocationsAsync(oversizedBatch, CancellationToken.None));
    }

    [Test]
    public async Task ExactReadAuditRepository_AppendsAndReadsUuidV7Evidence()
    {
        await using var setupContext = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(setupContext);
        EventLocationGraph graph = await SeedGraphAsync(setupContext, "exact-read");
        await using var context = fixture.CreateDbContext(new TestTenantContext(graph.TenantId));
        var repository = new EventLocationExactReadAuditRepository(context);
        EventLocationExactReadAudit audit = CreateExactReadAudit(graph);

        await repository.AppendAsync(audit, CancellationToken.None);
        context.ChangeTracker.Clear();
        IReadOnlyList<EventLocationExactReadAudit> persisted =
            await repository.GetByEventLocationsAsync([graph.EventLocationId], CancellationToken.None);

        await Assert.That(persisted.Select(item => item.Id)).IsEquivalentTo([audit.Id]);
        await Assert.That(audit.Id.Version).IsEqualTo(7);
        await Assert.That(context.ChangeTracker.Entries<EventLocationExactReadAudit>()).IsEmpty();
    }

    [Test]
    public async Task AppendOnlyRepositories_EnforcePolicySequenceAndCheckpointContinuity()
    {
        await using var context = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(context);
        EventLocationGraph graph = await SeedGraphAsync(context, "append-only");
        var auditRepository = new EventLocationDisclosureAuditRepository(context);
        var checkpointRepository = new PrivacyErasureReplayCheckpointRepository(context);

        context.ChangeTracker.Clear();
        EventLocation eventLocation = await new EventLocationRepository(context)
            .GetForUpdateAsync(graph.EventLocationId, CancellationToken.None)
            ?? throw new InvalidOperationException("EventLocation was not available for its first policy mutation.");
        EventLocationDisclosureAudit firstAudit = eventLocation.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.Country,
            LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
            null,
            1,
            graph.ActorUserId,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            DateTime.UtcNow);
        await auditRepository.AppendAsync(firstAudit, CancellationToken.None);

        EventLocationDisclosureAudit staleAudit = CreatePolicyAudit(graph, 1, 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auditRepository.AppendAsync(staleAudit, CancellationToken.None));
        EventLocationDisclosureAudit futureAudit = CreatePolicyAudit(graph, 2, 3);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auditRepository.AppendAsync(futureAudit, CancellationToken.None));

        IReadOnlyList<EventLocationDisclosureAudit> history =
            await auditRepository.GetByEventLocationAsync(graph.EventLocationId, CancellationToken.None);
        await Assert.That(history.Select(item => item.NewPolicyVersion)).IsEquivalentTo([1, 2]);
        await Assert.That(eventLocation.PolicyVersion).IsEqualTo(2);
        await Assert.That(eventLocation.ShowCountry).IsTrue();
        await Assert.That(eventLocation.FullDetailsAudienceId)
            .IsEqualTo((int)LocationDisclosureAudienceEnum.AnyCurrentRegistrant);

        PrivacyErasureIntent firstIntent = CreateAuthorityIntent(1);
        PrivacyErasureReplayCheckpoint firstCheckpoint =
            PrivacyErasureReplayCheckpoint.Start(firstIntent, DateTime.UtcNow);
        await checkpointRepository.AppendAsync(firstCheckpoint, CancellationToken.None);

        PrivacyErasureIntent secondIntent = CreateAuthorityIntent(2);
        PrivacyErasureReplayCheckpoint secondCheckpoint =
            PrivacyErasureReplayCheckpoint.Advance(firstCheckpoint, secondIntent, DateTime.UtcNow);
        await checkpointRepository.AppendAsync(secondCheckpoint, CancellationToken.None);

        PrivacyErasureIntent disconnectedIntent = CreateAuthorityIntent(1);
        PrivacyErasureReplayCheckpoint disconnectedCheckpoint =
            PrivacyErasureReplayCheckpoint.Start(disconnectedIntent, DateTime.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            checkpointRepository.AppendAsync(disconnectedCheckpoint, CancellationToken.None));

        context.ChangeTracker.Clear();
        PrivacyErasureReplayCheckpoint? latest =
            await checkpointRepository.GetLatestAsync(CancellationToken.None);
        await Assert.That(latest?.Id).IsEqualTo(secondCheckpoint.Id);
        await Assert.That(graph.EventLocationId.Version).IsEqualTo(7);
        await Assert.That(firstAudit.Id.Version).IsEqualTo(7);
        await Assert.That(firstCheckpoint.Id.Version).IsEqualTo(7);
        await Assert.That(context.ChangeTracker.Entries<PrivacyErasureReplayCheckpoint>()).IsEmpty();
    }

    [Test]
    public async Task SaveChanges_WhenPrivacyEvidenceIsUpdatedOrDeleted_RejectsMutation()
    {
        await using var context = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(context);
        EventLocationGraph graph = await SeedGraphAsync(context, "immutable");
        EventLocation eventLocation = await context.EventLocations
            .SingleAsync(item => item.Id == graph.EventLocationId);
        EventLocationDisclosureAudit policyAudit = eventLocation.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.Country,
            LocationDisclosureAudienceEnum.Never,
            null,
            1,
            graph.ActorUserId,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            DateTime.UtcNow);
        EventLocationExactReadAudit exactReadAudit = EventLocationExactReadAudit.Create(
            graph.TenantId,
            graph.EventLocationId,
            graph.ActorUserId,
            EventLocationExactReadPurposeEnum.EventManagement,
            true,
            DateTime.UtcNow,
            Guid.CreateVersion7(),
            null);
        PrivacyErasureReplayCheckpoint checkpoint =
            PrivacyErasureReplayCheckpoint.Start(CreateAuthorityIntent(1), DateTime.UtcNow);

        context.AddRange(policyAudit, exactReadAudit, checkpoint);
        await context.SaveChangesAsync();

        foreach (object evidence in new object[] { policyAudit, exactReadAudit, checkpoint })
        {
            context.Entry(evidence).State = EntityState.Modified;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            context.ChangeTracker.Clear();
        }

        EventLocationDisclosureAudit persisted = await context.EventLocationDisclosureAudits
            .SingleAsync(item => item.Id == policyAudit.Id);
        context.Remove(persisted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task DisclosurePolicy_WhenAggregateWritersRace_LoserGetsStableConcurrencyConflict()
    {
        EventLocationGraph graph;
        await using (var setupContext = fixture.CreateDbContext())
        {
            await ClearPrivacyStateAsync(setupContext);
            graph = await SeedGraphAsync(setupContext, "audit-race");
        }

        await using var firstContext = fixture.CreateDbContext(new TestTenantContext(graph.TenantId));
        await using var secondContext = fixture.CreateDbContext(new TestTenantContext(graph.TenantId));
        var firstLocationRepository = new EventLocationRepository(firstContext);
        var secondLocationRepository = new EventLocationRepository(secondContext);
        EventLocation firstAggregate = await firstLocationRepository.GetForUpdateAsync(
            graph.EventLocationId, CancellationToken.None)
            ?? throw new InvalidOperationException("First aggregate writer did not load EventLocation.");
        EventLocation secondAggregate = await secondLocationRepository.GetForUpdateAsync(
            graph.EventLocationId, CancellationToken.None)
            ?? throw new InvalidOperationException("Second aggregate writer did not load EventLocation.");
        EventLocationDisclosureAudit firstAudit = firstAggregate.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.Country,
            LocationDisclosureAudienceEnum.Never,
            null,
            1,
            graph.ActorUserId,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            DateTime.UtcNow);
        EventLocationDisclosureAudit secondAudit = secondAggregate.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.City,
            LocationDisclosureAudienceEnum.Never,
            null,
            1,
            graph.ActorUserId,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            DateTime.UtcNow.AddMilliseconds(1));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Exception?> first = PersistPolicyConcurrentlyAsync(firstContext, firstAudit, gate.Task);
        Task<Exception?> second = PersistPolicyConcurrentlyAsync(secondContext, secondAudit, gate.Task);
        gate.SetResult();
        Exception?[] outcomes = await Task.WhenAll(first, second);

        await Assert.That(outcomes.Count(item => item is null)).IsEqualTo(1);
        ConcurrencyConflictException loser = outcomes.OfType<ConcurrencyConflictException>().Single();
        await Assert.That(loser.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(loser.EntityType).IsEqualTo(nameof(EventLocation));
        await using var verifyContext = fixture.CreateDbContext();
        await Assert.That(await verifyContext.EventLocationDisclosureAudits.CountAsync(item =>
            item.EventLocationId == graph.EventLocationId)).IsEqualTo(2);
        EventLocation persisted = await verifyContext.EventLocations
            .AsNoTracking()
            .SingleAsync(item => item.Id == graph.EventLocationId);
        await Assert.That(persisted.PolicyVersion).IsEqualTo(2);
    }

    [Test]
    public async Task SaveChanges_RejectsCarrierEventLocationAndRoomMismatches()
    {
        await using var context = fixture.CreateDbContext();
        await ClearPrivacyStateAsync(context);
        EventLocationGraph graph = await SeedGraphAsync(context, "carrier-guards");
        context.ChangeTracker.Clear();
        var session = new EventSession
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.TenantId,
            Tenant = null!,
            EventId = Guid.CreateVersion7(),
            Event = null!
        };
        context.EventSessions.Add(session);
        context.Entry(session).Property(nameof(EventSession.EventLocationId)).CurrentValue = graph.EventLocationId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        EventLocation eventLocation = await context.EventLocations.SingleAsync(item => item.Id == graph.EventLocationId);
        var group = new EventSessionGroup
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.TenantId,
            Tenant = null!,
            EventId = graph.EventId,
            Event = null!,
            Name = "Location mismatch"
        };
        group.AssignEventLocation(eventLocation);
        group.LocationId = Guid.CreateVersion7();
        context.EventSessionGroups.Add(group);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        eventLocation = await context.EventLocations.SingleAsync(item => item.Id == graph.EventLocationId);
        var agendaItem = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.TenantId,
            Tenant = null!,
            EventId = graph.EventId,
            Event = null!,
            Title = "Room mismatch",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(1)
        };
        agendaItem.AssignEventLocation(eventLocation);
        agendaItem.RoomId = Guid.CreateVersion7();
        context.EventAgendaItems.Add(agendaItem);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        eventLocation = await context.EventLocations.SingleAsync(item => item.Id == graph.EventLocationId);
        var parentSession = new EventSession
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.TenantId,
            Tenant = null!,
            EventId = graph.EventId,
            Event = null!
        };
        var sessionAgendaItem = new EventSessionAgendaItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.TenantId,
            Tenant = null!,
            EventSessionId = parentSession.Id,
            EventSession = parentSession,
            Title = "Session event mismatch"
        };
        sessionAgendaItem.AssignEventLocation(eventLocation);
        parentSession.EventId = Guid.CreateVersion7();
        context.AddRange(parentSession, sessionAgendaItem);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task EventLocationConcurrency_WhenStaleWriterLoses_TranslatesToStableConflictAndKeepsUuidV7()
    {
        EventLocationGraph graph;
        await using (var setupContext = fixture.CreateDbContext())
        {
            await ClearPrivacyStateAsync(setupContext);
            graph = await SeedGraphAsync(setupContext, "concurrency");
        }

        await using var winningContext = fixture.CreateDbContext();
        await using var losingContext = fixture.CreateDbContext();
        var winningRepository = new EventLocationRepository(winningContext);
        var losingRepository = new EventLocationRepository(losingContext);
        EventLocation winner = await winningRepository.GetForUpdateAsync(graph.EventLocationId, CancellationToken.None)
            ?? throw new InvalidOperationException("Winning EventLocation was not found.");
        EventLocation loser = await losingRepository.GetForUpdateAsync(graph.EventLocationId, CancellationToken.None)
            ?? throw new InvalidOperationException("Losing EventLocation was not found.");

        winner.DetachFinalReference(graph.ActorUserId, DateTime.UtcNow);
        await new EfCoreUnitOfWork(winningContext).ExecuteInTransactionAsync(
            ct => winningRepository.SaveChangesAsync(ct),
            CancellationToken.None);

        loser.DetachFinalReference(graph.ActorUserId, DateTime.UtcNow.AddSeconds(1));
        ConcurrencyConflictException? exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new EfCoreUnitOfWork(losingContext).ExecuteInTransactionAsync(
                ct => losingRepository.SaveChangesAsync(ct),
                CancellationToken.None));

        await Assert.That(exception!.Code).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(exception.EntityType).IsEqualTo(nameof(EventLocation));

        await using var verifyContext = fixture.CreateDbContext();
        EventLocation persisted = await verifyContext.EventLocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == graph.EventLocationId);
        await Assert.That(persisted.ConcurrencyStamp.Version).IsEqualTo(7);
    }

    private static async Task<EventLocationGraph> SeedGraphAsync(ExploreDbContext context, string suffix)
    {
        await EnsureLocationPrivacyLookupsAsync(context);
        var tenant = new Tenant
        {
            FullName = $"Event location privacy {suffix}",
            Slug = $"elp-{suffix}-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"elp-{suffix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Event",
                LastName = "Owner"
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = $"ELP {suffix}" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null!,
            Title = $"ELP {suffix}",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            Description = "Current-model persistence verification",
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            TotalViews = 0
        };
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            FullName = $"Venue {suffix}",
            Country = "BE",
            City = "Brussels"
        };
        context.AddRange(eventEntity, location);
        await context.SaveChangesAsync();

        EventLocation eventLocation = EventLocation.CreatePhysical(
            tenant.Id,
            eventEntity.Id,
            location.Id,
            user.Id,
            DateTime.UtcNow);
        await new EventLocationRepository(context).AddAsync(eventLocation, CancellationToken.None);

        return new EventLocationGraph(
            tenant.Id,
            eventEntity.Id,
            location.Id,
            eventLocation.Id,
            user.Id);
    }

    private static async Task EnsureLocationPrivacyLookupsAsync(ExploreDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO location_kinds (id, master_code, full_name) VALUES
                (1, 'UNCLASSIFIED', 'Unclassified'),
                (2, 'COMMERCIAL_VENUE', 'Commercial venue'),
                (3, 'PUBLIC_SPACE', 'Public space'),
                (4, 'COMMUNITY_VENUE', 'Community venue'),
                (5, 'PRIVATE_HOME', 'Private home')
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO location_privacy_states (id, master_code, full_name) VALUES
                (1, 'NOT_PROVIDED', 'Not provided'),
                (2, 'ACTIVE', 'Active'),
                (3, 'ERASED', 'Erased')
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO location_disclosure_audiences (id, master_code, full_name) VALUES
                (1, 'NEVER', 'Never'),
                (2, 'ANY_CURRENT_REGISTRANT', 'Any current registrant'),
                (3, 'CONFIRMED_PARTICIPANT', 'Confirmed participant')
            ON CONFLICT (id) DO NOTHING;
            """);
    }

    private static Task ClearPrivacyStateAsync(ExploreDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM event_session_agenda_items WHERE event_location_id IS NOT NULL;
            DELETE FROM event_agenda_items WHERE event_location_id IS NOT NULL;
            DELETE FROM event_session_groups WHERE event_location_id IS NOT NULL;
            DELETE FROM event_sessions WHERE event_location_id IS NOT NULL;
            TRUNCATE TABLE event_location_disclosure_audits;
            TRUNCATE TABLE event_location_exact_read_audits;
            TRUNCATE TABLE privacy_erasure_replay_checkpoints;
            DELETE FROM event_locations;
            """);

    private static EventLocationDisclosureAudit CreatePolicyAudit(
        EventLocationGraph graph,
        int previousVersion,
        int newVersion) =>
        EventLocationDisclosureAudit.Create(
            graph.TenantId,
            graph.EventLocationId,
            graph.ActorUserId,
            EventLocationDisclosureFields.None,
            EventLocationDisclosureFields.Country,
            LocationDisclosureAudienceEnum.Never,
            LocationDisclosureAudienceEnum.Never,
            null,
            null,
            previousVersion,
            newVersion,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            DateTime.UtcNow);

    private static EventLocationExactReadAudit CreateExactReadAudit(EventLocationGraph graph) =>
        EventLocationExactReadAudit.Create(
            graph.TenantId,
            graph.EventLocationId,
            graph.ActorUserId,
            EventLocationExactReadPurposeEnum.EventManagement,
            true,
            DateTime.UtcNow,
            Guid.CreateVersion7(),
            null);

    private static async Task<Exception?> PersistPolicyConcurrentlyAsync(
        ExploreDbContext context,
        EventLocationDisclosureAudit audit,
        Task gate)
    {
        await gate;
        try
        {
            await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(
                ct => new EventLocationDisclosureAuditRepository(context).AppendAsync(audit, ct),
                CancellationToken.None);
            return null;
        }
        catch (ConcurrencyConflictException exception)
        {
            return exception;
        }
    }

    private ExploreDbContext CreateTenantlessContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ExploreDbContext(options);
    }

    private static PrivacyErasureIntent CreateAuthorityIntent(long sequence)
    {
        DateTime now = DateTime.UtcNow;
        return PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            sequence,
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            now,
            now);
    }

    private static EventLocationDisclosureFields GetDisclosureFields(EventLocation eventLocation)
    {
        EventLocationDisclosureFields fields = EventLocationDisclosureFields.None;
        fields |= eventLocation.ShowVenueName ? EventLocationDisclosureFields.VenueName : EventLocationDisclosureFields.None;
        fields |= eventLocation.ShowCity ? EventLocationDisclosureFields.City : EventLocationDisclosureFields.None;
        fields |= eventLocation.ShowCountry ? EventLocationDisclosureFields.Country : EventLocationDisclosureFields.None;
        fields |= eventLocation.ShowRoomName ? EventLocationDisclosureFields.RoomName : EventLocationDisclosureFields.None;
        fields |= eventLocation.ShowStreetAddress ? EventLocationDisclosureFields.StreetAddress : EventLocationDisclosureFields.None;
        fields |= eventLocation.ShowPostcode ? EventLocationDisclosureFields.Postcode : EventLocationDisclosureFields.None;
        fields |= eventLocation.ShowCoordinates ? EventLocationDisclosureFields.Coordinates : EventLocationDisclosureFields.None;
        return fields;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record EventLocationGraph(
        Guid TenantId,
        Guid EventId,
        Guid LocationId,
        Guid EventLocationId,
        Guid ActorUserId);
}
