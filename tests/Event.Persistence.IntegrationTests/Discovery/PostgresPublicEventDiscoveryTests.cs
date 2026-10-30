using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Discovery;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgresPublicEventDiscoveryTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task RuntimeRepositoryPagesCanonicalCandidatesAndRetainsCountOnEmptyPage(int pageNumber)
    {
        var seeded = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        using var services = ReaderServices(context);
        var repository = new EventRepository(context, publicDiscoveryBindings: new(services),
            nativeEnrollment: fixture.NativeQueueEnrollment);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var (items, total) = await repository.GetEventsWithDetailsPaged(pageNumber, 1,
                Query().And(EventFilter.SearchTerm("native")).And(EventFilter.SearchTerm("candidate")));
            await Assert.That(total).IsEqualTo(2);
            await Assert.That(items.Count).IsEqualTo(pageNumber <= 2 ? 1 : 0);
            if (pageNumber <= 2)
            {
                await Assert.That(items.Single().Id).IsEqualTo(pageNumber == 1 ? seeded.First : seeded.Second);
                await Assert.That(items.Single().Sessions.Count).IsEqualTo(1);
                await Assert.That(items.Single().DiscoveryPresentation).IsNotNull();
            }
            await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync($"SELECT * FROM {new HumanPrincipalSourceSql(context).Table(typeof(Explore.Domain.Event))}"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task RuntimeRepositoryPageReappliesCriteriaAndCurrentSourceAuthority()
    {
        var seeded = await SeedAsync();
        await using (var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
        {
            using var services = ReaderServices(context);
            var repository = new EventRepository(context, publicDiscoveryBindings: new(services),
                nativeEnrollment: fixture.NativeQueueEnrollment);
            var positive = await repository.GetEventsWithDetailsPaged(1, 25, Query());
            await Assert.That(positive.TotalCount).IsEqualTo(2);
            var absent = await repository.GetEventsWithDetailsPaged(1, 25,
                Query().And(EventFilter.SearchTerm("absent")));
            await Assert.That(absent.TotalCount).IsEqualTo(0);
            await Assert.That(absent.Items.Count).IsEqualTo(0);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
        }
        await using (var writer = fixture.CreateDbContext())
        {
            (await writer.TenantUsers.SingleAsync(row => row.TenantId == seeded.Tenant)).IsDeleted = true;
            await writer.SaveChangesAsync();
        }
        await using var current = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        using var currentServices = ReaderServices(current);
        var currentRepository = new EventRepository(current, publicDiscoveryBindings: new(currentServices),
            nativeEnrollment: fixture.NativeQueueEnrollment);
        var withdrawn = await currentRepository.GetEventsWithDetailsPaged(1, 25, Query());
        await Assert.That(withdrawn.TotalCount).IsEqualTo(0);
        await Assert.That(withdrawn.Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeRepositoryPageKeepsDefaultDescendingOccurrenceOrder()
    {
        var seeded = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            var later = await writer.EventSessions.SingleAsync(row => row.EventId == seeded.Second);
            later.StartTime = Now.AddDays(1);
            later.EndTime = Now.AddDays(1).AddHours(1);
            later.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            await writer.SaveChangesAsync();
        }
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        using var services = ReaderServices(context);
        var repository = new EventRepository(context, publicDiscoveryBindings: new(services),
            nativeEnrollment: fixture.NativeQueueEnrollment);
        var query = new EventQuerySpecification().WithOccurrence(new(null, null, TemporalView.All, Now, null));
        var (items, total) = await repository.GetEventsWithDetailsPaged(1, 1, query);
        await Assert.That(total).IsEqualTo(2);
        await Assert.That(items.Single().Id).IsEqualTo(seeded.Second);
    }

    private ServiceProvider ReaderServices(ExploreDbContext context) => new ServiceCollection()
        .AddSingleton(context)
        .AddSingleton(new ExploreDbContextExecutionLease())
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Migrator:Username"] = fixture.NativeQueueEnrollment.MigratorLogin,
            ["Database:Global:Username"] = fixture.NativeQueueEnrollment.GlobalLogin,
            ["Database:Runtime:Username"] = fixture.NativeQueueEnrollment.RuntimeLogin,
            ["Database:EmailPolicyInstanceMutation:Username"] = fixture.NativeQueueEnrollment.EmailInstanceMutationLogin,
            ["Database:EmailPolicyTenantMutation:Username"] = fixture.NativeQueueEnrollment.EmailTenantMutationLogin
        }).Build()).BuildServiceProvider();

    [Test]
    public async Task RuntimeReadsPublicCandidatesWithoutPrivateSourcePrivileges()
    {
        var seeded = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var rows = await SeekAsync(context, Query());
            await Assert.That(rows.Count).IsEqualTo(2);
            await Assert.That(rows[0].Id).IsEqualTo(seeded.First);
            await Assert.That(rows[0].Sessions.Single().Id).IsEqualTo(seeded.Session);
            await Assert.That(rows[0].DiscoveryPresentation).IsNotNull();
            await Assert.That(rows[0].Actor).IsNull();
            await Assert.That(rows[0].FeaturedImage).IsNull();
            await Assert.That(rows[0].TicketCatalogVersions.Count).IsEqualTo(0);
            await Assert.That(rows[0].Content).IsNull();
            await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }
        foreach (var type in new[] { typeof(Explore.Domain.Event), typeof(EventSession), typeof(Actor), typeof(ActorPii),
                     typeof(TenantUser), typeof(AtprotoRecord), typeof(StorageObject), typeof(RegistrationFormVersion),
                     typeof(ParticipationRequirementAttachment), typeof(EventTicketCatalogVersion) })
        {
            string table = new HumanPrincipalSourceSql(context).Table(type);
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync($"SELECT * FROM {table} LIMIT 1"));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
        }
        var ownerDenied = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync($"SET ROLE {PostgresPublicEventDiscoveryContract.OwnerRole}"));
        await Assert.That(ownerDenied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task RepeatedClausesIntersectAndDescendingTiesKeepSourceAscending()
    {
        var seeded = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var query = Query().And(EventFilter.SearchTerm("native"))
            .And(EventFilter.SearchTerm("candidate")).SortByDescending(EventSort.Title);
        var first = (await SeekAsync(context, query, take: 1)).Single();
        await Assert.That(first.Id).IsEqualTo(seeded.First);
        var after = new EventDiscoverySourceCursor(first.Id, first.Title, first.TotalViews,
            first.CreatedAt, first.Sessions.Single().StartTime);
        var second = (await SeekAsync(context, query, after, 1)).Single();
        await Assert.That(second.Id).IsEqualTo(seeded.Second);
        await Assert.That((await SeekAsync(context, query.And(EventFilter.SearchTerm("absent")))).Count).IsEqualTo(0);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task DirectAndAspectCriteriaHaveIndependentPositiveAndNegativeWitnesses()
    {
        var seeded = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            writer.Add(new EventIslamicAspect { Id = seeded.First, IncludesQuranRecitation = true });
            writer.Add(new EventTechAspect { Id = seeded.First, RequiresLaptop = true, TechStackTags = ".NET, PostgreSQL" });
            await writer.SaveChangesAsync();
        }
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That((await SeekAsync(context, Query().And(EventFilter.Formats([(int)EventFormatEnum.Local])))).Count).IsEqualTo(2);
        await Assert.That((await SeekAsync(context, Query().And(EventFilter.Statuses([(int)EventStatusEnum.Cancelled])))).Count).IsEqualTo(0);
        await Assert.That((await SeekAsync(context, Query().And(IslamicAspectFilter.IncludesQuranRecitation()))).Single().Id).IsEqualTo(seeded.First);
        await Assert.That((await SeekAsync(context, Query().And(TechAspectFilter.RequiresLaptop())
            .And(TechAspectFilter.TechStack(".NET")).And(TechAspectFilter.TechStack("PostgreSQL")))).Single().Id).IsEqualTo(seeded.First);
        await Assert.That((await SeekAsync(context, Query().And(AspectPresenceFilter.HasBothAspects()))).Single().Id).IsEqualTo(seeded.First);
        await Assert.That((await SeekAsync(context, Query().And(TechAspectFilter.TechStack("absent")))).Count).IsEqualTo(0);
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(TemporalView.Upcoming, 0)]
    [Arguments(TemporalView.Ongoing, 1)]
    [Arguments(TemporalView.Past, 1)]
    [Arguments(TemporalView.UpcomingAndOngoing, 1)]
    [Arguments(TemporalView.All, 2)]
    public async Task HalfOpenEndAndExplicitOpenEndRetainTemporalMeaning(TemporalView view, int expected)
    {
        var seeded = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            var sessions = await writer.EventSessions.Where(row => row.TenantId == seeded.Tenant).ToArrayAsync();
            foreach (var session in sessions)
            {
                session.StartTime = Now.AddHours(-1);
                session.EndTime = session.EventId == seeded.First ? Now : null;
                session.EndTimeType = session.EventId == seeded.First ? SessionEndTimeType.Fixed : SessionEndTimeType.OpenEnded;
                session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            }
            await writer.SaveChangesAsync();
        }
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var rows = await SeekAsync(context, Query().WithOccurrence(new(null, null, view, Now, null)));
        await Assert.That(rows.Count).IsEqualTo(expected);
        if (view == TemporalView.Past) await Assert.That(rows.Single().Id).IsEqualTo(seeded.First);
        if (view == TemporalView.Ongoing) await Assert.That(rows.Single().Id).IsEqualTo(seeded.Second);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task DateAndLocationMustMatchOneSessionAndDescendingStillSelectsItsEarliestOccurrence()
    {
        var seeded = await SeedAsync();
        Guid areaId = Guid.CreateVersion7(), dayId = Guid.CreateVersion7(), selectedSession;
        await using (var writer = fixture.CreateDbContext())
        {
            var first = await writer.Events.SingleAsync(value => value.Id == seeded.First);
            var second = await writer.Events.SingleAsync(value => value.Id == seeded.Second);
            var userId = await writer.TenantUsers.Where(value => value.TenantId == seeded.Tenant).Select(value => value.UserId).SingleAsync();
            var wanted = Venue(areaId);
            var elsewhere = Venue(Guid.CreateVersion7());
            var firstArea = Placement(first, wanted);
            var firstElsewhere = Placement(first, elsewhere);
            var secondArea = Placement(second, wanted);
            var firstCurrent = await writer.EventSessions.SingleAsync(value => value.EventId == first.Id);
            firstCurrent.AssignEventLocation(firstElsewhere);
            var firstOld = Session(first, Now.AddYears(-1));
            firstOld.AssignEventLocation(firstArea);
            writer.Add(firstOld);
            var secondCurrent = await writer.EventSessions.SingleAsync(value => value.EventId == second.Id);
            selectedSession = secondCurrent.Id;
            secondCurrent.AssignEventLocation(secondArea);
            var secondLater = Session(second, Now.AddHours(2));
            secondLater.AssignEventLocation(secondArea);
            writer.Add(secondLater);
            var day = new EventDay
            {
                Id = dayId,
                TenantId = seeded.Tenant,
                Tenant = null!,
                EventId = second.Id,
                Event = second,
                IsPublished = true,
                LocalDate = DateOnly.FromDateTime(Now.UtcDateTime)
            };
            writer.Add(day);
            secondCurrent.EventDayId = day.Id;
            secondLater.EventDayId = day.Id;
            await writer.SaveChangesAsync();

            Location Venue(Guid id)
            {
                var location = new Location
                {
                    Id = id,
                    TenantId = seeded.Tenant,
                    FullName = "Public venue",
                    City = "Brussels",
                    Country = "BE"
                };
                location.SetManualAddress("Venue street", "1000");
                writer.Add(location);
                return location;
            }
            EventLocation Placement(Explore.Domain.Event entity, Location location)
            {
                var placement = EventLocation.CreatePhysical(seeded.Tenant, entity.Id, location.Id, userId, Now.UtcDateTime);
                placement.ChangeDisclosurePolicy(EventLocationDisclosureFields.City | EventLocationDisclosureFields.Country,
                    LocationDisclosureAudienceEnum.Never, null, placement.PolicyVersion, userId,
                    EventLocationDisclosureAuditReasonEnum.GovernanceTightening, Now.UtcDateTime, needsPrivacyReview: false);
                writer.Add(placement);
                return placement;
            }
            EventSession Session(Explore.Domain.Event entity, DateTimeOffset start)
            {
                var session = new EventSession(EventSessionStatusEnum.Published)
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = seeded.Tenant,
                    Tenant = null!,
                    EventId = entity.Id,
                    Event = entity,
                    StartTime = start,
                    EndTime = start.AddHours(1),
                    EndTimeType = SessionEndTimeType.Fixed
                };
                session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
                return session;
            }
        }
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var query = Query().WithOccurrence(new(today, today, TemporalView.All, Now, [areaId])).SortByDescending(EventSort.Date);
        await using (var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var result = (await SeekAsync(context, query)).Single();
            await Assert.That(result.Id).IsEqualTo(seeded.Second);
            await Assert.That(result.Sessions.Single().Id).IsEqualTo(selectedSession);
            await Assert.That(result.DiscoveryAdditionalSessionCount).IsEqualTo(1);
            await Assert.That(result.Sessions.Single().Location).IsNull();
            await transaction.RollbackAsync();
        }
        await using (var writer = fixture.CreateDbContext())
        {
            var day = await writer.EventDays.SingleAsync(value => value.Id == dayId);
            day.IsPublished = false;
            await writer.SaveChangesAsync();
        }
        await using var current = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var currentTransaction = await current.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That((await SeekAsync(current, query)).Count).IsEqualTo(0);
        await currentTransaction.RollbackAsync();
    }

    [Test]
    public async Task CustomPropertyTextClausesIntersectWithoutProbingPrivateProjections()
    {
        var seeded = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            foreach (var exposure in new[] { ExposureLevel.Public, ExposureLevel.Internal })
            {
                var definition = new EventCustomPropertyDefinition
                {
                    Id = Guid.CreateVersion7(),
                    EventId = seeded.First,
                    TenantId = seeded.Tenant,
                    Namespace = "discovery",
                    Key = exposure == ExposureLevel.Public ? "topic" : "private",
                    DisplayName = "Topic",
                    PropertyType = PropertyType.Text,
                    IsActive = true,
                    ExposureLevel = exposure,
                    IsSearchable = true,
                    IsFilterable = true,
                    InstantiatedAt = Now
                };
                var value = new EventCustomPropertyValue
                {
                    Id = Guid.CreateVersion7(),
                    EventId = seeded.First,
                    TenantId = seeded.Tenant,
                    EventCustomPropertyDefinitionId = definition.Id,
                    TextValue = "community",
                    Ordinal = 0
                };
                writer.AddRange(definition, value, new EventCustomPropertyProjection
                {
                    Id = Guid.CreateVersion7(),
                    EventId = seeded.First,
                    TenantId = seeded.Tenant,
                    EventCustomPropertyDefinitionId = definition.Id,
                    EventCustomPropertyValueId = value.Id,
                    Namespace = definition.Namespace,
                    Key = definition.Key,
                    PropertyType = PropertyType.Text,
                    ExposureLevel = exposure,
                    IsFilterable = true,
                    IsSearchable = true,
                    TextValue = "community",
                    NormalizedValue = "community",
                    UpdatedAt = Now.UtcDateTime
                });
            }
            await writer.SaveChangesAsync();
        }
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var query = Query().And(EventCustomPropertyProjectionFilter.ExactMatch("discovery", "topic", "community"))
            .And(EventCustomPropertyProjectionFilter.TextSearch("discovery", "topic", "COMM"))
            .And(EventCustomPropertyProjectionFilter.GlobalTextSearch("unity"))
            .And(EventCustomPropertyProjectionFilter.Exists("discovery", "topic"));
        await Assert.That((await SeekAsync(context, query)).Single().Id).IsEqualTo(seeded.First);
        await Assert.That((await SeekAsync(context, query.And(
            EventCustomPropertyProjectionFilter.ExactMatch("discovery", "topic", "different")))).Count).IsEqualTo(0);
        await Assert.That((await SeekAsync(context, Query().And(
            EventCustomPropertyProjectionFilter.Exists("discovery", "private")))).Count).IsEqualTo(0);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task RemoteNullTimeBranchesAndVisibleLocalEchoRemainDistinct()
    {
        var seeded = await SeedAsync();
        string prefix = Guid.CreateVersion7().ToString("N")[..28];
        Guid nullTime = Guid.ParseExact(prefix + "0001", "N");
        Guid dated = Guid.ParseExact(prefix + "0002", "N");
        Guid echo = Guid.ParseExact(prefix + "0003", "N");
        await using (var writer = fixture.CreateDbContext())
        {
            var local = await writer.Events.SingleAsync(entity => entity.Id == seeded.First);
            var member = await writer.TenantUsers.SingleAsync(entity => entity.TenantId == seeded.Tenant);
            string did = $"did:web:{seeded.Tenant:N}.example.org";
            writer.Add(new AtprotoIdentity(AtprotoDid.Parse(did))
            {
                Id = Guid.CreateVersion7(),
                ActorId = local.ActorId,
                IsActive = true,
                PdsHost = "https://pds.example.org",
                LastResolvedAt = Now.UtcDateTime
            });
            foreach (Guid id in new[] { nullTime, dated, echo })
            {
                writer.Add(new AtprotoRecord
                {
                    Id = id,
                    Did = did,
                    Collection = "community.lexicon.calendar.event",
                    RecordKey = id.ToString("N"),
                    Direction = id == echo ? AtprotoRecordDirection.Outbound : AtprotoRecordDirection.Inbound,
                    Provenance = id == echo ? AtprotoRecordProvenance.LocalLifecycle : AtprotoRecordProvenance.Jetstream,
                    SourceVersion = 1,
                    UpdatedAt = Now.UtcDateTime,
                    RecordJson = "{\"privateProviderBody\":true}"
                });
                writer.Add(new AtprotoEventProjection
                {
                    AtprotoRecordId = id,
                    Name = "remote candidate",
                    CreatedAt = Now,
                    StartsAt = id == dated ? Now : null,
                    SourceUrl = "https://events.example.org/event",
                    SourceVersion = 1,
                    MaterializedAt = Now.UtcDateTime
                });
                writer.Add(new AtprotoRecordTenantPresentation
                {
                    TenantId = seeded.Tenant,
                    AtprotoRecordId = id,
                    IsVisible = true,
                    SourceVersion = 1,
                    EvaluatedAt = Now.UtcDateTime
                });
                if (id == echo)
                {
                    local.AtprotoRecordId = id;
                    writer.Add(new AtprotoOutboundRecordOwnership
                    {
                        TenantId = seeded.Tenant,
                        AtprotoRecordId = id,
                        UserId = member.UserId,
                        SourceEntityType = nameof(Explore.Domain.Event),
                        SourceEntityId = local.Id,
                        SourceVersion = Guid.CreateVersion7(),
                        CreatedAt = Now.UtcDateTime,
                        UpdatedAt = Now.UtcDateTime
                    });
                }
                else
                {
                    writer.Add(new Explore.Domain.Event(EventStatusEnum.Draft)
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = seeded.Tenant,
                        Tenant = null!,
                        ActorId = local.ActorId,
                        Actor = null!,
                        Title = "Imported candidate",
                        PublicCode = id.ToString("N")[^12..],
                        AtprotoRecordId = id,
                        EventStatus = null!,
                        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                        VisibilityType = null!,
                        EventFormatId = (int)EventFormatEnum.Local,
                        EventFormat = null!,
                        EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated
                    });
                }
            }
            await writer.SaveChangesAsync();
        }
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var binding = Binding(context);
        var query = new AtprotoEventProjectionQuery(1, null, null, null, null,
            AtprotoEventTemporalFilter.All, AtprotoEventDiscoverySort.Date, false, Now);
        var first = (await PublicEventDiscoveryProviderOperations.SeekRemoteAsync(context, binding, query, null, default)).Single();
        await Assert.That(first.AtprotoRecordId).IsEqualTo(nullTime);
        var cursor = new EventDiscoverySourceCursor(first.AtprotoRecordId, first.Name, 0, first.CreatedAt.UtcDateTime, first.StartsAt);
        await Assert.That((await PublicEventDiscoveryProviderOperations.SeekRemoteAsync(context, binding, query, cursor, default))
            .Single().AtprotoRecordId).IsEqualTo(dated);
        var descending = query with { SortDescending = true };
        var dateFirst = (await PublicEventDiscoveryProviderOperations.SeekRemoteAsync(context, binding, descending, null, default)).Single();
        await Assert.That(dateFirst.AtprotoRecordId).IsEqualTo(dated);
        cursor = new(dateFirst.AtprotoRecordId, dateFirst.Name, 0, dateFirst.CreatedAt.UtcDateTime, dateFirst.StartsAt);
        await Assert.That((await PublicEventDiscoveryProviderOperations.SeekRemoteAsync(context, binding, descending, cursor, default))
            .Single().AtprotoRecordId).IsEqualTo(nullTime);
        await Assert.That((await PublicEventDiscoveryProviderOperations.MembersRemoteAsync(context, binding, query, [echo], default)).Count).IsEqualTo(0);
        var visible = await PublicEventDiscoveryProviderOperations.VisibleRemoteRecordsAsync(context, binding, [echo], default);
        await Assert.That(visible.Single().AtprotoRecordId).IsEqualTo(echo);
        await Assert.That(visible.Single().AtprotoRecord).IsNull();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task BindingRejectsDifferentOwnerAndMutationPurposeWithoutInventingAnonymousLease()
    {
        var seeded = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        var binding = Binding(context);
        binding.RequireOriginalOwner(context);
        await using var other = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        {
            binding.RequireOriginalOwner(other);
            return Task.CompletedTask;
        });
        var mutation = new ExploreDbContextExecutionLease();
        mutation.Bind(context, seeded.Tenant, PolicyMutationPurpose.EmailDelivery);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        {
            _ = new PublicEventDiscoveryReadBinding(context, mutation, fixture.NativeQueueEnrollment);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ContinuationUsesExactPairAndNeverSubstitutesAnotherSession()
    {
        var seeded = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var binding = Binding(context);
        var exact = await PublicEventDiscoveryProviderOperations.MembersLocalAsync(
            context, binding, Query(), new Dictionary<Guid, Guid> { [seeded.First] = seeded.Session }, default);
        await Assert.That(exact.Single().Sessions.Single().Id).IsEqualTo(seeded.Session);
        var crossed = await PublicEventDiscoveryProviderOperations.MembersLocalAsync(context, binding, Query(),
            new Dictionary<Guid, Guid> { [seeded.Second] = seeded.Session }, default);
        await Assert.That(crossed.Count).IsEqualTo(0);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task RevokedActorMembershipRemovesPreviouslyCapturedCandidate()
    {
        var seeded = await SeedAsync();
        await using (var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.That((await SeekAsync(context, Query())).Count).IsEqualTo(2);
            await transaction.RollbackAsync();
        }
        await using (var writer = fixture.CreateDbContext())
        {
            var membership = await writer.TenantUsers.SingleAsync(row => row.TenantId == seeded.Tenant);
            membership.IsDeleted = true;
            await writer.SaveChangesAsync();
        }
        await using var current = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var currentTransaction = await current.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That((await PublicEventDiscoveryProviderOperations.MembersLocalAsync(
            current, Binding(current), Query(), new Dictionary<Guid, Guid> { [seeded.First] = seeded.Session },
            default)).Count).IsEqualTo(0);
        await currentTransaction.RollbackAsync();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    public async Task WrongLoginCannotBorrowPublicDiscovery(PrimaryDatabaseRole role)
    {
        var seeded = await SeedAsync();
        await using var context = Open(role, seeded.Tenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => SeekAsync(context, Query()));
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task ChangedTenantSettingFailsInsteadOfReadingAnotherTenant()
    {
        var seeded = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await context.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id',{0},true)", Guid.CreateVersion7().ToString());
        var denied = await Assert.ThrowsAsync<PostgresException>(() => SeekAsync(context, Query()));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("body")]
    [Arguments("acl")]
    [Arguments("role")]
    [Arguments("policy")]
    public async Task ReadinessRejectsDriftRatherThanRepairingIt(string mutation)
    {
        await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        string function = PostgresPublicEventDiscoveryContract.FunctionSql(migration, "seek_local");
        string change = mutation switch
        {
            "body" => $"""
                GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {PostgresPublicEventDiscoveryContract.OwnerRole};
                SET LOCAL ROLE {PostgresPublicEventDiscoveryContract.OwnerRole};
                CREATE OR REPLACE FUNCTION {function}(p_tenant uuid,p_request jsonb) RETURNS SETOF jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $changed$ BEGIN RETURN NEXT pg_catalog.jsonb_build_object(); END; $changed$;
                RESET ROLE;
                REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {PostgresPublicEventDiscoveryContract.OwnerRole};
                """,
            "acl" => $"GRANT EXECUTE ON FUNCTION {function}(uuid,jsonb) TO PUBLIC",
            "role" => $"ALTER ROLE {PostgresPublicEventDiscoveryContract.OwnerRole} INHERIT",
            "policy" => $"ALTER POLICY public_event_discovery_read ON {sql.Table(typeof(Explore.Domain.Event))} USING (true)",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresPublicEventDiscoveryContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
    }

    private Task<IReadOnlyList<Explore.Domain.Event>> SeekAsync(ExploreDbContext context,
        EventQuerySpecification query, EventDiscoverySourceCursor? after = null, int take = 100) =>
        PublicEventDiscoveryProviderOperations.SeekLocalAsync(context, Binding(context), query, after, take, default);

    private PublicEventDiscoveryReadBinding Binding(ExploreDbContext context) =>
        new(context, new ExploreDbContextExecutionLease(), fixture.NativeQueueEnrollment);

    private static EventQuerySpecification Query() => new EventQuerySpecification()
        .And(EventFilter.PubliclyDiscoverable()).And(EventFilter.Status((int)EventStatusEnum.Published))
        .WithOccurrence(new(null, null, TemporalView.All, Now, null)).SortBy(EventSort.Title);

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        builder.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        builder.UseNpgsql(options => options.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        return new(builder.Options) { TenantContext = new SelectedTenant(tenant) };
    }

    private async Task<(Guid Tenant, Guid First, Guid Second, Guid Session)> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresPublicEventDiscoveryContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Candidate reader",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User { Id = Guid.CreateVersion7(), Pii = null! };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            User = user,
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Public organizer" }
        };
        context.AddRange(tenant, user, actor, new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active
        });
        string prefix = Guid.CreateVersion7().ToString("N")[..28];
        var first = AddEvent(Guid.ParseExact(prefix + "0001", "N"));
        var second = AddEvent(Guid.ParseExact(prefix + "0002", "N"));
        var firstSession = AddSession(first);
        AddSession(second);
        await context.SaveChangesAsync();
        return (tenant.Id, first.Id, second.Id, firstSession.Id);

        Explore.Domain.Event AddEvent(Guid id)
        {
            var entity = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = id,
                TenantId = tenant.Id,
                Tenant = tenant,
                ActorId = actor.Id,
                Actor = actor,
                Title = "native candidate",
                Content = "Not returned in a public card",
                PublicCode = id.ToString("N")[^12..],
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                Timezone = "UTC",
                CreatedAt = Now.UtcDateTime
            };
            context.Add(entity);
            return entity;
        }
        EventSession AddSession(Explore.Domain.Event entity)
        {
            var session = new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                Event = entity,
                EventId = entity.Id,
                StartTime = Now,
                EndTime = Now.AddHours(1),
                EndTimeType = SessionEndTimeType.Fixed
            };
            session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            context.Add(session);
            return session;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
