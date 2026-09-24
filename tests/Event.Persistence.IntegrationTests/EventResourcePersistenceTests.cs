using Explore.Application.Specifications.EventResources;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions.Enums;
using TUnit.Core;
using TUnit.Core.Interfaces;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
public sealed class EventResourcePersistenceTests(EventResourcePersistenceTests.TestDatabase database)
{
    [Test]
    public async Task AdditionalRelationalRejectsIncompleteAvailability()
    {
        await database.SeedScopeAsync();
        await AssertRejectedAsync(database, resource =>
            Set(resource, nameof(EventResource.AvailabilityStartOffsetTicks), (long?)1));
    }

    [Test]
    public async Task AdditionalRelationalRejectsIncompleteExternalEnvelope()
    {
        await database.SeedScopeAsync();
        await AssertRejectedAsync(database, SetIncompleteExternalEnvelope);
    }

    [Test]
    public async Task AdditionalRelationalRejectsSameEventSiblingSessionTarget()
    {
        ResourceScope scope = await database.SeedScopeAsync();
        await AssertSameEventSiblingSessionTargetRejectedAsync(database, scope);
    }

    [Test]
    public async Task AdditionalRelationalRejectsEmptyPublicTitle()
    {
        await database.SeedScopeAsync();
        await AssertRejectedAsync(database, resource =>
        {
            Set(resource, nameof(EventResource.DisclosureModeId), (int)EventResourceDisclosureModeEnum.Teaser);
            Set(resource, nameof(EventResource.PublicTitle), " ");
        });
    }

    [Test]
    public async Task DirectRelationalOwnershipAndShapeConstraintsRejectMalformedRows()
    {
        await AssertInvalidPersistedOwnershipAsync(database);
    }

    [Test]
    public async Task MultipleNullDraftsSucceedButDuplicateNonNullAttachmentIsRejected()
    {
        ResourceScope scope = await database.SeedScopeAsync();
        await AssertNullDraftsAndExclusiveAttachmentAsync(database, scope);
    }

