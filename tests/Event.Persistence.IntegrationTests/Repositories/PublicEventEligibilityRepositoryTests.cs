// ABOUTME: Deterministic repository tests for the central public Event eligibility predicate.
// ABOUTME: Covers local owner participation, federated source correlation, and public read consistency without Docker.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class PublicEventEligibilityRepositoryTests
{
    [Test]
    public async Task IsPubliclyEligibleAsync_EnforcesLocalAndFederatedEligibilityMatrix()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(tenantId);
        var seeds = new EligibilitySeeds(context, tenantId);

        DomainEvent localUser = seeds.LocalUserEvent("local-user", hasActiveTenantUser: true);
        DomainEvent outboundLocalUser = seeds.LocalUserEvent("outbound-local-user", hasActiveTenantUser: true, isOutbound: true);
        DomainEvent missingLocalParticipation = seeds.LocalUserEvent("missing-participation", hasActiveTenantUser: false);
        DomainEvent suspendedActor = seeds.LocalUserEvent("suspended-actor", hasActiveTenantUser: true, actorSuspended: true);
        DomainEvent moderated = seeds.LocalUserEvent("moderated", hasActiveTenantUser: true, status: EventStatusEnum.Moderated);
        DomainEvent archived = seeds.LocalUserEvent("archived", hasActiveTenantUser: true, status: EventStatusEnum.Archived);
        DomainEvent privateEvent = seeds.LocalUserEvent("private", hasActiveTenantUser: true, visibility: VisibilityTypeEnum.Private);
        DomainEvent deleted = seeds.LocalUserEvent("deleted", hasActiveTenantUser: true, isDeleted: true);
        DomainEvent organizationWithoutOrganizerEligibility = seeds.OrganizationEvent(
            "organization-visible", isOrganizerEligible: false, participatesInCurrentTenant: true);
        DomainEvent organizationInOtherTenant = seeds.OrganizationEvent(
            "organization-other-tenant", isOrganizerEligible: true, participatesInCurrentTenant: false);
        DomainEvent group = seeds.GroupEvent("group-visible", isOrganizerEligible: false, isSuspended: false);
        DomainEvent suspendedGroup = seeds.GroupEvent("group-suspended", isOrganizerEligible: true, isSuspended: true);
        DomainEvent malformedLocalOwner = seeds.ExternalLocalEvent("external-local");
        DomainEvent federated = seeds.FederatedEvent("federated", didMatchesActorIdentity: true, presentationCurrent: true, presentationVisible: true);
        DomainEvent stalePresentation = seeds.FederatedEvent("stale-presentation", didMatchesActorIdentity: true, presentationCurrent: false, presentationVisible: true);
        DomainEvent mismatchedDid = seeds.FederatedEvent("mismatched-did", didMatchesActorIdentity: false, presentationCurrent: true, presentationVisible: true);
        DomainEvent hiddenPresentation = seeds.FederatedEvent("hidden-presentation", didMatchesActorIdentity: true, presentationCurrent: true, presentationVisible: false);
        DomainEvent otherTenantPresentation = seeds.FederatedEvent("other-tenant-presentation", didMatchesActorIdentity: true, presentationCurrent: true, presentationVisible: true, presentationTenantId: Guid.CreateVersion7());
        DomainEvent inactiveIdentity = seeds.FederatedEvent("inactive-identity", didMatchesActorIdentity: true, presentationCurrent: true, presentationVisible: true, identityActive: false);
        DomainEvent suspendedIdentity = seeds.FederatedEvent("suspended-identity", didMatchesActorIdentity: true, presentationCurrent: true, presentationVisible: true, identitySuspended: true);
        DomainEvent deletedIdentity = seeds.FederatedEvent("deleted-identity", didMatchesActorIdentity: true, presentationCurrent: true, presentationVisible: true, identityDeleted: true);
        DomainEvent tombstonedRecord = seeds.FederatedEvent("tombstoned-record", didMatchesActorIdentity: true, presentationCurrent: true, presentationVisible: true, recordTombstoned: true);

        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        var repository = (IEventRepository)new EventRepository(context);

        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, localUser.Id, CancellationToken.None)).IsTrue();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, outboundLocalUser.Id, CancellationToken.None)).IsTrue();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, missingLocalParticipation.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, suspendedActor.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, moderated.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, archived.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, privateEvent.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, deleted.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, organizationWithoutOrganizerEligibility.Id, CancellationToken.None)).IsTrue();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, organizationInOtherTenant.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, group.Id, CancellationToken.None)).IsTrue();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, suspendedGroup.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, malformedLocalOwner.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, federated.Id, CancellationToken.None)).IsTrue();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, stalePresentation.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, mismatchedDid.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, hiddenPresentation.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, otherTenantPresentation.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, inactiveIdentity.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, suspendedIdentity.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, deletedIdentity.Id, CancellationToken.None)).IsFalse();
        await Assert.That(await repository.IsPubliclyEligibleAsync(tenantId, tombstonedRecord.Id, CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task PublicReads_ExcludeIneligibleEventsBeforeCountPaginationAndDirectReads()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(tenantId);
        var seeds = new EligibilitySeeds(context, tenantId);

        DomainEvent firstEligible = seeds.LocalUserEvent("eligible-first", hasActiveTenantUser: true, publicCode: "eligible-first", startsAt: DateTimeOffset.UtcNow.AddDays(1));
        DomainEvent secondEligible = seeds.LocalUserEvent("eligible-second", hasActiveTenantUser: true, publicCode: "eligible-second", startsAt: DateTimeOffset.UtcNow.AddDays(2));
        DomainEvent ineligible = seeds.LocalUserEvent("ineligible", hasActiveTenantUser: false, publicCode: "ineligible", startsAt: DateTimeOffset.UtcNow.AddDays(3));

        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        var repository = new EventRepository(context);
        var publiclyDiscoverable = new EventQuerySpecification()
            .And(EventFilter.PubliclyDiscoverable());

        var (_, totalCount) = await repository.GetEventsWithDetailsPaged(1, 1, publiclyDiscoverable);

        await Assert.That(totalCount).IsEqualTo(2);
        await Assert.That(await repository.GetPublicEventWithDetailsByCodeAsync(ineligible.PublicCode, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetPublicEventForOpenGraphAsync(ineligible.PublicCode, CancellationToken.None)).IsNull();
        await Assert.That((await repository.GetPublishedPublicEventsForSitemap(10, CancellationToken.None)).Select(value => value.Id))
            .IsEquivalentTo([firstEligible.Id, secondEligible.Id]);
        await Assert.That((await repository.SearchAiReferenceEventsAsync("eligible", 10, CancellationToken.None)).Select(value => value.Id))
             .IsEquivalentTo([firstEligible.Id, secondEligible.Id]);
    }

    [Test]
    public async Task PublicChildReads_RequireEligibleParentEvent()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using var context = CreateContext(tenantId);
        var seeds = new EligibilitySeeds(context, tenantId);

        var eligible = CreatePublicChildren(context, seeds.LocalUserEvent("eligible-child", hasActiveTenantUser: true), tenantId);
        var suspendedActor = CreatePublicChildren(context, seeds.LocalUserEvent("suspended-child", hasActiveTenantUser: true, actorSuspended: true), tenantId);
        var missingParticipation = CreatePublicChildren(context, seeds.LocalUserEvent("missing-child", hasActiveTenantUser: false), tenantId);
        var hiddenFederated = CreatePublicChildren(
            context,
            seeds.FederatedEvent("hidden-federated-child", didMatchesActorIdentity: true, presentationCurrent: true, presentationVisible: false),
            tenantId);

        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();

        var sessionRepository = new EventSessionRepository(context);
        var groupRepository = new EventSessionGroupRepository(context);
        var assignmentRepository = new EventSessionGroupSessionRepository(context);
        var agendaRepository = new EventAgendaItemRepository(context);
        var sessionAgendaRepository = new EventSessionAgendaItemRepository(context);

        await Assert.That((await sessionRepository.GetPublicSessionsByEventAsync(eligible.Event.Id, CancellationToken.None)).Select(session => session.Id))
            .IsEquivalentTo([eligible.Session.Id]);
        await Assert.That(await sessionRepository.GetPublicSessionsByEventAsync(suspendedActor.Event.Id, CancellationToken.None)).IsEmpty();
        await Assert.That(await sessionRepository.GetPublicSessionsByEventAsync(missingParticipation.Event.Id, CancellationToken.None)).IsEmpty();
        await Assert.That(await sessionRepository.GetPublicSessionsByEventAsync(hiddenFederated.Event.Id, CancellationToken.None)).IsEmpty();

        await Assert.That((await groupRepository.GetPublicByEventAsync(eligible.Event.Id, CancellationToken.None)).Select(group => group.Id))
            .IsEquivalentTo([eligible.Group.Id]);
        await Assert.That(await groupRepository.GetPublicByEventAsync(suspendedActor.Event.Id, CancellationToken.None)).IsEmpty();
        await Assert.That(await groupRepository.GetPublicByEventAsync(missingParticipation.Event.Id, CancellationToken.None)).IsEmpty();

        await Assert.That((await assignmentRepository.GetPublicByGroupAsync(eligible.Group.Id, CancellationToken.None)).Select(assignment => assignment.Id))
            .IsEquivalentTo([eligible.Assignment.Id]);
        await Assert.That(await assignmentRepository.GetPublicByGroupAsync(suspendedActor.Group.Id, CancellationToken.None)).IsEmpty();
        await Assert.That(await assignmentRepository.GetPublicByGroupAsync(missingParticipation.Group.Id, CancellationToken.None)).IsEmpty();

        await Assert.That((await agendaRepository.GetPublicByEventAsync(eligible.Event.Id, CancellationToken.None)).Select(item => item.Id))
            .IsEquivalentTo([eligible.AgendaItem.Id]);
        await Assert.That(await agendaRepository.GetPublicByEventAsync(suspendedActor.Event.Id, CancellationToken.None)).IsEmpty();
        await Assert.That(await agendaRepository.GetPublicByEventAsync(missingParticipation.Event.Id, CancellationToken.None)).IsEmpty();

        await Assert.That((await sessionAgendaRepository.GetPublicBySessionAsync(eligible.Session.Id, CancellationToken.None)).Select(item => item.Id))
            .IsEquivalentTo([eligible.SessionAgendaItem.Id]);
        await Assert.That(await sessionAgendaRepository.GetPublicBySessionAsync(suspendedActor.Session.Id, CancellationToken.None)).IsEmpty();
        await Assert.That(await sessionAgendaRepository.GetPublicBySessionAsync(missingParticipation.Session.Id, CancellationToken.None)).IsEmpty();
    }

    private static PublicChildGraph CreatePublicChildren(ExploreDbContext context, DomainEvent @event, Guid tenantId)
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Public session",
            StartTime = new DateTimeOffset(2030, 1, 1, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero)
        };
        var group = new EventSessionGroup
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            TenantId = tenantId,
            Tenant = null!,
            Name = "Public group",
            IsPublished = true
        };
        var assignment = new EventSessionGroupSession
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            EventSessionId = session.Id,
            EventSession = session,
            EventSessionGroupId = group.Id,
            EventSessionGroup = group,
            TenantId = tenantId,
            Tenant = null!
        };
        var agendaItem = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventId = @event.Id,
            Event = @event,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Public agenda",
            StartTime = session.StartTime!.Value,
            EndTime = session.EndTime!.Value
        };
        var sessionAgendaItem = new EventSessionAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = session.Id,
            EventSession = session,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Public session agenda",
            StartTime = session.StartTime!.Value,
            EndTime = session.EndTime!.Value
        };

        context.AddRange(session, group, assignment, agendaItem, sessionAgendaItem);
        return new PublicChildGraph(@event, session, group, assignment, agendaItem, sessionAgendaItem);
    }

    private static ExploreDbContext CreateContext(Guid tenantId)
    {
        var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"public-event-eligibility-{Guid.NewGuid():N}")
            .Options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
        context.EnableTenantFilterBypass("Seeds public Event eligibility scenarios before tenant-filtered assertions.");
        return context;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record PublicChildGraph(
        DomainEvent Event,
        EventSession Session,
        EventSessionGroup Group,
        EventSessionGroupSession Assignment,
        EventAgendaItem AgendaItem,
        EventSessionAgendaItem SessionAgendaItem);

    private sealed class EligibilitySeeds
    {
        private readonly ExploreDbContext _context;
        private readonly Guid _tenantId;

        public EligibilitySeeds(ExploreDbContext context, Guid tenantId)
        {
            _context = context;
            _tenantId = tenantId;
            _context.ActorTypes.AddRange(
                new ActorType { Id = (int)ActorTypeEnum.User, MasterCode = "USER", FullName = "User" },
                new ActorType { Id = (int)ActorTypeEnum.Organization, MasterCode = "ORGANIZATION", FullName = "Organization" },
                new ActorType { Id = (int)ActorTypeEnum.Group, MasterCode = "GROUP", FullName = "Group" },
                new ActorType { Id = (int)ActorTypeEnum.ExternalUnclassified, MasterCode = "EXTERNAL", FullName = "External" });
            _context.TenantStatuses.Add(new TenantStatus { Id = (int)TenantStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active" });
            _context.Tenants.Add(new Tenant { Id = _tenantId, FullName = "Eligibility tenant", Slug = "eligibility-tenant", TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null! });
            _context.EventStatuses.Add(new EventStatus { Id = (int)EventStatusEnum.Published, MasterCode = "PUBLISHED", FullName = "Published" });
            _context.VisibilityTypes.Add(new VisibilityType { Id = (int)VisibilityTypeEnum.Public, MasterCode = "PUBLIC", FullName = "Public" });
            _context.EventFormats.Add(new EventFormat { Id = (int)EventFormatEnum.Local, MasterCode = "LOCAL", FullName = "Local" });
        }

        public DomainEvent LocalUserEvent(
            string name,
            bool hasActiveTenantUser,
            string? publicCode = null,
            DateTimeOffset? startsAt = null,
            bool actorSuspended = false,
            EventStatusEnum status = EventStatusEnum.Published,
            VisibilityTypeEnum visibility = VisibilityTypeEnum.Public,
            bool isDeleted = false,
            bool isOutbound = false)
        {
            var user = new User
            {
                Id = Guid.CreateVersion7(),
                Pii = new UserPii { Email = $"{name}@example.test", FirstName = "Public", LastName = "User" }
            };
            var actor = CreateActor(ActorTypeEnum.User, userId: user.Id);
            actor.IsSuspended = actorSuspended;
            var @event = CreateEvent(name, actor.Id, publicCode, startsAt, status, visibility, isDeleted);
            _context.Users.Add(user);
            _context.Actors.Add(actor);
            _context.Events.Add(@event);

            if (hasActiveTenantUser)
            {
                _context.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = _tenantId,
                    Tenant = null!,
                    UserId = user.Id,
                    User = null!,
                    ActorId = actor.Id,
                    Actor = null!,
                    StatusId = (int)TenantUserStatusEnum.Active
                });
            }

            if (isOutbound)
            {
                var record = new AtprotoRecord
                {
                    Id = Guid.CreateVersion7(),
                    Did = $"did:plc:{name}",
                    Collection = "community.lexicon.calendar.event",
                    RecordKey = name,
                    Direction = AtprotoRecordDirection.Outbound,
                    Provenance = AtprotoRecordProvenance.LocalLifecycle,
                    SourceVersion = 1,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.AtprotoRecords.Add(record);
                _context.AtprotoOutboundRecordOwnerships.Add(new AtprotoOutboundRecordOwnership
                {
                    AtprotoRecordId = record.Id,
                    TenantId = _tenantId,
                    UserId = user.Id,
                    SourceEntityType = "Event",
                    SourceEntityId = @event.Id,
                    SourceVersion = @event.ConcurrencyStamp,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                @event.AtprotoRecordId = record.Id;
            }

            return @event;
        }

        public DomainEvent OrganizationEvent(string name, bool isOrganizerEligible, bool participatesInCurrentTenant)
        {
            var organization = new Organization
            {
                Id = Guid.CreateVersion7(),
                Pii = new OrganizationPii { FullName = name }
            };
            var actor = CreateActor(ActorTypeEnum.Organization, organizationId: organization.Id);
            Guid participationTenantId = participatesInCurrentTenant ? _tenantId : Guid.CreateVersion7();
            _context.Organizations.Add(organization);
            _context.Actors.Add(actor);
            _context.OrganizationTenants.Add(new OrganizationTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = participationTenantId,
                Tenant = null!,
                OrganizationId = organization.Id,
                Organization = null!,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!,
                IsVisible = true,
                IsOrganizerEligible = isOrganizerEligible
            });
            var @event = CreateEvent(name, actor.Id);
            _context.Events.Add(@event);
            return @event;
        }

        public DomainEvent GroupEvent(string name, bool isOrganizerEligible, bool isSuspended)
        {
            var group = new Group { Id = Guid.CreateVersion7(), FullName = name };
            var actor = CreateActor(ActorTypeEnum.Group, groupId: group.Id);
            _context.Groups.Add(group);
            _context.Actors.Add(actor);
            _context.GroupTenants.Add(new GroupTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = _tenantId,
                Tenant = null!,
                GroupId = group.Id,
                Group = null!,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!,
                IsVisible = true,
                IsOrganizerEligible = isOrganizerEligible,
                IsSuspended = isSuspended
            });
            var @event = CreateEvent(name, actor.Id);
            _context.Events.Add(@event);
            return @event;
        }

        public DomainEvent ExternalLocalEvent(string name)
        {
            var subject = new ExternalActorSubject { Id = Guid.CreateVersion7() };
            var actor = CreateActor(ActorTypeEnum.ExternalUnclassified, externalActorSubjectId: subject.Id);
            _context.ExternalActorSubjects.Add(subject);
            _context.Actors.Add(actor);
            var @event = CreateEvent(name, actor.Id);
            _context.Events.Add(@event);
            return @event;
        }

        public DomainEvent FederatedEvent(
            string name,
            bool didMatchesActorIdentity,
            bool presentationCurrent,
            bool presentationVisible,
            Guid? presentationTenantId = null,
            bool identityActive = true,
            bool identitySuspended = false,
            bool identityDeleted = false,
            bool recordTombstoned = false)
        {
            var actor = CreateActor(ActorTypeEnum.ExternalUnclassified, externalActorSubjectId: Guid.CreateVersion7());
            var record = new AtprotoRecord
            {
                Id = Guid.CreateVersion7(),
                Did = $"did:plc:{name}",
                Collection = "community.lexicon.calendar.event",
                RecordKey = name,
                Direction = AtprotoRecordDirection.Inbound,
                Provenance = AtprotoRecordProvenance.Jetstream,
                SourceVersion = 2,
                UpdatedAt = DateTime.UtcNow,
                TombstonedAt = recordTombstoned ? DateTime.UtcNow : null
            };
            _context.ExternalActorSubjects.Add(new ExternalActorSubject { Id = actor.ExternalActorSubjectId!.Value });
            _context.Actors.Add(actor);
            _context.AtprotoRecords.Add(record);
            _context.AtprotoIdentities.Add(new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse(didMatchesActorIdentity ? record.Did : $"did:plc:other-{name}"))
            {
                Id = Guid.CreateVersion7(),

                ActorId = actor.Id,
                Actor = null!,
                PdsHost = "https://pds.example.test",
                IsActive = identityActive,
                IsSuspended = identitySuspended,
                IsDeleted = identityDeleted,
                LastResolvedAt = DateTime.UtcNow
            });
            _context.AtprotoRecordTenantPresentations.Add(new AtprotoRecordTenantPresentation
            {
                TenantId = presentationTenantId ?? _tenantId,
                AtprotoRecordId = record.Id,
                IsVisible = presentationVisible,
                SourceVersion = presentationCurrent ? record.SourceVersion : record.SourceVersion - 1,
                EvaluatedAt = DateTime.UtcNow
            });
            var @event = CreateEvent(name, actor.Id);
            @event.AtprotoRecordId = record.Id;
            _context.Events.Add(@event);
            return @event;
        }

        private Actor CreateActor(
            ActorTypeEnum type,
            Guid? userId = null,
            Guid? organizationId = null,
            Guid? groupId = null,
            Guid? externalActorSubjectId = null) => new()
            {
                Id = Guid.CreateVersion7(),
                ActorTypeId = (int)type,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = type.ToString() },
                UserId = userId,
                OrganizationId = organizationId,
                GroupId = groupId,
                ExternalActorSubjectId = externalActorSubjectId
            };

        private DomainEvent CreateEvent(
            string name,
            Guid actorId,
            string? publicCode = null,
            DateTimeOffset? startsAt = null,
            EventStatusEnum status = EventStatusEnum.Published,
            VisibilityTypeEnum visibility = VisibilityTypeEnum.Public,
            bool isDeleted = false) => new(status)
            {
                Id = Guid.CreateVersion7(),
                Title = name,
                PublicCode = publicCode ?? $"code-{name}",
                ActorId = actorId,
                Actor = null!,
                TenantId = _tenantId,
                Tenant = null!,
                EventStatus = null!,
                VisibilityTypeId = (int)visibility,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                TotalViews = 0,
                FirstSessionStartUtc = startsAt,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = isDeleted,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
    }
}
