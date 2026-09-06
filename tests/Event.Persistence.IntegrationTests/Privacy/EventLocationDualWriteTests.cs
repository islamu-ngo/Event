// ABOUTME: Real PostgreSQL acceptance tests for EventLocation dual-write carrier behavior.
// ABOUTME: Proves races, physical-key integrity, final detach, fresh reattach, moves, and rollback.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Privacy;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
[Category("EventLocationPrivacy")]
public sealed class EventLocationDualWriteTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 11, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AttachSecondReferenceDetachFinalAndReattachPersistsExactCarrierAndPolicyState()
    {
        await fixture.ResetAsync();
        DualWriteGraph graph = await SeedGraphAsync();
        await using var context = CreateTenantContext(graph.TenantId);
        var service = CreateService(context, graph);
        var unitOfWork = new EfCoreUnitOfWork(context);

        EventLocation placement = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation resolved = await service.ResolveAsync(
                graph.EventOneId, graph.LocationOneId, null, token);
            EventSession session = CreateSession(graph, graph.EventOneId);
            session.AssignEventLocation(resolved);
            session.RoomId = graph.RoomOneId;
            EventSessionGroup group = CreateGroup(graph, graph.EventOneId);
            group.AssignEventLocation(resolved);
            group.RoomId = graph.RoomOneId;
            EventAgendaItem agenda = CreateAgenda(graph, graph.EventOneId);
            agenda.AssignEventLocation(resolved);
            agenda.RoomId = graph.RoomOneId;
            EventSessionAgendaItem sessionAgenda = CreateSessionAgenda(graph, session);
            sessionAgenda.AssignEventLocation(resolved);
            context.AddRange(session, group, agenda, sessionAgenda);
            await context.SaveChangesAsync(token);
            return resolved;
        }, CancellationToken.None);

        EventSessionGroup groupReference = await context.EventSessionGroups.SingleAsync();
        await DeleteCarrierAsync(context, groupReference, placement.Id, service, unitOfWork);
        await Assert.That(placement.IsDeleted).IsFalse();

        EventAgendaItem agendaReference = await context.EventAgendaItems.SingleAsync();
        await DeleteCarrierAsync(context, agendaReference, placement.Id, service, unitOfWork);
        await Assert.That(placement.IsDeleted).IsFalse();

        EventSessionAgendaItem sessionAgendaReference = await context.EventSessionAgendaItems.SingleAsync();
        await DeleteCarrierAsync(context, sessionAgendaReference, placement.Id, service, unitOfWork);
        await Assert.That(placement.IsDeleted).IsFalse();

        EventSession sessionReference = await context.EventSessions.SingleAsync();
        await DeleteCarrierAsync(context, sessionReference, placement.Id, service, unitOfWork);
        await Assert.That(placement.IsDeleted).IsTrue();

        EventLocation replacement = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation resolved = await service.ResolveAsync(
                graph.EventOneId, graph.LocationOneId, placement.Id, token);
            EventSession session = CreateSession(graph, graph.EventOneId);
            session.AssignEventLocation(resolved);
            session.RoomId = graph.RoomOneId;
            context.EventSessions.Add(session);
            await context.SaveChangesAsync(token);
            return resolved;
        }, CancellationToken.None);

        context.ChangeTracker.Clear();
        EventLocation[] history = await context.EventLocations
            .IgnoreQueryFilters()
            .Where(item => item.EventId == graph.EventOneId && item.LocationId == graph.LocationOneId)
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync();
        EventSession activeCarrier = await context.EventSessions.SingleAsync();
        EventLocationDisclosureAudit[] audits = await context.EventLocationDisclosureAudits
            .Where(item => history.Select(location => location.Id).Contains(item.EventLocationId))
            .OrderBy(item => item.OccurredAtUtc)
            .ToArrayAsync();

        await Assert.That(history.Length).IsEqualTo(2);
        await Assert.That(history.Single(item => item.Id == placement.Id).IsDeleted).IsTrue();
        await Assert.That(history.Single(item => item.Id == replacement.Id).IsDeleted).IsFalse();
        await Assert.That(replacement.Id).IsNotEqualTo(placement.Id);
        await Assert.That(replacement.PolicyVersion).IsEqualTo(1);
        await Assert.That(activeCarrier.EventLocationId).IsEqualTo(replacement.Id);
        await Assert.That(activeCarrier.LocationId).IsEqualTo(graph.LocationOneId);
        await Assert.That(activeCarrier.RoomId).IsEqualTo(graph.RoomOneId);
        await Assert.That(audits.Select(item => item.EventLocationId))
            .IsEquivalentTo([placement.Id, replacement.Id]);
        await AssertZeroCarrierGapsAsync();
    }

    [Test]
    public async Task TbaUsesExclusiveShapeAndDualWritesNullPhysicalKey()
    {
        await fixture.ResetAsync();
        DualWriteGraph graph = await SeedGraphAsync();
        await using var context = CreateTenantContext(graph.TenantId);
        var service = CreateService(context, graph);
        EventLocation tba = await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async token =>
        {
            EventLocation resolved = await service.ResolveAsync(graph.EventOneId, null, null, token);
            EventSession session = CreateSession(graph, graph.EventOneId);
            session.AssignEventLocation(resolved);
            context.EventSessions.Add(session);
            await context.SaveChangesAsync(token);
            return resolved;
        }, CancellationToken.None);

        context.ChangeTracker.Clear();
        EventSession carrier = await context.EventSessions.SingleAsync();
        await Assert.That(tba.IsToBeAnnounced).IsTrue();
        await Assert.That(tba.LocationId).IsNull();
        await Assert.That(tba.HasValidLocationOrTbaShape).IsTrue();
        await Assert.That(carrier.EventLocationId).IsEqualTo(tba.Id);
        await Assert.That(carrier.LocationId).IsNull();
        await Assert.That(carrier.RoomId).IsNull();

        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(
            $"UPDATE event_locations SET location_id = {graph.LocationOneId} WHERE id = {tba.Id}"));
    }

    [Test]
    public async Task SamePhysicalLocationAcrossEventsCreatesIndependentActivePoliciesAndMoveDetachesOld()
    {
        await fixture.ResetAsync();
        DualWriteGraph graph = await SeedGraphAsync();
        await using var context = CreateTenantContext(graph.TenantId);
        var service = CreateService(context, graph);
        var unitOfWork = new EfCoreUnitOfWork(context);
        EventSession session = CreateSession(graph, graph.EventOneId);
        EventAgendaItem agendaItem = CreateAgenda(graph, graph.EventOneId);
        EventLocation first = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation resolved = await service.ResolveAsync(
                graph.EventOneId, graph.LocationOneId, null, token);
            session.AssignEventLocation(resolved);
            agendaItem.AssignEventLocation(resolved);
            context.AddRange(session, agendaItem);
            await context.SaveChangesAsync(token);
            return resolved;
        }, CancellationToken.None);

        EventLocation second = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation resolved = await service.ResolveAsync(
                graph.EventTwoId, graph.LocationOneId, first.Id, token);
            var repository = new EventSessionRepository(context);
            await repository.MoveToEventAsync(session, graph.EventTwoId, resolved, null, token);
            await repository.UpdateWithRoomOverlapGuardAsync(session, token);
            var agendaRepository = new EventAgendaItemRepository(context);
            await agendaRepository.MoveToEventAsync(agendaItem, graph.EventTwoId, resolved, null, token);
            await agendaRepository.Update(agendaItem);
            await service.DetachIfUnreferencedAsync(first.Id, token);
            return resolved;
        }, CancellationToken.None);

        context.ChangeTracker.Clear();
        EventSession moved = await context.EventSessions.SingleAsync();
        EventAgendaItem movedAgenda = await context.EventAgendaItems.SingleAsync();
        EventLocation oldPlacement = await context.EventLocations
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == first.Id);
        EventLocation newPlacement = await context.EventLocations.SingleAsync(item => item.Id == second.Id);
        await Assert.That(second.Id).IsNotEqualTo(first.Id);
        await Assert.That(oldPlacement.IsDeleted).IsTrue();
        await Assert.That(newPlacement.IsDeleted).IsFalse();
        await Assert.That(newPlacement.EventId).IsEqualTo(graph.EventTwoId);
        await Assert.That(newPlacement.LocationId).IsEqualTo(graph.LocationOneId);
        await Assert.That(moved.EventId).IsEqualTo(graph.EventTwoId);
        await Assert.That(moved.EventLocationId).IsEqualTo(second.Id);
        await Assert.That(moved.LocationId).IsEqualTo(graph.LocationOneId);
        await Assert.That(movedAgenda.EventId).IsEqualTo(graph.EventTwoId);
        await Assert.That(movedAgenda.EventLocationId).IsEqualTo(second.Id);
        await Assert.That(movedAgenda.LocationId).IsEqualTo(graph.LocationOneId);
        await AssertZeroCarrierGapsAsync();
    }

    [Test]
    public async Task ConcurrentDuplicatePhysicalPairReturnsOneAssociationAndOneInitialAudit()
    {
        await fixture.ResetAsync();
        DualWriteGraph graph = await SeedGraphAsync();
        await using var firstContext = CreateTenantContext(graph.TenantId);
        await using var secondContext = CreateTenantContext(graph.TenantId);
        var firstRepository = new EventLocationRepository(firstContext);
        var secondRepository = new EventLocationRepository(secondContext);
        EventLocation firstCandidate = EventLocation.CreatePhysical(
            graph.TenantId, graph.EventOneId, graph.LocationOneId, graph.ActorUserId, Now.UtcDateTime);
        EventLocation secondCandidate = EventLocation.CreatePhysical(
            graph.TenantId, graph.EventOneId, graph.LocationOneId, graph.ActorUserId, Now.UtcDateTime.AddMilliseconds(1));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<EventLocation> first = AddAfterGateAsync(firstRepository, firstCandidate, gate.Task);
        Task<EventLocation> second = AddAfterGateAsync(secondRepository, secondCandidate, gate.Task);
        gate.SetResult();
        EventLocation[] results = await Task.WhenAll(first, second);

        await using var verifyContext = fixture.CreateDbContext();
        EventLocation[] active = await verifyContext.EventLocations
            .Where(item => item.EventId == graph.EventOneId && item.LocationId == graph.LocationOneId)
            .ToArrayAsync();
        int auditCount = await verifyContext.EventLocationDisclosureAudits
            .CountAsync(item => item.EventLocationId == active.Single().Id);
        await Assert.That(results.Select(item => item.Id).Distinct().Count()).IsEqualTo(1);
        await Assert.That(active.Length).IsEqualTo(1);
        await Assert.That(auditCount).IsEqualTo(1);
    }

    [Test]
    public async Task RoomLocationAndCrossEventOrTenantMismatchesFailClosedWithoutPartialWrites()
    {
        await fixture.ResetAsync();
        DualWriteGraph graph = await SeedGraphAsync();
        await using var context = CreateTenantContext(graph.TenantId);
        var service = CreateService(context, graph);
        var unitOfWork = new EfCoreUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation placement = await service.ResolveAsync(
                graph.EventOneId, graph.LocationOneId, null, token);
            EventSession session = CreateSession(graph, graph.EventOneId);
            session.AssignEventLocation(placement);
            session.RoomId = graph.RoomTwoId;
            context.EventSessions.Add(session);
            await context.SaveChangesAsync(token);
        }, CancellationToken.None));

        context.ChangeTracker.Clear();
        await Assert.That(await context.EventSessions.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EventLocations.CountAsync()).IsEqualTo(0);

        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await service.ResolveAsync(graph.CrossTenantEventId, graph.LocationOneId, null, token);
        }, CancellationToken.None));

        context.ChangeTracker.Clear();
        await Assert.That(await context.EventLocations.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EventLocationDisclosureAudits.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task FailedCarrierTransactionRollsBackAssociationAuditAndPhysicalKey()
    {
        await fixture.ResetAsync();
        DualWriteGraph graph = await SeedGraphAsync();
        await using var context = CreateTenantContext(graph.TenantId);
        var service = CreateService(context, graph);
        var unitOfWork = new EfCoreUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation placement = await service.ResolveAsync(
                graph.EventOneId, graph.LocationOneId, null, token);
            EventSession session = CreateSession(graph, graph.EventOneId);
            session.AssignEventLocation(placement);
            context.EventSessions.Add(session);
            await context.SaveChangesAsync(token);
            throw new InvalidOperationException("force rollback");
        }, CancellationToken.None));

        await using var verifyContext = fixture.CreateDbContext();
        await Assert.That(await verifyContext.EventLocations.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.EventLocationDisclosureAudits.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.EventSessions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task MidTransactionCancellationRollsBackAuthorityAuditAndCarrier()
    {
        await fixture.ResetAsync();
        DualWriteGraph graph = await SeedGraphAsync();
        await using var context = CreateTenantContext(graph.TenantId);
        var service = CreateService(context, graph);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async token =>
            {
                EventLocation placement = await service.ResolveAsync(
                    graph.EventOneId,
                    graph.LocationOneId,
                    null,
                    token);
                EventSession session = CreateSession(graph, graph.EventOneId);
                session.AssignEventLocation(placement);
                context.EventSessions.Add(session);
                await context.SaveChangesAsync(token);
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            }, cancellation.Token));

        await using var verifyContext = fixture.CreateDbContext();
        await Assert.That(await verifyContext.EventLocations.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.EventLocationDisclosureAudits.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.EventSessions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConcurrentGroupAndSessionAgendaFinalDetachRetiresOrphanedAssociation(bool rejectGroupDelete)
    {
        await fixture.ResetAsync();
        DualWriteGraph graph = await SeedGraphAsync();
        Guid placementId;
        Guid groupId;
        Guid sessionAgendaId;
        await using (var seedContext = CreateTenantContext(graph.TenantId))
        {
            var service = CreateService(seedContext, graph);
            EventLocation placement = await new EfCoreUnitOfWork(seedContext).ExecuteInTransactionAsync(
                async token =>
                {
                    EventLocation resolved = await service.ResolveAsync(
                        graph.EventOneId,
                        graph.LocationOneId,
                        null,
                        token);
                    EventSession session = CreateSession(graph, graph.EventOneId);
                    EventSessionGroup group = CreateGroup(graph, graph.EventOneId);
                    group.AssignEventLocation(resolved);
                    EventSessionAgendaItem sessionAgenda = CreateSessionAgenda(graph, session);
                    sessionAgenda.AssignEventLocation(resolved);
                    seedContext.AddRange(session, group, sessionAgenda);
                    await seedContext.SaveChangesAsync(token);
                    return resolved;
                },
                CancellationToken.None);
            placementId = placement.Id;
            groupId = await seedContext.EventSessionGroups.Select(item => item.Id).SingleAsync();
            sessionAgendaId = await seedContext.EventSessionAgendaItems.Select(item => item.Id).SingleAsync();
        }

        var bothDeletesSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        int deleteCount = 0;
        Task DeleteGroupAsync() => DeleteAfterBarrierAsync<EventSessionGroup>(
            groupId,
            group => group.DetachEventLocationForDeletion());
        Task DeleteSessionAgendaAsync() => DeleteAfterBarrierAsync<EventSessionAgendaItem>(
            sessionAgendaId,
            _ => { });

        async Task DeleteAfterBarrierAsync<TCarrier>(Guid id, Action<TCarrier> prepareDelete)
            where TCarrier : class
        {
            await using var context = CreateTenantContext(graph.TenantId);
            var service = CreateService(context, graph);
            try
            {
                await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(async token =>
                {
                    TCarrier carrier = await context.Set<TCarrier>().SingleAsync(
                        item => EF.Property<Guid>(item, "Id") == id,
                        token);
                    prepareDelete(carrier);
                    context.Remove(carrier);
                    await context.SaveChangesAsync(token);
                    if (rejectGroupDelete && typeof(TCarrier) == typeof(EventSessionGroup))
                    {
                        throw new InvalidOperationException("Synthetic detach failure.");
                    }
                    if (Interlocked.Increment(ref deleteCount) == 2)
                    {
                        bothDeletesSaved.TrySetResult();
                    }

                    await bothDeletesSaved.Task.WaitAsync(token);
                    await service.DetachIfUnreferencedAsync(placementId, token);
                }, cancellation.Token);
            }
            catch (Exception exception)
            {
                bothDeletesSaved.TrySetException(exception);
                throw;
            }
        }

        if (rejectGroupDelete)
        {
            InvalidOperationException? failure = await Assert.That(async () => await Task.WhenAll(DeleteGroupAsync(), DeleteSessionAgendaAsync()))
                .Throws<InvalidOperationException>();
            await Assert.That(failure!.Message).IsEqualTo("Synthetic detach failure.");
            await using var retainedContext = CreateTenantContext(graph.TenantId);
            EventSessionGroup retainedGroup = await retainedContext.EventSessionGroups.SingleAsync(item => item.Id == groupId);
            EventSessionAgendaItem retainedAgenda = await retainedContext.EventSessionAgendaItems.SingleAsync(item => item.Id == sessionAgendaId);
            await Assert.That(retainedGroup.EventLocationId).IsEqualTo(placementId);
            await Assert.That(retainedGroup.LocationId).IsEqualTo(graph.LocationOneId);
            await Assert.That(retainedAgenda.EventLocationId).IsEqualTo(placementId);
            await Assert.That(retainedAgenda.LocationId).IsEqualTo(graph.LocationOneId);
            await Assert.That(await retainedContext.EventLocations.AnyAsync(item => item.Id == placementId)).IsTrue();
            return;
        }

        await Task.WhenAll(DeleteGroupAsync(), DeleteSessionAgendaAsync());

        await using var verifyContext = fixture.CreateDbContext();
        EventLocation placementAfterRace = await verifyContext.EventLocations
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == placementId);
        await Assert.That(placementAfterRace.IsDeleted).IsTrue();
        await Assert.That(await verifyContext.EventSessionGroups.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.EventSessionAgendaItems.CountAsync()).IsEqualTo(0);
        await Assert.That(await verifyContext.EventLocations.CountAsync()).IsEqualTo(0);
    }

    private async Task<DualWriteGraph> SeedGraphAsync()
    {
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "ELP dual-write tenant",
            Slug = $"elp-dual-write-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var otherTenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "ELP cross-tenant tenant",
            Slug = $"elp-cross-tenant-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"elp-dual-write-{Guid.NewGuid():N}@example.com",
                FirstName = "Dual",
                LastName = "Writer"
            }
        };
        context.AddRange(tenant, otherTenant, user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "ELP dual writer" }
        };
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            FullName = "ELP cross-tenant group",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var otherActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            GroupId = group.Id,
            Group = group,
            ActorTypeId = (int)ActorTypeEnum.Group,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "ELP other actor" }
        };
        context.AddRange(actor, group, otherActor);
        await context.SaveChangesAsync();

        var eventOne = CreateEvent(tenant.Id, actor.Id, "ELP event one");
        var eventTwo = CreateEvent(tenant.Id, actor.Id, "ELP event two");
        var crossTenantEvent = CreateEvent(otherTenant.Id, otherActor.Id, "ELP cross tenant event");
        var locationOne = CreateLocation(tenant.Id, "ELP venue one");
        var locationTwo = CreateLocation(tenant.Id, "ELP venue two");
        var roomOne = CreateRoom(tenant.Id, locationOne.Id, "Room one");
        var roomTwo = CreateRoom(tenant.Id, locationTwo.Id, "Room two");
        context.AddRange(eventOne, eventTwo, crossTenantEvent, locationOne, locationTwo, roomOne, roomTwo);
        await context.SaveChangesAsync();

        return new DualWriteGraph(
            tenant.Id,
            user.Id,
            eventOne.Id,
            eventTwo.Id,
            crossTenantEvent.Id,
            locationOne.Id,
            locationTwo.Id,
            roomOne.Id,
            roomTwo.Id);
    }

    private ExploreDbContext CreateTenantContext(Guid tenantId) =>
        fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));

    private static EventLocationAttachmentService CreateService(
        ExploreDbContext context,
        DualWriteGraph graph) =>
        new(
            new EventLocationRepository(context),
            new TestUserContext(graph.ActorUserId),
            new TestTenantContext(graph.TenantId),
            new FixedTimeProvider(Now));

    private static Explore.Domain.Event CreateEvent(Guid tenantId, Guid actorId, string title) => new(EventStatusEnum.Draft)
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        ActorId = actorId,
        Actor = null!,
        Title = title,
        PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
        EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
        Description = "ELP dual-write PostgreSQL acceptance",
        EventStatus = null!,
        EventFormatId = (int)EventFormatEnum.Local,
        EventFormat = null!,
        VisibilityTypeId = (int)VisibilityTypeEnum.Private,
        VisibilityType = null!,
        TotalViews = 0
    };

    private static Location CreateLocation(Guid tenantId, string name) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        FullName = name,
        Country = "BE",
        City = "Brussels"
    };

    private static LocationRoom CreateRoom(Guid tenantId, Guid locationId, string name) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        LocationId = locationId,
        Location = null!,
        Name = name
    };

    private static EventSession CreateSession(DualWriteGraph graph, Guid eventId) => new(EventSessionStatusEnum.Draft)
    {
        Id = Guid.CreateVersion7(),
        TenantId = graph.TenantId,
        Tenant = null!,
        EventId = eventId,
        Event = null!,
        Title = $"ELP session {Guid.NewGuid():N}",
    };

    private static EventSessionGroup CreateGroup(DualWriteGraph graph, Guid eventId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = graph.TenantId,
        Tenant = null!,
        EventId = eventId,
        Event = null!,
        Name = $"ELP group {Guid.NewGuid():N}"
    };

    private static EventAgendaItem CreateAgenda(DualWriteGraph graph, Guid eventId)
    {
        var agenda = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.TenantId,
            Tenant = null!,
            EventId = eventId,
            Event = null!,
            Title = $"ELP agenda {Guid.NewGuid():N}"
        };
        agenda.Reschedule(UtcInstantRange.Create(Now, Now.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
        return agenda;
    }

    private static EventSessionAgendaItem CreateSessionAgenda(
        DualWriteGraph graph,
        EventSession session) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.TenantId,
            Tenant = null!,
            EventSessionId = session.Id,
            EventSession = session,
            Title = $"ELP session agenda {Guid.NewGuid():N}",
            StartTime = Now,
            EndTime = Now.AddMinutes(30)
        };

    private static async Task DeleteCarrierAsync<TCarrier>(
        ExploreDbContext context,
        TCarrier carrier,
        Guid eventLocationId,
        EventLocationAttachmentService service,
        EfCoreUnitOfWork unitOfWork)
        where TCarrier : class
    {
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            switch (carrier)
            {
                case EventSession session:
                    session.DetachEventLocationForDeletion();
                    break;
                case EventSessionGroup group:
                    group.DetachEventLocationForDeletion();
                    break;
                case EventAgendaItem agendaItem:
                    agendaItem.DetachEventLocationForDeletion();
                    break;
            }

            context.Remove(carrier);
            await context.SaveChangesAsync(token);
            await service.DetachIfUnreferencedAsync(eventLocationId, token);
        }, CancellationToken.None);

        if (carrier is EventSession or EventSessionGroup or EventAgendaItem)
        {
            var entry = context.Entry(carrier);
            await entry.ReloadAsync();
            await Assert.That(entry.Property<bool>(nameof(EventSession.IsDeleted)).CurrentValue).IsTrue();
            await Assert.That(entry.Property<Guid?>(nameof(EventSession.EventLocationId)).CurrentValue).IsNull();
            await Assert.That(entry.Property<Guid?>(nameof(EventSession.LocationId)).CurrentValue).IsNull();
            await Assert.That(entry.Property<Guid?>(nameof(EventSession.RoomId)).CurrentValue).IsNull();
        }
    }

    private async Task AssertZeroCarrierGapsAsync()
    {
        const string sql =
            """
            SELECT
                (SELECT count(*) FROM event_sessions c LEFT JOIN event_locations e ON e.id = c.event_location_id AND e.tenant_id = c.tenant_id WHERE c.is_deleted = false AND (c.event_location_id IS NULL OR e.id IS NULL OR e.event_id <> c.event_id OR e.location_id IS DISTINCT FROM c.location_id OR e.is_deleted))
              + (SELECT count(*) FROM event_session_groups c LEFT JOIN event_locations e ON e.id = c.event_location_id AND e.tenant_id = c.tenant_id WHERE c.is_deleted = false AND (c.event_location_id IS NULL OR e.id IS NULL OR e.event_id <> c.event_id OR e.location_id IS DISTINCT FROM c.location_id OR e.is_deleted))
              + (SELECT count(*) FROM event_agenda_items c LEFT JOIN event_locations e ON e.id = c.event_location_id AND e.tenant_id = c.tenant_id WHERE c.is_deleted = false AND (c.event_location_id IS NULL OR e.id IS NULL OR e.event_id <> c.event_id OR e.location_id IS DISTINCT FROM c.location_id OR e.is_deleted))
              + (SELECT count(*) FROM event_session_agenda_items c JOIN event_sessions s ON s.id = c.event_session_id AND s.tenant_id = c.tenant_id LEFT JOIN event_locations e ON e.id = c.event_location_id AND e.tenant_id = c.tenant_id WHERE c.event_location_id IS NULL OR e.id IS NULL OR e.event_id <> s.event_id OR e.location_id IS DISTINCT FROM c.location_id OR e.is_deleted)
            """;
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        long gaps = (long)(await command.ExecuteScalarAsync() ?? -1L);
        await Assert.That(gaps).IsEqualTo(0);
    }

    private static async Task<EventLocation> AddAfterGateAsync(
        EventLocationRepository repository,
        EventLocation candidate,
        Task gate)
    {
        await gate;
        return await repository.AddAsync(candidate, CancellationToken.None);
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record TestUserContext(Guid RequiredUserId) : IUserContext
    {
        public Guid? UserId => RequiredUserId;
        public string? Email => null;
        public string? Username => null;
        public bool IsAuthenticated => true;
        public Guid GetRequiredUserId() => RequiredUserId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record DualWriteGraph(
        Guid TenantId,
        Guid ActorUserId,
        Guid EventOneId,
        Guid EventTwoId,
        Guid CrossTenantEventId,
        Guid LocationOneId,
        Guid LocationTwoId,
        Guid RoomOneId,
        Guid RoomTwoId);
}
