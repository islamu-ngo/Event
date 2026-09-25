using System.Data.Common;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
public sealed partial class EventResourceAuthoritySnapshotPersistenceTests(
    EventResourcePersistenceTests.TestDatabase database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CommittedMembershipRevocationBeforeFinalReadUsesIndependentConnectionsAndIgnoresTrackedMembership()
    {
        var scope = await database.SeedScopeAsync();
        var subject = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"audience-{Guid.CreateVersion7():N}@example.test", FirstName = "Audience", LastName = "Member"
            }
        };
        var resourceId = Guid.CreateVersion7();
        await using (var seed = database.CreateContext())
        {
            var publisherId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            seed.Users.Add(subject);
            seed.TenantUsers.AddRange(
                new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                    UserId = publisherId, User = null!, ActorId = scope.ActorId,
                    StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
                },
                new TenantUser
                {
                    Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                    UserId = subject.Id, User = subject, StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
                });
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            if (parent.EventStatusId != (int)EventStatusEnum.Published) parent.Publish(Now);
            parent.VisibilityTypeId = (int)VisibilityTypeEnum.Public;
            var resource = EventResource.CreateDraft(resourceId, scope.TenantAId, scope.EventAId, null,
                new EventResourceMetadata
                {
                    Title = "Tenant member material", Kind = (EventResourceKindEnum)1,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resourceId,
                    EventResourceAudienceKindEnum.AuthenticatedTenantMember)], publisherId, Now);
            resource.SetStoredFile(scope.StorageAId, resource.ConcurrencyStamp, publisherId, Now);
            resource.Publish(new(scope.TenantAId, scope.EventAId, null, EventStatusEnum.Published,
                false, true, null, false, new(new(Now), new(Now.AddHours(1)), null, null)),
                true, resource.ConcurrencyStamp, publisherId, Now);
            seed.Add(resource);
            await seed.SaveChangesAsync();
        }

        await using var read = database.CreateIndependentContext();
        await using var writer = database.CreateIndependentContext();
        var trackedMembership = await read.TenantUsers.SingleAsync(value => value.UserId == subject.Id);
        bool independentConnections = !ReferenceEquals(read.Database.GetDbConnection(), writer.Database.GetDbConnection());
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IEventResourceAuthorizationProvider>();
        provider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            providerEntered.TrySetResult();
            await releaseProvider.Task.WaitAsync(call.ArgAt<CancellationToken>(1));
            return EventResourceProviderDecision.Allow;
        });
        var routes = Substitute.For<IEventResourceProviderSnapshotReader>();
        routes.ReadAsync(scope.TenantAId, Arg.Any<CancellationToken>())
            .Returns(new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "", "default"));
        var service = new EventResourceAuthorityOrchestrator(new EfCoreUnitOfWork(read),
            CreateReader(new EventResourceRepository(read), new EventAuthoritySnapshotService(read)),
            routes, provider, new AuthorityClock());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        AuthorityPreparation? preparation = null;
        var pending = service.AuthorizeAsync(new(scope.TenantAId, resourceId, subject.Id, false, "view",
            new DateTimeOffset(Now.AddMinutes(1))), (facts, _) =>
        {
            preparation = new(facts.AttachmentGeneration);
            return Task.FromResult<IEventResourcePrivatePreparation>(preparation);
        }, cancellation.Token);
        bool providerOutsideTransaction;
        try
        {
            await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation.Token);
            providerOutsideTransaction = read.Database.CurrentTransaction is null;
            var membership = await writer.TenantUsers.SingleAsync(value => value.UserId == subject.Id, cancellation.Token);
            membership.StatusId = (int)TenantUserStatusEnum.Suspended;
            await writer.SaveChangesAsync(cancellation.Token);
        }
        finally
        {
            releaseProvider.TrySetResult();
        }
        await using var result = await pending.WaitAsync(TimeSpan.FromSeconds(10), cancellation.Token);
        await Assert.That(independentConnections).IsTrue();
        await Assert.That(providerOutsideTransaction).IsTrue();
        await Assert.That(trackedMembership.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(preparation!.Disposed).IsTrue();
        await Assert.That(result.Lease).IsNull();
    }

    [Test]
    public async Task CheckedInAudienceRequiresTheExactTicketsCurrentSubjectEligibility()
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        Guid checkedTicketId;
        Guid checkedEligibilityId;
        Guid firstEligibilityId;
        Guid resourceId = Guid.CreateVersion7();
        EventResource resource;
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                UserId = userId, User = null!, ActorId = scope.ActorId,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
            });
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            if (parent.EventStatusId != (int)EventStatusEnum.Published) parent.Publish(Now);
            parent.VisibilityTypeId = (int)VisibilityTypeEnum.Public;
            var session = await seed.EventSessions.SingleAsync(value => value.Id == scope.SessionAId);
            session.StartTime = new DateTimeOffset(Now.AddMinutes(-5));
            session.EndTime = new DateTimeOffset(Now.AddHours(1));
            session.Publish(EventStatusEnum.Published, Now);
            var catalog = await seed.EventTicketCatalogVersions.Include(value => value.TicketTypes)
                .SingleAsync(value => value.Id == scope.CatalogAId);
            var type = catalog.TicketTypes.Single(value => value.Id == scope.TicketTypeAId);
            var entitlement = TicketTypeEntitlement.CreateForEvent(type.Id, scope.TenantAId, scope.EventAId, 1);
            catalog.AddEntitlement(type, entitlement);
            catalog.Publish();
            var order = RegistrationOrder.Create(scope.TenantAId, scope.EventAId, userId, scope.ActorId,
                BookingPartyTypeEnum.Individual, catalog.Id,
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
                null, null, "USD", Now, null);
            var line = RegistrationOrderLine.Create(catalog, type, order.Id, 2, null, null);
            order.AddLine(line);
            var participants = Enumerable.Range(0, 2).Select(_ => RegistrationParticipant.Create(
                scope.TenantAId, order.Id, userId, ParticipantTypeEnum.Adult, null)).ToArray();
            var assignments = participants.Select((participant, index) => RegistrationTicketAssignment.CreateAssigned(
                Guid.CreateVersion7(), line.Id, index + 1, participant, Now)).ToArray();
            for (int index = 0; index < participants.Length; index++)
            {
                order.AddParticipant(participants[index]);
                order.AddAssignment(line, assignments[index], participants[index]);
            }
            order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 0, 0, 0, 0));
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Now);
            order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, Now);
            order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, Now);
            var tickets = participants.Select((participant, index) => AdmissionTicket.Issue(
                order, line, assignments[index], participant, catalog, type, Guid.CreateVersion7(),
                $"RESOURCE-{index}", Guid.CreateVersion7(), 1, 1,
                Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                Now)).ToArray();
            var eligibility = participants.Select((participant, index) => ParticipantAdmissionEligibility.Create(
                scope.TenantAId, scope.EventAId, assignments[index], participant, false, false, Now)).ToArray();
            eligibility[0].RecordSubjectCompletion(participants[0], userId, null, Now, Guid.CreateVersion7());
            eligibility[0].Approve(scope.ActorId, Now, Guid.CreateVersion7());
            firstEligibilityId = eligibility[0].Id;
            checkedTicketId = tickets[1].Id;
            checkedEligibilityId = eligibility[1].Id;

            var target = await seed.AdmissionTargets.SingleAsync(value => value.Id == scope.TargetAId);
            var policy = AdmissionCheckInPolicy.Create(Guid.CreateVersion7(), target, Now.AddHours(-1), Now.AddHours(1), 1);
            var checkIn = AdmissionCheckInRules.Decide(tickets[1], target, entitlement, policy,
                AdmissionCheckInState.Create(Guid.CreateVersion7(), tickets[1], target),
                AdmissionCheckInActionEnum.CheckIn, Guid.CreateVersion7(), scope.ActorId, null, null, Now);
            await Assert.That(checkIn.Event).IsNotNull();
            var parentFacts = new EventResourceParentFacts(scope.TenantAId, scope.EventAId, null,
                EventStatusEnum.Published, false, true, null, false,
                new EventResourceScheduleFacts(new(Now), new(Now.AddHours(1)), null, null));
            resource = EventResource.CreateDraft(resourceId, scope.TenantAId, scope.EventAId, null,
                new EventResourceMetadata
                {
                    Title = "Checked-in participant material", Kind = (EventResourceKindEnum)1,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resourceId,
                    EventResourceAudienceKindEnum.CheckedInParticipant, ticketTypeId: type.Id,
                    targetType: AdmissionTargetTypeEnum.Event, targetId: target.Id,
                    ticketCatalogVersionId: catalog.Id, targetScopeId: target.ScopeId)], userId, Now);
            resource.SetStoredFile(scope.StorageAId, resource.ConcurrencyStamp, userId, Now);
            resource.Publish(parentFacts, true, resource.ConcurrencyStamp, userId, Now);
            seed.AddRange(order, policy, checkIn.Event!, checkIn.NextState, resource);
            seed.AddRange(tickets);
            seed.AddRange(eligibility);
            seed.EventRegistrations.AddRange(participants.Select(participant => new EventRegistration
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                EventId = scope.EventAId, Event = parent,
                EventSessionId = scope.SessionAId, EventSession = session,
                LinkedUserId = userId, RegistrationOrderId = order.Id,
                RegistrationOrderLineId = line.Id,
                RegistrationParticipantId = participant.Id, RegistrationParticipant = participant,
                CoverageEstablishedAt = Now, ConcurrencyStamp = Guid.CreateVersion7()
            }));
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var reader = CreateReader(
            new EventResourceRepository(context), new EventAuthoritySnapshotService(context));
        var request = new EventResourceAuthorityRequest(scope.TenantAId, resourceId, userId, false, "view");
        var before = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(before.Access.Audience.Any(value =>
            value.Kind == EventResourceAudienceKindEnum.TicketHolder)).IsTrue();
        await Assert.That(before.Access.Audience.Any(value =>
            value.Kind == EventResourceAudienceKindEnum.CheckedInParticipant)).IsFalse();
        await Assert.That(EventResourceAccessRules.Evaluate(resource, before.Access, new(Now)).DisclosePrivateMetadata).IsFalse();

        await using (var writer = database.CreateContext())
        {
            var exactEligibility = await writer.Set<ParticipantAdmissionEligibility>()
                .Include(value => value.Participant)
                .SingleAsync(value => value.Id == checkedEligibilityId);
            var participant = exactEligibility.Participant
                ?? throw new InvalidOperationException("The exact eligibility must retain its persisted participant.");
            exactEligibility.RecordSubjectCompletion(participant, userId, null, Now, Guid.CreateVersion7());
            await writer.SaveChangesAsync();
        }
        var approved = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(approved.Access.Audience.Single(value =>
            value.Kind == EventResourceAudienceKindEnum.CheckedInParticipant).ApprovedSubjectUserId).IsNull();
        await Assert.That(EventResourceAccessRules.Evaluate(resource, approved.Access, new(Now)).DisclosePrivateMetadata).IsTrue();

        await using (var writer = database.CreateContext())
        {
            await writer.Set<AdmissionCheckInState>().Where(value => value.AdmissionTicketId == checkedTicketId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ActiveCheckInEventId, (Guid?)null));
        }
        var reversed = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(EventResourceAccessRules.Evaluate(resource, reversed.Access, new(Now)).DisclosePrivateMetadata).IsFalse();

        await using (var writer = database.CreateContext())
        {
            var current = await writer.EventResources.Include(value => value.AudienceRules)
                .SingleAsync(value => value.Id == resourceId);
            current.ReplacePolicy(EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resourceId,
                    EventResourceAudienceKindEnum.SessionRegistrant, sessionId: scope.SessionAId,
                    requireApproval: true, requireCompletion: true)],
                current.ConcurrencyStamp, userId, Now);
            await writer.SaveChangesAsync();
            await writer.ParticipantAdmissionEligibilities.Where(value => value.Id == firstEligibilityId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.RequirementsCompletedAt, (DateTime?)null));
        }
        await using var currentContext = database.CreateContext();
        var registrant = await currentContext.EventResources.Include(value => value.AudienceRules)
            .SingleAsync(value => value.Id == resourceId);
        var split = (await reader.ReadAsync(request, new(Now), default))!;
        var splitRows = split.Access.Audience.Where(value =>
            value.Kind == EventResourceAudienceKindEnum.SessionRegistrant).ToArray();
        await Assert.That(splitRows.Length).IsEqualTo(2);
        await Assert.That(splitRows.Count(value => value.ApprovedSubjectUserId == userId
            && value.CompletedSubjectUserId is null)).IsEqualTo(1);
        await Assert.That(splitRows.Count(value => value.CompletedSubjectUserId == userId
            && value.ApprovedSubjectUserId is null)).IsEqualTo(1);
        await Assert.That(EventResourceAccessRules.Evaluate(registrant, split.Access, new(Now)).DisclosePrivateMetadata).IsFalse();

        await using (var writer = database.CreateContext())
            await writer.ParticipantAdmissionEligibilities.Where(value => value.Id == firstEligibilityId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.RequirementsCompletedAt, (DateTime?)Now));
        var united = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(EventResourceAccessRules.Evaluate(registrant, united.Access, new(Now)).DisclosePrivateMetadata).IsTrue();
        await using (var writer = database.CreateContext())
            await writer.ParticipantAdmissionEligibilities.Where(value => value.Id == firstEligibilityId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ApprovedAt, (DateTime?)null)
                    .SetProperty(value => value.ApprovedByActorId, (Guid?)null));
        var approvalRevoked = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(EventResourceAccessRules.Evaluate(registrant, approvalRevoked.Access, new(Now)).DisclosePrivateMetadata).IsFalse();
        await using (var writer = database.CreateContext())
            await writer.ParticipantAdmissionEligibilities.Where(value => value.Id == firstEligibilityId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ApprovedAt, (DateTime?)Now)
                    .SetProperty(value => value.ApprovedByActorId, (Guid?)scope.ActorId)
                    .SetProperty(value => value.RequirementsCompletedAt, (DateTime?)null));
        var completionRevoked = (await reader.ReadAsync(request, new(Now), default))!;
        await Assert.That(EventResourceAccessRules.Evaluate(registrant, completionRevoked.Access, new(Now)).DisclosePrivateMetadata).IsFalse();
    }

    [Test]
    public async Task AuthenticatedParentVisibilityCannotBypassSuspendedPublishingActor()
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            if (parent.EventStatusId != (int)EventStatusEnum.Published) parent.Publish(Now);
            parent.VisibilityTypeId = (int)VisibilityTypeEnum.Unlisted;
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                UserId = userId, User = null!, ActorId = scope.ActorId,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
            });
            seed.Add(resource);
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var reader = CreateReader(
            new EventResourceRepository(context), new EventAuthoritySnapshotService(context));
        var request = new EventResourceAuthorityRequest(scope.TenantAId, resource.Id, userId, false, "view");
        await Assert.That((await reader.ReadAsync(request, new(Now), default))!.Access.Parent.EventEligible).IsTrue();
        await using (var writer = database.CreateContext())
        {
            var actor = await writer.Actors.SingleAsync(value => value.Id == scope.ActorId);
            actor.IsSuspended = true;
            await writer.SaveChangesAsync();
        }
        await Assert.That((await reader.ReadAsync(request, new(Now), default))!.Access.Parent.EventEligible).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicOnlyCallerCannotBorrowAuthenticatedUnlistedParentVisibility(bool machine)
    {
        var scope = await database.SeedScopeAsync();
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        await using (var seed = database.CreateContext())
        {
            var userId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            seed.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                UserId = userId, User = null!, ActorId = scope.ActorId,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = Now
            });
            var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
            if (parent.EventStatusId != (int)EventStatusEnum.Published) parent.Publish(Now);
            parent.VisibilityTypeId = (int)VisibilityTypeEnum.Unlisted;
            seed.Add(resource);
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        var reader = CreateReader(
            new EventResourceRepository(context), new EventAuthoritySnapshotService(context));
        var facts = await reader.ReadAsync(
            new(scope.TenantAId, resource.Id, null, machine, "view"), new(Now), default);
        await Assert.That(facts!.Access.Parent.EventEligible).IsFalse();
    }

    [Test]
    public async Task StaffAudiencePreservesAssignmentExpiryForTheFinalClockGate()
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            var assignment = EventRoleAssignment.Create(scope.TenantAId, scope.EventAId, userId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active,
                Now.AddMinutes(-1), Now.AddSeconds(1), userId);
            seed.AddRange(assignment, resource);
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var reader = CreateReader(
            new EventResourceRepository(context), new EventAuthoritySnapshotService(context));
        var facts = (await reader.ReadAsync(
            new(scope.TenantAId, resource.Id, userId, false, "view", new DateTimeOffset(Now.AddMinutes(1))),
            new DateTimeOffset(Now), default))!;
        var staff = facts.Access.Audience.Single(value => value.Kind == EventResourceAudienceKindEnum.EventStaff);
        await Assert.That(staff.ExpiresAtUtc).IsEqualTo(new DateTimeOffset(Now.AddSeconds(1)));
        await Assert.That(facts.Management.Permissions.Any(value =>
            value.Code == PermissionCodes.EventUpdate && value.Authority.IsEffectiveAt(new DateTimeOffset(Now.AddSeconds(1)))))
            .IsFalse();
    }

    [Test]
    [Arguments(RegistrationOrderStatusEnum.Cancelled)]
    [Arguments(RegistrationOrderStatusEnum.Rejected)]
    public async Task TerminalOrderCannotRemainASessionRegistrant(RegistrationOrderStatusEnum status)
    {
        var scope = await database.SeedScopeAsync();
        Guid userId;
        EventResource resource = EventResourcePersistenceTests.CreateDraft(
            scope.TenantAId, scope.EventAId, scope.SessionAId);
        await using (var seed = database.CreateContext())
        {
            userId = (await seed.Actors.Where(actor => actor.Id == scope.ActorId)
                .Select(actor => actor.UserId).SingleAsync())!.Value;
            var order = RegistrationOrder.Create(scope.TenantAId, scope.EventAId, userId, null,
                BookingPartyTypeEnum.Individual, scope.CatalogAId,
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
                null, null, "USD", Now, null);
            var participant = RegistrationParticipant.Create(
                scope.TenantAId, order.Id, userId, ParticipantTypeEnum.Adult, null);
            order.AddParticipant(participant);
            seed.AddRange(order, resource, new EventRegistration
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                EventId = scope.EventAId, Event = null!, EventSessionId = scope.SessionAId,
                EventSession = null!, LinkedUserId = userId, RegistrationOrderId = order.Id,
                RegistrationParticipantId = participant.Id, RegistrationParticipant = participant,
                CoverageEstablishedAt = Now, ConcurrencyStamp = Guid.CreateVersion7()
            });
            await seed.SaveChangesAsync();
            await seed.RegistrationOrders.Where(value => value.Id == order.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.RegistrationOrderStatusId, (int)status));
        }

        await using var context = database.CreateContext();
        var reader = CreateReader(
            new EventResourceRepository(context), new EventAuthoritySnapshotService(context));
        var facts = await reader.ReadAsync(
            new(scope.TenantAId, resource.Id, userId, false, "view", new DateTimeOffset(Now.AddMinutes(1))),
            new DateTimeOffset(Now), default);
        await Assert.That(facts).IsNotNull();
        await Assert.That(facts!.Access.Audience.Any(value =>
            value.Kind == EventResourceAudienceKindEnum.SessionRegistrant)).IsFalse();
    }

    [Test]
    public async Task AuthorityReadBypassesPretrackedStaleResource()
    {
        var scope = await database.SeedScopeAsync();
        EventResource resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        await using (ExploreDbContext seed = database.CreateContext())
        {
            seed.EventResources.Add(resource);
            await seed.SaveChangesAsync();
        }

        await using ExploreDbContext staleContext = database.CreateContext();
        EventResource tracked = await staleContext.EventResources
            .Include(value => value.AudienceRules)
            .SingleAsync(value => value.Id == resource.Id);
        await Assert.That(tracked.Title).IsEqualTo("Portable resource");

        await using (ExploreDbContext writer = database.CreateContext())
        {
            EventResource current = await writer.EventResources
                .Include(value => value.AudienceRules)
                .SingleAsync(value => value.Id == resource.Id);
            current.UpdateMetadata(new EventResourceMetadata
            {
                Title = "Fresh authority title",
                Kind = (EventResourceKindEnum)current.EventResourceKindId,
                DisclosureMode = (EventResourceDisclosureModeEnum)current.DisclosureModeId,
                PublicTitle = current.PublicTitle,
                Description = current.Description,
                SensitiveNotes = current.SensitiveNotes,
                LanguageCode = current.LanguageCode,
                SortOrder = current.SortOrder
            }, current.ConcurrencyStamp, scope.ActorId,
                new DateTime(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            await writer.SaveChangesAsync();
        }

        var repository = new EventResourceRepository(staleContext);
        EventResource fresh = (await repository.GetAuthorityResourceAsync(
            scope.TenantAId, resource.Id, CancellationToken.None))!;
        await Assert.That(fresh.Title).IsEqualTo("Fresh authority title");
        await Assert.That(tracked.Title).IsEqualTo("Portable resource");

        var reader = CreateReader(
            repository, new EventAuthoritySnapshotService(staleContext));
        EventResourceAuthorizationFacts? facts = await reader.ReadAsync(
            new(scope.TenantAId, resource.Id, null, false, "view"),
            new DateTimeOffset(Now), CancellationToken.None);
        await Assert.That(facts).IsNotNull();
        await Assert.That(facts!.ResourceVersion).IsEqualTo(fresh.ConcurrencyStamp);
        await Assert.That(facts.ResourceVersion).IsNotEqualTo(tracked.ConcurrencyStamp);
    }

    [Test]
    public async Task ReaderDeniesDeletedRequiredSessionChild()
    {
        var scope = await database.SeedScopeAsync();
        EventResource resource = EventResourcePersistenceTests.CreateDraft(
            scope.TenantAId, scope.EventAId, scope.SessionAId);
        await using (ExploreDbContext seed = database.CreateContext())
        {
            seed.EventResources.Add(resource);
            await seed.SaveChangesAsync();
        }

        await using (ExploreDbContext writer = database.CreateContext())
        {
            EventSession session = await writer.EventSessions.SingleAsync(value => value.Id == scope.SessionAId);
            session.IsDeleted = true;
            session.DeletedAt = new DateTime(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            await writer.SaveChangesAsync();
        }

        await using ExploreDbContext read = database.CreateContext();
        var reader = CreateReader(
            new EventResourceRepository(read), new EventAuthoritySnapshotService(read));
        EventResourceAuthorizationFacts? facts = await reader.ReadAsync(
            new(scope.TenantAId, resource.Id, null, false, "view",
                new DateTimeOffset(2040, 1, 1, 12, 1, 0, TimeSpan.Zero)),
            new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        await Assert.That(facts).IsNull();
    }

    [Test]
    public async Task BatchPreservesPositionsAndHasCardinalityIndependentQueryGrowth()
    {
        var scope = await database.SeedScopeAsync();
        EventResource[] resources = Enumerable.Range(0, EventResourceAuthorityRequest.MaximumBatchResources)
            .Select(_ => EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId))
            .ToArray();
        await using (ExploreDbContext seed = database.CreateContext())
        {
            seed.EventResources.AddRange(resources);
            await seed.SaveChangesAsync();
        }

        EventResourceAuthorityRequest[] smallRequests = resources.Take(5)
            .Select(resource => new EventResourceAuthorityRequest(
                scope.TenantAId, resource.Id, null, false, "view"))
            .ToArray();
        var smallCounter = new ReaderCommandCounter();
        await using (ExploreDbContext context = database.CreateContext(smallCounter))
        {
            var reader = CreateReader(
                new EventResourceRepository(context), new EventAuthoritySnapshotService(context));
            IReadOnlyList<EventResourceAuthorizationFacts?> facts = await reader.ReadBatchAsync(
                smallRequests, new DateTimeOffset(Now), default);
            await Assert.That(facts.Count).IsEqualTo(smallRequests.Length);
            await Assert.That(facts.All(value => value is not null)).IsTrue();
        }

        EventResourceAuthorityRequest[] largeRequests = resources
            .Select(resource => new EventResourceAuthorityRequest(
                scope.TenantAId, resource.Id, null, false, "view"))
            .ToArray();
        largeRequests[217] = new(scope.TenantAId, Guid.CreateVersion7(), null, false, "view");
        var largeCounter = new ReaderCommandCounter();
        await using (ExploreDbContext context = database.CreateContext(largeCounter))
        {
            var reader = CreateReader(
                new EventResourceRepository(context), new EventAuthoritySnapshotService(context));
            IReadOnlyList<EventResourceAuthorizationFacts?> facts = await reader.ReadBatchAsync(
                largeRequests, new DateTimeOffset(Now), default);
            await Assert.That(facts.Count).IsEqualTo(largeRequests.Length);
            await Assert.That(facts[217]).IsNull();
            await Assert.That(facts[216]).IsNotNull();
            await Assert.That(facts[218]).IsNotNull();
        }

        await Assert.That(smallCounter.ReaderCommandCount).IsGreaterThan(0);
        await Assert.That(largeCounter.ReaderCommandCount)
            .IsLessThanOrEqualTo(smallCounter.ReaderCommandCount + 1);
    }

    [Test]
    public async Task AuthorityIdentityOverflowIsDeniedWithoutTruncation()
    {
        await using ExploreDbContext context = database.CreateContext();
        var repository = new EventResourceRepository(context);
        Guid[] excessive = Enumerable.Range(0, 501).Select(_ => Guid.CreateVersion7()).ToArray();

        ArgumentOutOfRangeException? overflow = null;
        try
        {
            await repository.GetCheckInStatesAsync(
                Guid.CreateVersion7(), excessive, [Guid.CreateVersion7()], 500, CancellationToken.None);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            overflow = exception;
        }
        await Assert.That(overflow).IsNotNull();
    }

    private static EventResourceAuthoritySnapshotReader CreateReader(
        IEventResourceRepository resources, IEventAuthoritySnapshotService authority)
    {
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(EventResourceGovernancePolicy.Default(long.MaxValue));
        return new(resources, authority, governance);
    }

    private sealed class AuthorityClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class AuthorityPreparation(string generation) : IEventResourcePrivatePreparation
    {
        public string AttachmentGeneration { get; } = generation;
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            ReaderCommandCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}
