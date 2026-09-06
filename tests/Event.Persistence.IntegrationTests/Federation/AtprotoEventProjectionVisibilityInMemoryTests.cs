// ABOUTME: In-memory fallback tests for ATProto projection visibility when PostgreSQL containers are unavailable.
// ABOUTME: Exercises inbound Actor/DID presentation checks and owned local-echo eligibility without bypassing tenant filters.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class AtprotoEventProjectionVisibilityInMemoryTests
{
    [Test]
    public async Task VisibleQuery_ExcludesRowsWithoutTheExactCurrentActorIdentityCorrelation()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        Guid valid = AddProjection(context, tenantId, "valid");
        Guid suspendedActor = AddProjection(
            context,
            tenantId,
            "actor-suspended",
            configureEventActor: actor => actor.IsSuspended = true);
        Guid inactiveIdentity = AddProjection(
            context,
            tenantId,
            "identity-inactive",
            configureIdentity: identity => identity.IsActive = false);
        Guid didMismatch = AddProjection(
            context,
            tenantId,
            "did-mismatch",
            identityDid: "did:plc:not-the-record-did");
        Guid actorMismatch = AddProjection(
            context,
            tenantId,
            "actor-mismatch",
            identityActor: Actor("other-actor"));
        Guid stalePresentation = AddProjection(
            context,
            tenantId,
            "presentation-stale",
            presentationSourceVersion: 1);
        await context.SaveChangesAsync();
        var repository = new AtprotoEventProjectionRepository(context);

        (IReadOnlyList<AtprotoEventProjection> discovery, int totalCount) =
            await repository.GetPublicWindowAsync(PublicQuery(), CancellationToken.None);
        IReadOnlyList<AtprotoEventProjection> echoes = await repository.GetVisibleByRecordIdsAsync(
            [valid, suspendedActor, inactiveIdentity, didMismatch, actorMismatch, stalePresentation],
            CancellationToken.None);

        await Assert.That(discovery).HasSingleItem();
        await Assert.That(discovery[0].AtprotoRecordId).IsEqualTo(valid);
        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(echoes).HasSingleItem();
        await Assert.That(echoes[0].AtprotoRecordId).IsEqualTo(valid);
    }

    [Test]
    public async Task VisibleQuery_AppliesImportedEventVisibilityAndLifecycleWithoutRequiringPublished()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        Guid draft = AddProjection(context, tenantId, "draft", eventStatus: EventStatusEnum.Draft);
        Guid published = AddProjection(context, tenantId, "published", eventStatus: EventStatusEnum.Published);
        Guid cancelled = AddProjection(context, tenantId, "cancelled", eventStatus: EventStatusEnum.Cancelled);
        Guid completed = AddProjection(context, tenantId, "completed", eventStatus: EventStatusEnum.Completed);
        Guid moderated = AddProjection(context, tenantId, "moderated", eventStatus: EventStatusEnum.Moderated);
        Guid archived = AddProjection(context, tenantId, "archived", eventStatus: EventStatusEnum.Archived);
        Guid deleted = AddProjection(context, tenantId, "deleted", eventIsDeleted: true);
        Guid privateEvent = AddProjection(context, tenantId, "private", visibility: VisibilityTypeEnum.Private);
        Guid unlisted = AddProjection(context, tenantId, "unlisted", visibility: VisibilityTypeEnum.Unlisted);
        Guid membersOnly = AddProjection(context, tenantId, "members", visibility: VisibilityTypeEnum.MembersOnly);
        Guid detached = AddProjection(context, tenantId, "detached", detachRecordAnchor: true);
        await context.SaveChangesAsync();
        var repository = new AtprotoEventProjectionRepository(context);

        (IReadOnlyList<AtprotoEventProjection> window, int totalCount) =
            await repository.GetPublicWindowAsync(PublicQuery(), CancellationToken.None);
        IReadOnlyList<AtprotoEventProjection> exact = await repository.GetVisibleByRecordIdsAsync(
            [draft, published, cancelled, completed, moderated, archived, deleted, privateEvent, unlisted, membersOnly, detached],
            CancellationToken.None);

        await Assert.That(window).Count().IsEqualTo(3);
        await Assert.That(window.Any(value => value.AtprotoRecordId == draft)).IsTrue();
        await Assert.That(window.Any(value => value.AtprotoRecordId == cancelled)).IsTrue();
        await Assert.That(window.Any(value => value.AtprotoRecordId == completed)).IsTrue();
        await Assert.That(window.Any(value => value.AtprotoRecordId == published)).IsFalse();
        await Assert.That(totalCount).IsEqualTo(3);
        await Assert.That(exact).Count().IsEqualTo(4);
        await Assert.That(exact.Any(value => value.AtprotoRecordId == draft)).IsTrue();
        await Assert.That(exact.Any(value => value.AtprotoRecordId == published)).IsTrue();
        await Assert.That(exact.Any(value => value.AtprotoRecordId == cancelled)).IsTrue();
        await Assert.That(exact.Any(value => value.AtprotoRecordId == completed)).IsTrue();

        foreach (Guid deniedRecordId in new[] { moderated, archived, deleted, privateEvent, unlisted, membersOnly, detached })
        {
            await Assert.That(exact.Any(value => value.AtprotoRecordId == deniedRecordId)).IsFalse();
            await Assert.That(await repository.GetVisibleByRecordIdAsync(deniedRecordId, CancellationToken.None)).IsNull();
        }
    }

    [Test]
    public async Task VisibleQuery_UsesExactEventOwnershipAndCentralEligibilityForLocalEchoes()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        Guid eligible = AddOwnedLocalEcho(context, tenantId, "eligible", hasActiveTenantUser: true);
        Guid inactiveParticipation = AddOwnedLocalEcho(context, tenantId, "inactive-participation", hasActiveTenantUser: false);
        Guid mismatchedSource = AddOwnedLocalEcho(
            context,
            tenantId,
            "mismatched-source",
            hasActiveTenantUser: true,
            ownershipMatchesEvent: false,
            hasInboundEvidence: true);
        await context.SaveChangesAsync();
        var repository = new AtprotoEventProjectionRepository(context);

        (IReadOnlyList<AtprotoEventProjection> discovery, int totalCount) =
            await repository.GetPublicWindowAsync(PublicQuery(), CancellationToken.None);
        IReadOnlyList<AtprotoEventProjection> echoes = await repository.GetVisibleByRecordIdsAsync(
            [eligible, inactiveParticipation, mismatchedSource],
            CancellationToken.None);

        await Assert.That(discovery).Count().IsEqualTo(0);
        await Assert.That(totalCount).IsEqualTo(0);
        await Assert.That(echoes).HasSingleItem();
        await Assert.That(echoes[0].AtprotoRecordId).IsEqualTo(eligible);
        await Assert.That(await repository.GetVisibleByRecordIdAsync(eligible, CancellationToken.None)).IsNotNull();
        await Assert.That(await repository.GetVisibleByRecordIdAsync(inactiveParticipation, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetVisibleByRecordIdAsync(mismatchedSource, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task ActorSubscriptionTarget_AllowsEligibleFederatedOrganizationWithoutParticipation()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        var actorType = new ActorType
        {
            Id = (int)ActorTypeEnum.Organization,
            FullName = "Organization",
            MasterCode = "ORGANIZATION"
        };
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "Federated organization" }
        };
        Guid recordId = AddProjection(
            context,
            tenantId,
            "federated-organization",
            configureEventActor: actor =>
            {
                actor.ActorTypeId = (int)ActorTypeEnum.Organization;
                actor.ActorType = actorType;
                actor.OrganizationId = organization.Id;
                actor.Organization = organization;
                organization.Actor = actor;
            },
            eventStatus: EventStatusEnum.Published);
        context.Set<ActorType>().Add(actorType);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        Guid actorId = context.Events.Local.Single(@event => @event.AtprotoRecordId == recordId).ActorId;
        var repository = new ActorRepository(context);

        Actor? target = await repository.GetLocallyDiscoverableSubscriptionTargetAsync(
            tenantId,
            actorId,
            CancellationToken.None);

        await Assert.That(target).IsNotNull();
        await Assert.That(target!.Id).IsEqualTo(actorId);
        await Assert.That(organization.TenantParticipations).IsEmpty();
    }

    [Test]
    public async Task VisibleQuery_ExcludesTombstonedOwnedLocalEchoes()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        Guid recordId = AddOwnedLocalEcho(context, tenantId, "tombstoned", hasActiveTenantUser: true);
        context.AtprotoRecords.Local.Single(record => record.Id == recordId).TombstonedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        var repository = new AtprotoEventProjectionRepository(context);

        IReadOnlyList<AtprotoEventProjection> visible = await repository.GetVisibleByRecordIdsAsync(
            [recordId],
            CancellationToken.None);

        await Assert.That(visible).IsEmpty();
        await Assert.That(await repository.GetVisibleByRecordIdAsync(recordId, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task LiveGroundedEventOwnerships_IncludeSoftDeletedEventsForExactGlobalModerationReconciliation()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        Guid recordId = AddOwnedLocalEcho(context, tenantId, "deleted-source", hasActiveTenantUser: true);
        AtprotoOutboundRecordOwnership ownership = context.AtprotoOutboundRecordOwnerships.Local.Single();
        Explore.Domain.Event sourceEvent = context.Events.Local.Single(value => value.Id == ownership.SourceEntityId);
        sourceEvent.IsDeleted = true;
        await context.SaveChangesAsync();
        var repository = new AtprotoRecordRepository(context);

        List<AtprotoOutboundRecordOwnership> byActor = await repository.GetLiveGroundedEventOwnershipsForActorAsync(
            sourceEvent.ActorId,
            CancellationToken.None);
        List<AtprotoOutboundRecordOwnership> byActorAndDid =
            await repository.GetLiveGroundedEventOwnershipsForActorAndDidAsync(
                sourceEvent.ActorId,
                context.AtprotoRecords.Local.Single(record => record.Id == recordId).Did,
                CancellationToken.None);

        await Assert.That(byActor).HasSingleItem();
        await Assert.That(byActor[0].AtprotoRecordId).IsEqualTo(recordId);
        await Assert.That(byActorAndDid).HasSingleItem();
        await Assert.That(byActorAndDid[0].AtprotoRecordId).IsEqualTo(recordId);
    }

    [Test]
    public async Task UnsettledEventMutations_SelectExactActorAndDidForGlobalModeration()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        Guid recordId = AddOwnedLocalEcho(context, tenantId, "unsettled-source", hasActiveTenantUser: true);
        AtprotoOutboundRecordOwnership ownership = context.AtprotoOutboundRecordOwnerships.Local.Single();
        Explore.Domain.Event sourceEvent = context.Events.Local.Single(value => value.Id == ownership.SourceEntityId);
        string did = context.AtprotoRecords.Local.Single(record => record.Id == recordId).Did;
        context.PdsSyncOutbox.AddRange(
            CreateUnsettledOutbox(tenantId, sourceEvent.Id, ownership.UserId, did, PdsSyncStatus.Pending),
            CreateUnsettledOutbox(tenantId, sourceEvent.Id, ownership.UserId, "did:plc:other", PdsSyncStatus.Processing));
        await context.SaveChangesAsync();
        var repository = new PdsSyncOutboxRepository(context);

        IReadOnlyList<PdsSyncOutbox> byActor = await repository.GetUnsettledEventMutationsForActorAsync(
            sourceEvent.ActorId,
            AtprotoEventPublicationPlanner.EventSourceType,
            AtprotoEventPublicationPlanner.EventCollection,
            CancellationToken.None);
        IReadOnlyList<PdsSyncOutbox> byActorAndDid =
            await repository.GetUnsettledEventMutationsForActorAndDidAsync(
                sourceEvent.ActorId,
                did,
                AtprotoEventPublicationPlanner.EventSourceType,
                AtprotoEventPublicationPlanner.EventCollection,
                CancellationToken.None);

        await Assert.That(byActor).Count().IsEqualTo(2);
        await Assert.That(byActorAndDid).HasSingleItem();
        await Assert.That(byActorAndDid[0].Did).IsEqualTo(did);
    }

    private static PdsSyncOutbox CreateUnsettledOutbox(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        string did,
        PdsSyncStatus status) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Did = did,
            Collection = AtprotoEventPublicationPlanner.EventCollection,
            RecordKey = Guid.CreateVersion7().ToString("N"),
            Operation = PdsSyncOperation.Create,
            PayloadHash = "hash",
            IdempotencyKey = Guid.CreateVersion7().ToString("N"),
            PdsHost = "https://pds.example/",
            SourceEntityType = AtprotoEventPublicationPlanner.EventSourceType,
            SourceEntityId = eventId,
            SourceVersion = Guid.CreateVersion7(),
            Status = status,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = 10
        };

    private static ExploreDbContext CreateContext(Guid tenantId) => new(
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"atproto-projection-visibility-{Guid.NewGuid():N}")
            .Options)
    {
        TenantContext = new TestTenantContext(tenantId)
    };

    private static Guid AddProjection(
        ExploreDbContext context,
        Guid tenantId,
        string key,
        Action<Actor>? configureEventActor = null,
        Action<AtprotoIdentity>? configureIdentity = null,
        string? identityDid = null,
        Actor? identityActor = null,
        long presentationSourceVersion = 2,
        EventStatusEnum eventStatus = EventStatusEnum.Draft,
        VisibilityTypeEnum visibility = VisibilityTypeEnum.Public,
        bool eventIsDeleted = false,
        bool detachRecordAnchor = false)
    {
        Actor eventActor = Actor($"event-owner-{key}");
        configureEventActor?.Invoke(eventActor);
        string recordDid = $"did:plc:in-memory-{key}";
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = recordDid,
            Collection = "community.lexicon.calendar.event",
            RecordKey = $"record-{key}",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = 2,
            RecordJson = "{\"name\":\"In-memory visibility test\"}",
            RecordHash = new string('a', 64),
            UpdatedAt = DateTime.UtcNow
        };
        Actor identityOwner = identityActor ?? eventActor;
        var identity = new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse(identityDid ?? recordDid))
        {
            Id = Guid.CreateVersion7(),

            ActorId = identityOwner.Id,
            Actor = identityOwner,
            PdsHost = "https://pds.example.test",
            IsActive = true,
            LastResolvedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        configureIdentity?.Invoke(identity);
        var projection = new AtprotoEventProjection
        {
            AtprotoRecordId = record.Id,
            Name = $"In-memory {key}",
            CreatedAt = DateTimeOffset.UtcNow,
            StartsAt = DateTimeOffset.UtcNow.AddDays(1),
            SourceVersion = 2,
            MaterializedAt = DateTime.UtcNow
        };
        var presentation = new AtprotoRecordTenantPresentation
        {
            TenantId = tenantId,
            AtprotoRecordId = record.Id,
            IsVisible = true,
            SourceVersion = presentationSourceVersion,
            EvaluatedAt = DateTime.UtcNow
        };
        var importedEvent = new Explore.Domain.Event(eventStatus)
        {
            Id = Guid.CreateVersion7(),
            Title = $"In-memory {key}",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated,
            PublicCode = "ATPROTO",
            ActorId = eventActor.Id,
            Actor = eventActor,
            TenantId = tenantId,
            Tenant = null!,
            VisibilityTypeId = (int)visibility,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            AtprotoRecordId = detachRecordAnchor ? null : record.Id,
            AtprotoRecord = detachRecordAnchor ? null : record,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = eventIsDeleted,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(identity, projection, presentation, importedEvent);
        return record.Id;
    }

    private static Guid AddOwnedLocalEcho(
        ExploreDbContext context,
        Guid tenantId,
        string key,
        bool hasActiveTenantUser,
        bool ownershipMatchesEvent = true,
        bool hasInboundEvidence = false)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = $"{key}@example.test", FirstName = "Local", LastName = "Owner" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id,
            Pii = new ActorPii { DisplayName = $"local-owner-{key}" },
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = $"did:plc:local-{key}",
            Collection = "community.lexicon.calendar.event",
            RecordKey = $"local-{key}",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = 2,
            RecordJson = "{\"name\":\"Local echo visibility test\"}",
            RecordHash = new string('b', 64),
            UpdatedAt = DateTime.UtcNow
        };
        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = $"Local echo {key}",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            PublicCode = "ATPROTO",
            ActorId = actor.Id,
            Actor = actor,
            TenantId = tenantId,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            AtprotoRecordId = record.Id,
            AtprotoRecord = record,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var projection = new AtprotoEventProjection
        {
            AtprotoRecordId = record.Id,
            Name = $"Local echo {key}",
            CreatedAt = DateTimeOffset.UtcNow,
            StartsAt = DateTimeOffset.UtcNow.AddDays(1),
            SourceVersion = 2,
            MaterializedAt = DateTime.UtcNow
        };
        var ownership = new AtprotoOutboundRecordOwnership
        {
            AtprotoRecordId = record.Id,
            TenantId = tenantId,
            UserId = user.Id,
            SourceEntityType = "Event",
            SourceEntityId = ownershipMatchesEvent ? @event.Id : Guid.CreateVersion7(),
            SourceVersion = @event.ConcurrencyStamp,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.AddRange(user, actor, record, @event, projection, ownership);

        if (hasInboundEvidence)
        {
            context.AddRange(
                new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse(record.Did))
                {
                    Id = Guid.CreateVersion7(),

                    ActorId = actor.Id,
                    Actor = actor,
                    PdsHost = "https://pds.example.test",
                    IsActive = true,
                    LastResolvedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                },
                new AtprotoRecordTenantPresentation
                {
                    TenantId = tenantId,
                    AtprotoRecordId = record.Id,
                    IsVisible = true,
                    SourceVersion = record.SourceVersion,
                    EvaluatedAt = DateTime.UtcNow
                });
        }

        if (hasActiveTenantUser)
        {
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                UserId = user.Id,
                User = user,
                ActorId = actor.Id,
                Actor = actor,
                StatusId = (int)TenantUserStatusEnum.Active
            });
        }

        return record.Id;
    }

    private static Actor Actor(string displayName) => new()
    {
        Id = Guid.CreateVersion7(),
        ActorTypeId = (int)ActorTypeEnum.Bot,
        ActorType = null!,
        Pii = new ActorPii { DisplayName = displayName },
        CreatedAt = DateTime.UtcNow,
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static AtprotoEventProjectionQuery PublicQuery() => new(
        20,
        null,
        null,
        null,
        null,
        AtprotoEventTemporalFilter.All,
        AtprotoEventDiscoverySort.Date,
        false,
        DateTimeOffset.UtcNow);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
