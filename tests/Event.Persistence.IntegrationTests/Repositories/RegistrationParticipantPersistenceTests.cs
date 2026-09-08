using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
[Category("Phase62Participants")]
public sealed class RegistrationParticipantPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ParticipantErasure_UnlinksSubjectAndHardDeletesPiiWhilePreservingUnrelatedParticipant()
    {
        SeededOrder seed = await SeedAsync(1, 1);
        await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(seed.TenantId));
        var unrelatedUser = new User
        {
            Pii = new UserPii
            {
                Email = $"unrelated-{Guid.NewGuid():N}@example.test",
                FirstName = "Unrelated",
                LastName = "Participant"
            }
        };
        context.Users.Add(unrelatedUser);
        await context.SaveChangesAsync();

        RegistrationParticipant subjectParticipant = RegistrationParticipant.Create(
            seed.TenantId, seed.OrderId, seed.UserId, ParticipantTypeEnum.Adult, null);
        subjectParticipant.SetPii(RegistrationParticipantPii.Create(
            subjectParticipant.Id, seed.TenantId, "Subject participant", "subject@example.test", "+32000000001"));
        RegistrationParticipant unrelatedParticipant = RegistrationParticipant.Create(
            seed.TenantId, seed.OrderId, unrelatedUser.Id, ParticipantTypeEnum.Adult, null);
        unrelatedParticipant.SetPii(RegistrationParticipantPii.Create(
            unrelatedParticipant.Id, seed.TenantId, "Unrelated participant", "unrelated@example.test", "+32000000002"));
        context.AddRange(subjectParticipant, unrelatedParticipant);
        await context.SaveChangesAsync();

        var repository = new UserLocationPrivacyErasureRepository(context);
        await repository.EraseRegistrationAndLocalNotificationsAsync(seed.UserId, CancellationToken.None);
        context.ChangeTracker.Clear();

        RegistrationParticipant erased = await context.RegistrationParticipants
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(value => value.Id == subjectParticipant.Id);
        RegistrationParticipant unaffected = await context.RegistrationParticipants
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(value => value.Id == unrelatedParticipant.Id);
        bool erasedPiiExists = await context.RegistrationParticipantPii
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .AnyAsync(value => value.RegistrationParticipantId == subjectParticipant.Id);
        bool unrelatedPiiExists = await context.RegistrationParticipantPii
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .AnyAsync(value => value.RegistrationParticipantId == unrelatedParticipant.Id);

        await Assert.That(erased.LinkedUserId).IsNull();
        await Assert.That(erasedPiiExists).IsFalse();
        await Assert.That(unaffected.LinkedUserId).IsEqualTo(unrelatedUser.Id);
        await Assert.That(unrelatedPiiExists).IsTrue();
    }

    [Test]
    public async Task ParticipantLinkage_RejectsAssignmentAndAdmissionFromAnotherOrderInTheSameTenant()
    {
        SeededOrder seed = await SeedAsync(1, 1);
        await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(seed.TenantId));
        RegistrationParticipant firstOrderParticipant = RegistrationParticipant.Create(
            seed.TenantId, seed.OrderId, seed.UserId, ParticipantTypeEnum.Adult, null);
        context.RegistrationParticipants.Add(firstOrderParticipant);
        await context.SaveChangesAsync();

        context.RegistrationTicketAssignments.Add(RegistrationTicketAssignment.Create(
            seed.TenantId,
            seed.OtherOrderId,
            seed.OtherLineId,
            1,
            firstOrderParticipant.Id,
            AssignmentStatusEnum.Assigned,
            null,
            DateTime.UtcNow));
        DbUpdateException crossOrder = (await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>())!;
        await Assert.That(FindPostgresException(crossOrder).SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);

        context.ChangeTracker.Clear();
        EventRegistration admission = Admission(seed, firstOrderParticipant, seed.SessionIds[0]);
        admission.RegistrationOrderId = seed.OtherOrderId;
        admission.RegistrationOrderLineId = seed.OtherLineId;
        admission.RegistrationParticipant = null!;
        context.EventRegistrations.Add(admission);
        DbUpdateException crossOrderAdmission = (await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>())!;
        await Assert.That(FindPostgresException(crossOrderAdmission).SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Test]
    public async Task AdmissionParticipantSessionUniqueness_AllowsDifferentSessionsAndRejectsDuplicate()
    {
        SeededOrder seed = await SeedAsync(1, 1);
        await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(seed.TenantId));
        RegistrationParticipant participant = RegistrationParticipant.Create(
            seed.TenantId, seed.OrderId, seed.UserId, ParticipantTypeEnum.Adult, null);
        context.RegistrationParticipants.Add(participant);
        context.EventRegistrations.AddRange(
            Admission(seed, participant, seed.SessionIds[0]),
            Admission(seed, participant, seed.SessionIds[1]));
        await context.SaveChangesAsync();

        context.EventRegistrations.Add(Admission(seed, participant, seed.SessionIds[0]));
        DbUpdateException duplicate = (await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>())!;
        await Assert.That(FindPostgresException(duplicate).SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
    }

    [Test]
    public async Task AssignmentSlots_AllowUnnamedParticipantsAndRejectDuplicateLineOrdinal()
    {
        SeededOrder seed = await SeedAsync(1, 1);
        await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(seed.TenantId));
        context.RegistrationTicketAssignments.Add(RegistrationTicketAssignment.Create(
            seed.TenantId, seed.OrderId, seed.LineIds[0], 1, null, AssignmentStatusEnum.Unassigned, null, DateTime.UtcNow));
        context.RegistrationTicketAssignments.Add(RegistrationTicketAssignment.Create(
            seed.TenantId, seed.OrderId, seed.LineIds[1], 1, null, AssignmentStatusEnum.Unassigned, null, DateTime.UtcNow));
        await context.SaveChangesAsync();

        context.RegistrationTicketAssignments.Add(RegistrationTicketAssignment.Create(
            seed.TenantId, seed.OrderId, seed.LineIds[0], 1, null, AssignmentStatusEnum.Unassigned, null, DateTime.UtcNow));
        DbUpdateException duplicate = (await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>())!;
        await Assert.That(FindPostgresException(duplicate).SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
    }

    [Test]
    public async Task ConcurrentAssignmentInsertions_AllowOnlyOneWriterPerLineOrdinal()
    {
        SeededOrder seed = await SeedAsync(1, 1);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int readyCount = 0;

        async Task<Exception?> InsertAsync(int ordinal)
        {
            await using ExploreDbContext context = fixture.CreateTenantFilteredDbContext(new TestTenantContext(seed.TenantId));
            context.RegistrationTicketAssignments.Add(RegistrationTicketAssignment.Create(
                seed.TenantId, seed.OrderId, seed.LineIds[0], ordinal, null, AssignmentStatusEnum.Unassigned, null, DateTime.UtcNow));
            if (Interlocked.Increment(ref readyCount) == 2)
            {
                ready.TrySetResult();
            }

            await release.Task.WaitAsync(timeout.Token);
            try
            {
                await context.SaveChangesAsync(timeout.Token);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        Task<Exception?> first = InsertAsync(1);
        Task<Exception?> second = InsertAsync(1);
        await ready.Task.WaitAsync(timeout.Token);
        release.TrySetResult();
        Exception?[] results = await Task.WhenAll(first, second);

        await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
        await Assert.That(FindPostgresException(results.Single(result => result is not null)!).SqlState)
            .IsEqualTo(PostgresErrorCodes.UniqueViolation);
    }

    private async Task<SeededOrder> SeedAsync(int firstQuantity, int secondQuantity)
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        TenantStatus activeStatus = await context.TenantStatuses.SingleAsync(status => status.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant { FullName = "Participant persistence tenant", Slug = $"participants-{Guid.NewGuid():N}", TenantStatusId = activeStatus.Id, TenantStatus = activeStatus };
        var user = new User { Pii = new UserPii { Email = $"participant-{Guid.NewGuid():N}@example.test", FirstName = "Participant", LastName = "Owner" } };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor { Pii = new ActorPii { DisplayName = "Participant Owner" }, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var eventEntity = new DomainEvent(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Participant persistence event",
            Subtitle = "",
            Description = "",
            FirstSessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            LastSessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            EventTypeId = 1,
            AudienceGenderId = 1,
            AudienceAgeId = 1,
            ActorId = actor.Id,
            Actor = null!,
            OrganizerActorId = actor.Id,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            TotalViews = 0
        };
        var sessions = new[]
        {
            new EventSession { Id = Guid.CreateVersion7(), EventId = eventEntity.Id, Event = null!, TenantId = tenant.Id, Tenant = tenant, Title = "One" },
            new EventSession { Id = Guid.CreateVersion7(), EventId = eventEntity.Id, Event = null!, TenantId = tenant.Id, Tenant = tenant, Title = "Two" }
        };
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenant.Id, eventEntity.Id, "USD", 1);
        EventTicketType firstTicket = FreeTicket(tenant.Id, catalog.Id, "Tier A");
        EventTicketType secondTicket = FreeTicket(tenant.Id, catalog.Id, "Tier B");
        catalog.AddTicketType(firstTicket, null);
        catalog.AddTicketType(secondTicket, null);
        catalog.AddEntitlement(firstTicket, TicketTypeEntitlement.CreateForEvent(firstTicket.Id, tenant.Id, eventEntity.Id, 1));
        catalog.AddEntitlement(secondTicket, TicketTypeEntitlement.CreateForEvent(secondTicket.Id, tenant.Id, eventEntity.Id, 1));
        catalog.Publish();
        RegistrationOrder order = RegistrationOrder.Create(
            tenant.Id, eventEntity.Id, user.Id, actor.Id, BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null), null, null, "USD", DateTime.UtcNow, null);
        RegistrationOrderLine firstLine = RegistrationOrderLine.Create(catalog, firstTicket, order.Id, firstQuantity, null, null);
        RegistrationOrderLine secondLine = RegistrationOrderLine.Create(catalog, secondTicket, order.Id, secondQuantity, null, null);
        order.AddLine(firstLine);
        order.AddLine(secondLine);
        RegistrationOrder otherOrder = RegistrationOrder.Create(
            tenant.Id, eventEntity.Id, user.Id, actor.Id, BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null), null, null, "USD", DateTime.UtcNow, null);
        RegistrationOrderLine otherLine = RegistrationOrderLine.Create(catalog, firstTicket, otherOrder.Id, 1, null, null);
        otherOrder.AddLine(otherLine);
        context.AddRange(eventEntity, sessions[0], sessions[1], catalog, order, otherOrder);
        await context.SaveChangesAsync();
        return new SeededOrder(
            tenant.Id,
            user.Id,
            eventEntity.Id,
            order.Id,
            otherOrder.Id,
            otherLine.Id,
            [firstLine.Id, secondLine.Id],
            [sessions[0].Id, sessions[1].Id]);
    }

    private static EventTicketType FreeTicket(Guid tenantId, Guid catalogId, string name) => EventTicketType.Create(
        Guid.CreateVersion7(), tenantId, catalogId, name, "USD", TicketPricingModeEnum.Free, null, null, null,
        ParticipantDataCollectionModeEnum.None, null, null, null, false, false, null, null, null, null);

    private static EventRegistration Admission(SeededOrder seed, RegistrationParticipant participant, Guid sessionId) => new()
    {
        Id = Guid.CreateVersion7(),
        ConcurrencyStamp = Guid.CreateVersion7(),
        TenantId = seed.TenantId,
        Tenant = null!,
        EventId = seed.EventId,
        Event = null!,
        EventSessionId = sessionId,
        EventSession = null!,
        RegistrationOrderId = seed.OrderId,
        RegistrationParticipantId = participant.Id,
        RegistrationParticipant = participant,
        LinkedUserId = seed.UserId,
        ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
        CoverageEstablishedAt = DateTime.UtcNow
    };

    private static PostgresException FindPostgresException(Exception exception) =>
        exception.GetBaseException() as PostgresException
        ?? throw new InvalidOperationException("Expected a PostgreSQL constraint failure.", exception);

    private sealed record SeededOrder(
        Guid TenantId,
        Guid UserId,
        Guid EventId,
        Guid OrderId,
        Guid OtherOrderId,
        Guid OtherLineId,
        Guid[] LineIds,
        Guid[] SessionIds);
    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
