using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Repositories;

namespace Event.Persistence.IntegrationTests.Federation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoEventProjectionVisibilityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task VisibleQuery_RequiresCurrentTenantPresentationAndExactActiveActorIdentity()
    {
        await fixture.ResetAsync();
        VisibilitySeed seed = await SeedAsync();
        await using ExploreDbContext context =
            fixture.CreateTenantFilteredDbContext(new StaticTenantContext(seed.TenantId));
        var repository = new AtprotoEventProjectionRepository(context);

        (IReadOnlyList<AtprotoEventProjection> discovery, int totalCount) =
            await repository.GetPublicWindowAsync(PublicQuery(), CancellationToken.None);
        IReadOnlyList<AtprotoEventProjection> echoes = await repository.GetVisibleByRecordIdsAsync(
            seed.AllRecordIds,
            CancellationToken.None);
        AtprotoEventProjection? source = await repository.GetVisibleByRecordIdAsync(
            seed.VisibleRecordId,
            CancellationToken.None);

        await Assert.That(discovery).HasSingleItem();
        await Assert.That(discovery[0].AtprotoRecordId).IsEqualTo(seed.VisibleRecordId);
        await Assert.That(totalCount).IsEqualTo(1);
        await Assert.That(echoes).HasSingleItem();
        await Assert.That(echoes[0].AtprotoRecordId).IsEqualTo(seed.VisibleRecordId);
        await Assert.That(source).IsNotNull();
        await Assert.That(source!.AtprotoRecordId).IsEqualTo(seed.VisibleRecordId);

        foreach (Guid deniedRecordId in seed.DeniedRecordIds)
        {
            AtprotoEventProjection? denied = await repository.GetVisibleByRecordIdAsync(
                deniedRecordId,
                CancellationToken.None);
            await Assert.That(denied).IsNull();
        }
    }

    [Test]
    public async Task VisibleQuery_UsesExactEventOwnershipAndCentralEligibilityForLocalEchoes()
    {
        await fixture.ResetAsync();
        DateTime now = new(2026, 7, 28, 10, 0, 0, DateTimeKind.Utc);
        var tenant = Tenant("local-echo");
        await using (ExploreDbContext seedContext = fixture.CreateDbContext())
        {
            seedContext.Tenants.Add(tenant);
            Guid eligible = AddOwnedLocalEcho(seedContext, tenant, "eligible", now, hasActiveTenantUser: true);
            Guid inactiveParticipation = AddOwnedLocalEcho(
                seedContext,
                tenant,
                "inactive-participation",
                now,
                hasActiveTenantUser: false);
            Guid mismatchedSource = AddOwnedLocalEcho(
                seedContext,
                tenant,
                "mismatched-source",
                now,
                hasActiveTenantUser: true,
                ownershipMatchesEvent: false,
                hasInboundEvidence: true);
            await seedContext.SaveChangesAsync();

            await using ExploreDbContext context =
                fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenant.Id));
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
    }

    [Test]
    public async Task VisibleQuery_ExcludesTombstonedOwnedLocalEchoes()
    {
        await fixture.ResetAsync();
        DateTime now = new(2026, 7, 28, 10, 0, 0, DateTimeKind.Utc);
        var tenant = Tenant("tombstoned-local-echo");
        await using (ExploreDbContext seedContext = fixture.CreateDbContext())
        {
            seedContext.Tenants.Add(tenant);
            Guid recordId = AddOwnedLocalEcho(seedContext, tenant, "tombstoned", now, hasActiveTenantUser: true);
            seedContext.AtprotoRecords.Local.Single(record => record.Id == recordId).TombstonedAt = now;
            await seedContext.SaveChangesAsync();

            await using ExploreDbContext context =
                fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenant.Id));
            var repository = new AtprotoEventProjectionRepository(context);

            IReadOnlyList<AtprotoEventProjection> visible = await repository.GetVisibleByRecordIdsAsync(
                [recordId],
                CancellationToken.None);

            await Assert.That(visible).IsEmpty();
            await Assert.That(await repository.GetVisibleByRecordIdAsync(recordId, CancellationToken.None)).IsNull();
        }
    }

    private async Task<VisibilitySeed> SeedAsync()
    {
        DateTime now = new(2026, 7, 28, 10, 0, 0, DateTimeKind.Utc);
        var tenant = Tenant("visibility-a");
        var otherTenant = Tenant("visibility-b");
        await using ExploreDbContext context = fixture.CreateDbContext();
        context.AddRange(tenant, otherTenant);

        Guid visible = AddProjectionGraph(context, tenant, tenant, "valid", now);
        Guid suspendedActor = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "actor-suspended",
            now,
            configureEventActor: actor => actor.IsSuspended = true);
        Guid deletedActor = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "actor-deleted",
            now,
            configureEventActor: actor => actor.IsDeleted = true);
        Guid inactiveIdentity = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "identity-inactive",
            now,
            configureIdentity: identity => identity.IsActive = false);
        Guid suspendedIdentity = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "identity-suspended",
            now,
            configureIdentity: identity => identity.IsSuspended = true);
        Guid deletedIdentity = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "identity-deleted",
            now,
            configureIdentity: identity => identity.IsDeleted = true);
        Guid didMismatch = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "did-mismatch",
            now,
            identityDid: "did:plc:identity-does-not-match-record");
        Guid actorMismatch = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "actor-mismatch",
            now,
            identityActor: Actor("identity-owner", now));
        Guid hiddenPresentation = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "presentation-hidden",
            now,
            isVisible: false);
        Guid stalePresentation = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "presentation-stale",
            now,
            presentationSourceVersion: 1);
        Guid crossTenantPresentation = AddProjectionGraph(
            context,
            tenant,
            otherTenant,
            "presentation-other-tenant",
            now);
        Guid tombstonedRecord = AddProjectionGraph(
            context,
            tenant,
            tenant,
            "record-tombstoned",
            now,
            tombstoned: true);
        await context.SaveChangesAsync();

        return new(
            tenant.Id,
            visible,
            [
                visible,
                suspendedActor,
                deletedActor,
                inactiveIdentity,
                suspendedIdentity,
                deletedIdentity,
                didMismatch,
                actorMismatch,
                hiddenPresentation,
                stalePresentation,
                crossTenantPresentation,
                tombstonedRecord
            ],
            [
                suspendedActor,
                deletedActor,
                inactiveIdentity,
                suspendedIdentity,
                deletedIdentity,
                didMismatch,
                actorMismatch,
                hiddenPresentation,
                stalePresentation,
                crossTenantPresentation,
                tombstonedRecord
            ]);
    }

    private static Guid AddProjectionGraph(
        ExploreDbContext context,
        Tenant eventTenant,
        Tenant presentationTenant,
        string key,
        DateTime now,
        Action<Actor>? configureEventActor = null,
        Action<AtprotoIdentity>? configureIdentity = null,
        string? identityDid = null,
        Actor? identityActor = null,
        bool isVisible = true,
        long presentationSourceVersion = 2,
        bool tombstoned = false)
    {
        Actor eventActor = Actor($"event-owner-{key}", now);
        configureEventActor?.Invoke(eventActor);
        string recordDid = $"did:plc:visibility-{key}";
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = recordDid,
            Collection = "community.lexicon.calendar.event",
            RecordKey = $"record-{key}",
            Cid = $"bafy-visibility-{key}",
            Uri = $"at://{recordDid}/community.lexicon.calendar.event/record-{key}",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = 2,
            RecordJson = "{\"name\":\"Visibility test\"}",
            RecordHash = new string('a', 64),
            IndexedAt = now,
            UpdatedAt = now,
            TombstonedAt = tombstoned ? now : null
        };
        Actor identityOwner = identityActor ?? eventActor;
        var identity = new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse(identityDid ?? recordDid))
        {
            Id = Guid.CreateVersion7(),

            ActorId = identityOwner.Id,
            Actor = identityOwner,
            PdsHost = "https://pds.example.test",
            IsActive = true,
            LastResolvedAt = now,
            LastSeenAt = now,
            CreatedAt = now
        };
        configureIdentity?.Invoke(identity);
        var projection = new AtprotoEventProjection
        {
            AtprotoRecordId = record.Id,
            Name = $"Visibility {key}",
            CreatedAt = new DateTimeOffset(now),
            StartsAt = new DateTimeOffset(now.AddDays(1)),
            SourceUrl = "https://events.example.test/source",
            SourceVersion = 2,
            MaterializedAt = now
        };
        var presentation = new AtprotoRecordTenantPresentation
        {
            TenantId = presentationTenant.Id,
            Tenant = presentationTenant,
            AtprotoRecordId = record.Id,
            AtprotoRecord = record,
            IsVisible = isVisible,
            SourceVersion = presentationSourceVersion,
            EvaluatedAt = now
        };
        var importedEvent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = $"Visibility {key}",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated,
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
            ActorId = eventActor.Id,
            Actor = eventActor,
            TenantId = eventTenant.Id,
            Tenant = eventTenant,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            AtprotoRecordId = record.Id,
            AtprotoRecord = record,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(identity, projection, presentation, importedEvent);
        return record.Id;
    }

    private static Guid AddOwnedLocalEcho(
        ExploreDbContext context,
        Tenant tenant,
        string key,
        DateTime now,
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
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = $"did:plc:local-{key}",
            Collection = "community.lexicon.calendar.event",
            RecordKey = $"local-{key}",
            Cid = $"bafy-local-{key}",
            Uri = $"at://did:plc:local-{key}/community.lexicon.calendar.event/local-{key}",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = 2,
            RecordJson = "{\"name\":\"Local echo visibility test\"}",
            RecordHash = new string('b', 64),
            IndexedAt = now,
            UpdatedAt = now
        };
        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = $"Local echo {key}",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
            ActorId = actor.Id,
            Actor = actor,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            AtprotoRecordId = record.Id,
            AtprotoRecord = record,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var projection = new AtprotoEventProjection
        {
            AtprotoRecordId = record.Id,
            Name = $"Local echo {key}",
            CreatedAt = new DateTimeOffset(now),
            StartsAt = new DateTimeOffset(now.AddDays(1)),
            SourceVersion = 2,
            MaterializedAt = now
        };
        var ownership = new AtprotoOutboundRecordOwnership
        {
            AtprotoRecordId = record.Id,
            TenantId = tenant.Id,
            UserId = user.Id,
            SourceEntityType = "Event",
            SourceEntityId = ownershipMatchesEvent ? @event.Id : Guid.CreateVersion7(),
            SourceVersion = @event.ConcurrencyStamp,
            CreatedAt = now,
            UpdatedAt = now
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
                    LastResolvedAt = now,
                    LastSeenAt = now,
                    CreatedAt = now
                },
                new AtprotoRecordTenantPresentation
                {
                    TenantId = tenant.Id,
                    Tenant = tenant,
                    AtprotoRecordId = record.Id,
                    AtprotoRecord = record,
                    IsVisible = true,
                    SourceVersion = record.SourceVersion,
                    EvaluatedAt = now
                });
        }

        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)(hasActiveTenantUser
                ? TenantUserStatusEnum.Active
                : TenantUserStatusEnum.Removed)
        });

        return record.Id;
    }

    private static Actor Actor(string displayName, DateTime now)
    {
        var externalSubject = new ExternalActorSubject
        {
            Id = Guid.CreateVersion7(),
            FirstObservedAt = now,
            LastObservedAt = now,
            CreatedAt = now
        };
        return new()
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.ExternalUnclassified,
            ActorType = null!,
            ExternalActorSubjectId = externalSubject.Id,
            ExternalActorSubject = externalSubject,
            Pii = new ActorPii { DisplayName = displayName },
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    private static Tenant Tenant(string slug) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = slug,
        Slug = $"{slug}-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static AtprotoEventProjectionQuery PublicQuery() => new(
        50,
        null,
        null,
        null,
        null,
        AtprotoEventTemporalFilter.All,
        AtprotoEventDiscoverySort.Date,
        false,
        new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero));

    private sealed record VisibilitySeed(
        Guid TenantId,
        Guid VisibleRecordId,
        IReadOnlyCollection<Guid> AllRecordIds,
        IReadOnlyCollection<Guid> DeniedRecordIds);

    private sealed record StaticTenantContext(Guid TenantId) : ITenantContext;
}