    [Test]
    public async Task RepositoryRoundTripRehydratesImmutableRulesAndRejectsOptimisticConflict()
    {
        ResourceScope scope = await database.SeedScopeAsync();
        Guid resourceId;
        Guid laterResourceId;
        await using (ExploreDbContext seed = database.CreateContext())
        {
            EventResource resource = CreateDraft(scope.TenantAId, scope.EventAId);
            resourceId = resource.Id;
            EventResource later = CreateDraft(scope.TenantAId, scope.EventAId);
            later.UpdateMetadata(Metadata("Later") with { SortOrder = 1 },
                later.ConcurrencyStamp, scope.ActorId, UtcNow);
            laterResourceId = later.Id;
            seed.EventResources.AddRange(resource, later);
            await seed.SaveChangesAsync();
        }

        await using (ExploreDbContext read = database.CreateContext())
        {
            var repository = new EventResourceRepository(read);
            EventResource loaded = (await repository.GetByIdAsync(scope.TenantAId, scope.EventAId, resourceId, default))!;
            await Assert.That(loaded.AudienceRules).Count().IsEqualTo(1);
            var exposed = (ICollection<EventResourceAudienceRule>)loaded.AudienceRules;
            await Assert.That(() => exposed.Clear()).Throws<NotSupportedException>();

            IReadOnlyList<EventResource> page = await repository.ListCandidatesAsync(
                scope.TenantAId,
                10,
                new EventResourceQuerySpecification()
                    .And(EventResourceFilter.Event(scope.EventAId))
                    .SortBy(EventResourceSort.SortOrder),
                default);
            await Assert.That(page.Select(item => item.Id)).Contains(resourceId);
            IReadOnlyList<EventResource> after = await repository.ListCandidatesAsync(
                scope.TenantAId, 10,
                new EventResourceQuerySpecification()
                    .And(EventResourceFilter.Event(scope.EventAId))
                    .And(EventResourceFilter.After(new EventResourceCursor(0, resourceId)))
                    .SortBy(EventResourceSort.SortOrder),
                default);
            await Assert.That(after.Count).IsEqualTo(1);
            await Assert.That(after[0].Id).IsEqualTo(laterResourceId);
        }

        await using ExploreDbContext firstContext = database.CreateContext();
        await using ExploreDbContext staleContext = database.CreateContext();
        var firstRepository = new EventResourceRepository(firstContext);
        var staleRepository = new EventResourceRepository(staleContext);
        EventResource first = (await firstRepository.GetByIdForUpdateAsync(scope.TenantAId, scope.EventAId, resourceId, default))!;
        EventResource stale = (await staleRepository.GetByIdForUpdateAsync(scope.TenantAId, scope.EventAId, resourceId, default))!;
        Guid resourceVersion = first.ConcurrencyStamp;
        await using (ExploreDbContext unrelated = database.CreateContext())
        {
            DomainEvent sibling = await unrelated.Events.SingleAsync(item => item.Id == scope.EventCId);
            sibling.Description = "Independent event edit";
            await unrelated.SaveChangesAsync();
        }
        await Assert.That(first.ConcurrencyStamp).IsEqualTo(resourceVersion);
        first.UpdateMetadata(Metadata("First"), first.ConcurrencyStamp, scope.ActorId, UtcNow.AddMinutes(1));
        await firstContext.SaveChangesAsync();
        stale.UpdateMetadata(Metadata("Stale"), stale.ConcurrencyStamp, scope.ActorId, UtcNow.AddMinutes(2));
        await Assert.That(() => staleContext.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        await using ExploreDbContext verify = database.CreateContext();
        await Assert.That((await verify.EventResources.AsNoTracking().SingleAsync(item => item.Id == resourceId)).Title)
            .IsEqualTo("First");
        await Assert.That((await verify.Events.AsNoTracking().SingleAsync(item => item.Id == scope.EventCId)).Description)
            .IsEqualTo("Independent event edit");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CandidateCursorRejectsIncompatibleOrdering(bool descending)
    {
        await using var context = database.CreateContext();
        var repository = new EventResourceRepository(context);
        var specification = descending
            ? new EventResourceQuerySpecification().SortByDescending(EventResourceSort.SortOrder)
            : new EventResourceQuerySpecification().SortBy(EventResourceSort.Title);
        await Assert.That(() => repository.ListCandidatesAsync(
            Guid.CreateVersion7(), 1, specification, default)).Throws<ArgumentException>();
    }

    [Test]
    public async Task EqualSortOrderCursorVisitsEveryCandidateExactlyOnce()
    {
        ResourceScope scope = await database.SeedScopeAsync();
        await using (var seed = database.CreateContext())
        {
            seed.EventResources.AddRange(
                CreateDraft(scope.TenantAId, scope.EventAId),
                CreateDraft(scope.TenantAId, scope.EventAId),
                CreateDraft(scope.TenantAId, scope.EventAId));
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var repository = new EventResourceRepository(context);
        var specification = new EventResourceQuerySpecification().And(EventResourceFilter.Event(scope.EventAId));
        var expected = await repository.ListCandidatesAsync(scope.TenantAId, 10, specification, default);
        await Assert.That(expected.Count).IsEqualTo(3);
        EventResourceCursor? cursor = null;
        foreach (var resource in expected)
        {
            var next = cursor is null ? specification : specification.And(EventResourceFilter.After(cursor));
            var page = await repository.ListCandidatesAsync(scope.TenantAId, 1, next, default);
            await Assert.That(page.Count).IsEqualTo(1);
            await Assert.That(page[0].Id).IsEqualTo(resource.Id);
            cursor = new EventResourceCursor(page[0].SortOrder, page[0].Id);
        }
        await Assert.That(await repository.ListCandidatesAsync(scope.TenantAId, 1,
            specification.And(EventResourceFilter.After(cursor!)), default)).IsEmpty();
    }

    [Test]
    public async Task RepositoryRejectsOversizedAuthorityRequestRatherThanTruncating()
    {
        await using ExploreDbContext context = database.CreateContext();
        var repository = new EventResourceRepository(context);
        Guid[] oversized = Enumerable.Range(0, 501).Select(_ => Guid.CreateVersion7()).ToArray();

        await Assert.That(() => repository.GetSessionsAsync(
            Guid.CreateVersion7(), Guid.CreateVersion7(), oversized, 500, default))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ChildPolicyWritesAdvanceAggregateConcurrencyAtTheSameInstant()
    {
        ResourceScope scope = await database.SeedScopeAsync();
        Guid resourceId;
        await using (var seed = database.CreateContext())
        {
            var resource = CreateDraft(scope.TenantAId, scope.EventAId);
            resource.ReplacePolicy(resource.Availability, resource.AudienceRules,
                resource.ConcurrencyStamp, scope.ActorId, UtcNow);
            resourceId = resource.Id;
            seed.EventResources.Add(resource);
            await seed.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var staleContext = database.CreateContext();
        var firstRepository = new EventResourceRepository(firstContext);
        var staleRepository = new EventResourceRepository(staleContext);
        var first = (await firstRepository.GetByIdForUpdateAsync(scope.TenantAId, scope.EventAId, resourceId, default))!;
        var stale = (await staleRepository.GetByIdForUpdateAsync(scope.TenantAId, scope.EventAId, resourceId, default))!;
        var originalStamp = first.ConcurrencyStamp;
        first.ReplacePolicy(first.Availability,
            [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resourceId,
                EventResourceAudienceKindEnum.AuthenticatedTenantMember)],
            first.ConcurrencyStamp, scope.ActorId, UtcNow);
        firstRepository.Update(first);
        await firstContext.SaveChangesAsync();
        await Assert.That(first.ConcurrencyStamp).IsNotEqualTo(originalStamp);

        // A metadata-only contender cannot fail merely because the old child row was deleted.
        stale.UpdateMetadata(Metadata("Stale after audience change"),
            stale.ConcurrencyStamp, scope.ActorId, UtcNow);
        staleRepository.Update(stale);
        var conflict = await Assert.That(() => staleContext.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        await Assert.That(conflict!.Entries.Any(entry => entry.Entity is EventResource)).IsTrue();
    }

    [Test]
    public async Task AmbientTenantFiltersCannotBeOverriddenByExplicitReadArguments()
    {
        ResourceScope scope = await database.SeedScopeAsync();
        var resource = CreateDraft(scope.TenantAId, scope.EventAId);
        await using (var seed = database.CreateContext())
        {
            seed.EventResources.Add(resource);
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var repository = new EventResourceRepository(context);
        context.TenantContext = new ResourceTenantContext(scope.TenantAId);
        await Assert.That(await repository.GetByIdAsync(scope.TenantAId, scope.EventAId, resource.Id, default)).IsNotNull();
        context.TenantContext = new ResourceTenantContext(scope.TenantBId);
        await Assert.That(await repository.GetByIdAsync(scope.TenantAId, scope.EventAId, resource.Id, default)).IsNull();
        context.TenantContext = null;
        await Assert.That(await repository.GetByIdAsync(scope.TenantAId, scope.EventAId, resource.Id, default)).IsNull();
    }

    [Test]
    public async Task LookupRepairAndLargeFileSizeRemainStable()
    {
        ResourceScope scope = await database.SeedScopeAsync();
        await using ExploreDbContext context = database.CreateContext();
        EventResourceKind removed = await context.EventResourceKinds.SingleAsync(value => value.Id == 13);
        context.EventResourceKinds.Remove(removed);
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedAsync(context);
        await Assert.That(await context.EventResourceKinds.CountAsync()).IsEqualTo(13);
        await Assert.That((await context.StorageObjects.AsNoTracking().SingleAsync(value => value.Id == scope.StorageAId)).Size)
            .IsEqualTo((long)int.MaxValue + 42L);
    }

    internal static EventResource CreateDraft(Guid tenantId, Guid eventId, Guid? sessionId = null,
        EventResourceAudienceRule? rule = null)
    {
        Guid resourceId = rule?.EventResourceId ?? Guid.CreateVersion7();
        rule ??= EventResourceAudienceRule.Create(tenantId, eventId, resourceId, EventResourceAudienceKindEnum.Public);
        return EventResource.CreateDraft(
            resourceId,
            tenantId,
            eventId,
            sessionId,
            Metadata("Portable resource"),
            EventResourceDeliveryTypeEnum.StoredFile,
            EventResourceAvailability.Create(),
            [rule],
            Guid.CreateVersion7(),
            UtcNow);
    }

    internal static async Task AssertInvalidPersistedOwnershipAsync(Func<ExploreDbContext> contextFactory)
    {
        await using TestDatabase database = TestDatabase.CreateProvider(contextFactory);
        await AssertInvalidPersistedOwnershipAsync(database);
    }

    private static async Task AssertInvalidPersistedOwnershipAsync(TestDatabase database)
    {
        ResourceScope scope = await database.SeedScopeAsync();
        await AssertRejectedAsync(database, resource => SetField(resource, "_tenantId", scope.TenantBId));
        await AssertRejectedAsync(database, resource => Set(resource, nameof(EventResource.EventSessionId), scope.SessionBId));
        await AssertRejectedAsync(database, resource => Set(resource, nameof(EventResource.StorageObjectId), scope.StorageBId));
        await AssertRejectedAsync(database, resource => Set(resource, nameof(EventResource.AccessibleAlternativeEventResourceId), scope.AlternativeBId));
        await AssertRejectedAsync(database, resource => Set(resource, nameof(EventResource.PublicationStateId), (int)EventResourcePublicationStateEnum.Published));
        await AssertSiblingSessionRuleRejectedAsync(database, scope);
        await AssertSiblingAdmissionTargetRejectedAsync(database, scope);
        await AssertForeignTicketQualifierRejectedAsync(database, scope, sameTenant: false);
        await AssertForeignTicketQualifierRejectedAsync(database, scope, sameTenant: true);
        await AssertSameTenantForeignEventDayTargetRejectedAsync(database, scope);
        await AssertRejectedAsync(database, resource =>
            Set(resource, nameof(EventResource.AvailabilityStartOffsetTicks), (long?)1));
        await AssertRejectedAsync(database, resource =>
            Set(resource, nameof(EventResource.AvailabilityEndOffsetTicks), (long?)1));
        await AssertRejectedAsync(database, SetIncompleteExternalEnvelope);
        await AssertSameEventSiblingSessionTargetRejectedAsync(database, scope);
        await AssertRejectedAsync(database, resource =>
        {
            Set(resource, nameof(EventResource.DisclosureModeId), (int)EventResourceDisclosureModeEnum.Teaser);
            Set(resource, nameof(EventResource.PublicTitle), " ");
        });
        await AssertRejectedAsync(database, resource =>
        {
            resource.SetStoredFile(scope.StorageAId, resource.ConcurrencyStamp, scope.ActorId, UtcNow);
            Set(resource, nameof(EventResource.IsDeleted), true);
        });
        await AssertNullDraftsAndExclusiveAttachmentAsync(database, scope);
    }

    private static readonly DateTime UtcNow = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    private static EventResourceMetadata Metadata(string title, Guid? alternativeId = null) => new()
    {
        Title = title,
        Kind = EventResourceKindEnum.GeneralDocument,
        DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly,
        AccessibleAlternativeEventResourceId = alternativeId
    };

    private static async Task AssertRejectedAsync(TestDatabase database, Action<EventResource> corrupt)
    {
        await using ExploreDbContext context = database.CreateContext();
        EventResource resource = CreateDraft(database.Scope!.TenantAId, database.Scope.EventAId);
        context.EventResources.Add(resource);
        corrupt(resource);
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    private static async Task AssertSiblingSessionRuleRejectedAsync(TestDatabase database, ResourceScope scope)
    {
        await using ExploreDbContext context = database.CreateContext();
        Guid id = Guid.CreateVersion7();
        EventResourceAudienceRule rule = EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, id,
            EventResourceAudienceKindEnum.SessionSpeaker, scope.SessionAId);
        EventResource resource = CreateDraft(scope.TenantAId, scope.EventAId, scope.SessionAId, rule);
        context.EventResources.Add(resource);
        Set(rule, nameof(EventResourceAudienceRule.EventSessionId), scope.SessionA2Id);
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    private static async Task AssertSiblingAdmissionTargetRejectedAsync(TestDatabase database, ResourceScope scope)
    {
        await using ExploreDbContext context = database.CreateContext();
        Guid id = Guid.CreateVersion7();
        EventResourceAudienceRule rule = EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, id,
            EventResourceAudienceKindEnum.CheckedInParticipant,
            targetType: AdmissionTargetTypeEnum.Event,
            targetId: scope.TargetAId);
        EventResource resource = CreateDraft(scope.TenantAId, scope.EventAId, rule: rule);
        context.EventResources.Add(resource);
        Set(rule, nameof(EventResourceAudienceRule.AdmissionTargetId), scope.TargetBId);
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    private static async Task AssertForeignTicketQualifierRejectedAsync(
        TestDatabase database, ResourceScope scope, bool sameTenant)
    {
        await using ExploreDbContext context = database.CreateContext();
        Guid id = Guid.CreateVersion7();
        EventResourceAudienceRule rule = EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, id,
            EventResourceAudienceKindEnum.TicketHolder,
            scope.SessionAId,
            scope.TicketTypeAId,
            ticketCatalogVersionId: scope.CatalogAId);
        EventResource resource = CreateDraft(scope.TenantAId, scope.EventAId, rule: rule);
        context.EventResources.Add(resource);
        Set(rule, nameof(EventResourceAudienceRule.EventTicketCatalogVersionId),
            sameTenant ? scope.CatalogCId : scope.CatalogBId);
        Set(rule, nameof(EventResourceAudienceRule.EventTicketTypeId),
            sameTenant ? scope.TicketTypeCId : scope.TicketTypeBId);
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    private static async Task AssertSameTenantForeignEventDayTargetRejectedAsync(TestDatabase database, ResourceScope scope)
    {
        await using var context = database.CreateContext();
        var validId = Guid.CreateVersion7();
        var validRule = EventResourceAudienceRule.Create(scope.TenantAId, scope.EventCId, validId,
            EventResourceAudienceKindEnum.CheckedInParticipant, targetType: AdmissionTargetTypeEnum.EventDay,
            targetId: scope.TargetCId, targetScopeId: scope.DayCId);
        context.EventResources.Add(CreateDraft(scope.TenantAId, scope.EventCId, rule: validRule));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var invalidId = Guid.CreateVersion7();
        var invalidRule = EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, invalidId,
            EventResourceAudienceKindEnum.CheckedInParticipant, targetType: AdmissionTargetTypeEnum.EventDay,
            targetId: scope.TargetCId, targetScopeId: scope.DayCId);
        context.EventResources.Add(CreateDraft(scope.TenantAId, scope.EventAId, rule: invalidRule));
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    private static async Task AssertNullDraftsAndExclusiveAttachmentAsync(TestDatabase database, ResourceScope scope)
    {
        await using ExploreDbContext context = database.CreateContext();
        EventResource first = CreateDraft(scope.TenantAId, scope.EventAId);
        EventResource second = CreateDraft(scope.TenantAId, scope.EventAId);
        context.EventResources.AddRange(first, second);
        await context.SaveChangesAsync();

        EventResource attached = CreateDraft(scope.TenantAId, scope.EventAId);
        attached.SetStoredFile(scope.StorageAId, attached.ConcurrencyStamp, scope.ActorId, UtcNow);
        context.EventResources.Add(attached);
        await context.SaveChangesAsync();

        EventResource duplicate = CreateDraft(scope.TenantAId, scope.EventAId);
        duplicate.SetStoredFile(scope.StorageAId, duplicate.ConcurrencyStamp, scope.ActorId, UtcNow);
        context.EventResources.Add(duplicate);
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    private static void SetIncompleteExternalEnvelope(EventResource resource)
    {
        Set(resource, nameof(EventResource.EventResourceDeliveryTypeId), (int)EventResourceDeliveryTypeEnum.ExternalLink);
        Set(resource, nameof(EventResource.ExternalDestinationCiphertext), "synthetic-envelope");
        Set(resource, nameof(EventResource.ExternalDestinationSafeOrigin), "https://example.test");
    }

    private static async Task AssertSameEventSiblingSessionTargetRejectedAsync(TestDatabase database, ResourceScope scope)
    {
        await using ExploreDbContext context = database.CreateContext();
        var target = AdmissionTarget.Create(Guid.CreateVersion7(), scope.TenantAId, scope.EventAId,
            AdmissionTargetTypeEnum.EventSession, null, scope.SessionA2Id);
        context.AdmissionTargets.Add(target);
        await context.SaveChangesAsync();

        var resourceId = Guid.CreateVersion7();
        var rule = EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resourceId,
            EventResourceAudienceKindEnum.CheckedInParticipant, scope.SessionAId,
            targetType: AdmissionTargetTypeEnum.EventSession, targetId: target.Id);
        context.EventResources.Add(CreateDraft(scope.TenantAId, scope.EventAId, scope.SessionAId, rule));
        await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    private static void Set<TEntity, TValue>(TEntity entity, string property, TValue value)
        where TEntity : class => typeof(TEntity).GetProperty(property)!.SetValue(entity, value);

    private static void SetField<TEntity, TValue>(TEntity entity, string field, TValue value)
        where TEntity : class => typeof(TEntity).GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(entity, value);

    public sealed class TestDatabase : IAsyncInitializer, IAsyncDisposable
    {
        private SqliteConnection? _connection;
        private Func<ExploreDbContext> _contextFactory = null!;
        private DbContextOptions<ExploreDbContext>? _options;

        public TestDatabase() { }

        private TestDatabase(SqliteConnection? connection, Func<ExploreDbContext> contextFactory)
        {
            _connection = connection;
            _contextFactory = contextFactory;
        }

        internal ResourceScope? Scope { get; private set; }

        public async Task InitializeAsync()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = $"event-resources-{Guid.CreateVersion7():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString());
            _connection = connection;
            await connection.OpenAsync();
            DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("Explore.Persistence.Migrations.Sqlite"))
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var context = new ExploreDbContext(options);
            await context.Database.MigrateAsync();
            await LookupTableSeeder.SeedAsync(context);
            // Share immutable schema only; keep provider caching disabled and every context's state separate.
            options = TestDbContextOptions.Create(options).UseModel(context.Model).Options;
            _options = options;
            _contextFactory = () =>
            {
                var created = new ExploreDbContext(options);
                created.EnableTenantFilterBypass("Event resource relational invariant test.");
                return created;
            };
        }

        public static TestDatabase CreateProvider(Func<ExploreDbContext> contextFactory) =>
            new(null, contextFactory);

        public ExploreDbContext CreateContext() => _contextFactory();

        public ExploreDbContext CreateIndependentContext()
        {
            if (_options is null || _connection is null)
                throw new InvalidOperationException("Independent connections require the initialized SQLite fixture.");
            var options = TestDbContextOptions.Create(_options)
                .UseSqlite(_connection.ConnectionString).Options;
            var context = new ExploreDbContext(options);
            context.EnableTenantFilterBypass("Event resource separate-connection revocation test.");
            return context;
        }

        public ExploreDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            if (_options is null) throw new InvalidOperationException("Interceptors require the initialized SQLite fixture.");
            DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create(_options)
                .AddInterceptors(interceptors).Options;
            var context = new ExploreDbContext(options);
            context.EnableTenantFilterBypass("Event resource relational invariant test.");
            return context;
        }

        internal async Task<ResourceScope> SeedScopeAsync()
        {
            await using ExploreDbContext context = CreateContext();
            Tenant tenantA = NewTenant("resource-a");
            Tenant tenantB = NewTenant("resource-b");
            var user = new User { Pii = new UserPii { Email = $"resource-{Guid.CreateVersion7():N}@example.test", FirstName = "Resource", LastName = "Owner" } };
            context.AddRange(tenantA, tenantB, user);
            await SaveSeedAsync(context, "tenants/users");
            var actor = new Actor { ActorTypeId = 1, ActorType = null!, UserId = user.Id, Pii = new ActorPii { DisplayName = "Resource Owner" } };
            context.Actors.Add(actor);
            await SaveSeedAsync(context, "actor");

            DomainEvent eventA = NewEvent(tenantA.Id, actor.Id, "Resource A");
            DomainEvent eventB = NewEvent(tenantB.Id, actor.Id, "Resource B");
            DomainEvent eventC = NewEvent(tenantA.Id, actor.Id, "Same-tenant sibling event");
            context.Events.AddRange(eventA, eventB, eventC);
            await SaveSeedAsync(context, "events");

            EventSession sessionA = NewSession(tenantA, eventA);
            EventSession sessionA2 = NewSession(tenantA, eventA);
            EventSession sessionB = NewSession(tenantB, eventB);
            context.EventSessions.AddRange(sessionA, sessionA2, sessionB);
            EventTicketCatalogVersion catalogA = NewCatalog(tenantA.Id, eventA.Id, out EventTicketType ticketA);
            EventTicketCatalogVersion catalogB = NewCatalog(tenantB.Id, eventB.Id, out EventTicketType ticketB);
            EventTicketCatalogVersion catalogC = NewCatalog(tenantA.Id, eventC.Id, out EventTicketType ticketC);
            context.EventTicketCatalogVersions.AddRange(catalogA, catalogB, catalogC);
            var dayC = new EventDay
            {
                Id = Guid.CreateVersion7(), TenantId = tenantA.Id, Tenant = tenantA,
                EventId = eventC.Id, Event = eventC, LocalDate = DateOnly.FromDateTime(UtcNow),
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            context.EventDays.Add(dayC);
            AdmissionTarget targetA = AdmissionTarget.Create(Guid.CreateVersion7(), tenantA.Id, eventA.Id, AdmissionTargetTypeEnum.Event, null, null);
            AdmissionTarget targetB = AdmissionTarget.Create(Guid.CreateVersion7(), tenantB.Id, eventB.Id, AdmissionTargetTypeEnum.Event, null, null);
            AdmissionTarget targetC = AdmissionTarget.Create(Guid.CreateVersion7(), tenantA.Id, eventC.Id,
                AdmissionTargetTypeEnum.EventDay, dayC.Id, null);
            context.AdmissionTargets.AddRange(targetA, targetB, targetC);
            FileType fileType = await context.FileTypes.FirstAsync();
            StorageObject storageA = NewStorage(tenantA, fileType, (long)int.MaxValue + 42L);
            StorageObject storageB = NewStorage(tenantB, fileType, 10);
            context.StorageObjects.AddRange(storageA, storageB);
            await SaveSeedAsync(context, "session/catalog/target/storage graph");

            EventResource alternativeB = CreateDraft(tenantB.Id, eventB.Id);
            context.EventResources.Add(alternativeB);
            await context.SaveChangesAsync();

            Scope = new ResourceScope(tenantA.Id, tenantB.Id, eventA.Id, eventB.Id, sessionA.Id, sessionA2.Id,
                sessionB.Id, storageA.Id, storageB.Id, alternativeB.Id, targetA.Id, targetB.Id,
                catalogA.Id, catalogB.Id, ticketA.Id, ticketB.Id, actor.Id,
                eventC.Id, dayC.Id, targetC.Id, catalogC.Id, ticketC.Id);
            return Scope;
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }

        private static async Task SaveSeedAsync(ExploreDbContext context, string stage)
        {
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException exception)
            {
                throw new InvalidOperationException($"Event resource seed failed at {stage}: {exception.InnerException}", exception);
            }
        }

        private static Tenant NewTenant(string prefix) => new()
        {
            Id = Guid.CreateVersion7(),
            FullName = prefix,
            Slug = $"{prefix}-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };

        private static DomainEvent NewEvent(Guid tenantId, Guid actorId, string title) => new(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            ActorId = actorId,
            Actor = null!,
            Title = title,
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        private static EventSession NewSession(Tenant tenant, DomainEvent owner) => new(EventSessionStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            EventId = owner.Id,
            Event = owner,
            Title = "Resource session",
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        private static EventTicketCatalogVersion NewCatalog(Guid tenantId, Guid eventId, out EventTicketType ticketType)
        {
            EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
            ticketType = EventTicketType.Create(Guid.CreateVersion7(), tenantId, catalog.Id, "Resource ticket", "USD",
                TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None,
                null, null, null, false, false, null, null, null, null);
            catalog.AddTicketType(ticketType, null);
            return catalog;
        }

        private static StorageObject NewStorage(Tenant tenant, FileType fileType, long size) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            FileTypeId = fileType.Id,
            FileType = fileType,
            Uri = $"private://{Guid.CreateVersion7():N}",
            Provider = StorageProviders.Local,
            FullName = "resource.pdf",
            SafeDisplayName = "resource.pdf",
            Extension = ".pdf",
            ContentType = "application/pdf",
            Size = size,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    private sealed record ResourceTenantContext(Guid TenantId) : ITenantContext;

    internal sealed record ResourceScope(
        Guid TenantAId,
        Guid TenantBId,
        Guid EventAId,
        Guid EventBId,
        Guid SessionAId,
        Guid SessionA2Id,
        Guid SessionBId,
        Guid StorageAId,
        Guid StorageBId,
        Guid AlternativeBId,
        Guid TargetAId,
        Guid TargetBId,
        Guid CatalogAId,
        Guid CatalogBId,
        Guid TicketTypeAId,
        Guid TicketTypeBId,
        Guid ActorId,
        Guid EventCId,
        Guid DayCId,
        Guid TargetCId,
        Guid CatalogCId,
        Guid TicketTypeCId);
}
