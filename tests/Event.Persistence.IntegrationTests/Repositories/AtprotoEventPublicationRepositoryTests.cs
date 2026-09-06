// ABOUTME: PostgreSQL-backed contract tests for the bounded ATProto event publication graph query.
// ABOUTME: Proves exact tenant selection, entity-first results, no tracking, and a fixed SQL command budget.

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class AtprotoEventPublicationRepositoryTests(PostgreSqlContainerFixture fixture)
{
    private const int MaximumPublicationQueryCount = 27;

    [Test]
    [NotInParallel("PersistenceDb")]
    public async Task GetAtprotoPublicationGraphAsync_ReturnsUntrackedTenantEntityGraphWithinBudget()
    {
        await fixture.ResetAsync();
        (Guid tenantId, Guid eventId) = await SeedEventAsync();
        var counter = new CommandCountingInterceptor();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(counter)
            .Options;
        await using var context = new ExploreDbContext(options);

        AtprotoEventPublicationEntityGraph? graph = await new EventRepository(context)
            .GetAtprotoPublicationGraphAsync(tenantId, eventId, CancellationToken.None);

        await Assert.That(graph).IsNotNull();
        await Assert.That(graph!.Event.Id).IsEqualTo(eventId);
        await Assert.That(graph.Event.TenantId).IsEqualTo(tenantId);
        await Assert.That(graph.EventLocations).Count().IsEqualTo(2);
        await Assert.That(graph.EventLocations.Single(location => location.LocationId.HasValue).Location!.Rooms).IsNotEmpty();
        await Assert.That(graph.Sessions).Count().IsEqualTo(2);
        await Assert.That(graph.Days).Count().IsEqualTo(2);
        await Assert.That(graph.SessionGroups).IsNotEmpty();
        await Assert.That(graph.SessionGroupSessions).Count().IsEqualTo(2);
        await Assert.That(graph.AgendaItems).IsNotEmpty();
        await Assert.That(graph.SessionAgendaItems).IsNotEmpty();
        await Assert.That(graph.Categories).IsNotEmpty();
        await Assert.That(graph.Tags).IsNotEmpty();
        await Assert.That(graph.SessionCategories).IsNotEmpty();
        await Assert.That(graph.SessionTags).IsNotEmpty();
        await Assert.That(graph.SessionLanguages).IsNotEmpty();
        await Assert.That(graph.SessionSpeakers).IsNotEmpty();
        await Assert.That(graph.CustomPropertyDefinitions.Single().Options).IsNotEmpty();
        await Assert.That(graph.CustomPropertyDefinitions.Single().Values).IsNotEmpty();
        await Assert.That(graph.SessionCustomPropertyDefinitions.Single().Options).IsNotEmpty();
        await Assert.That(graph.SessionCustomPropertyDefinitions.Single().Values).IsNotEmpty();
        await Assert.That(context.ChangeTracker.Entries()).IsEmpty();
        await Assert.That(counter.ReaderCommandCount).IsLessThanOrEqualTo(MaximumPublicationQueryCount);
    }

    private async Task<(Guid TenantId, Guid EventId)> SeedEventAsync()
    {
        await using var context = fixture.CreateDbContext();
        await LookupTableSeeder.SeedAsync(context);
        var tenant = new Tenant
        {
            FullName = "ATProto projection tenant",
            Slug = $"atproto-projection-{Guid.NewGuid():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"atproto-projection-{Guid.NewGuid():N}@example.test",
                FirstName = "Projection",
                LastName = "Owner"
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();

        Actor actor = CreateActor(user.Id, "Projection owner");
        context.Actors.Add(actor);
        SetForeignKeyIfPresent(context, actor, "TenantId", tenant.Id);
        await context.SaveChangesAsync();

        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Bounded publication graph",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        context.Events.Add(eventEntity);
        SetForeignKeyIfPresent(context, eventEntity, "EventProvenanceTypeId", 1);
        await context.SaveChangesAsync();

        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(7);
        var calculator = new EventScheduleProjectionCalculator();
        var physicalLocation = new Location
        {
            Id = Guid.CreateVersion7(),
            FullName = "ATProto venue",
            Country = "Belgium",
            City = "Brussels",
            Timezone = "Europe/Brussels",
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        physicalLocation.SetProviderAddress(
            "Projection street 1",
            "1000",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(50.85, 4.35));
        var room = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            LocationId = physicalLocation.Id,
            Location = physicalLocation,
            Name = "Projection room",
            Slug = "projection-room",
            Description = "Full graph room",
            Capacity = 120,
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        physicalLocation.Rooms.Add(room);
        context.Locations.Add(physicalLocation);
        await context.SaveChangesAsync();

        EventLocation physicalPlacement = EventLocation.CreatePhysical(
            tenant.Id, eventEntity.Id, physicalLocation.Id, user.Id, DateTime.UtcNow);
        physicalPlacement.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.All,
            LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
            null,
            1,
            user.Id,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            DateTime.UtcNow.AddMinutes(1));
        EventLocation tbaPlacement = EventLocation.CreateToBeAnnounced(
            tenant.Id, eventEntity.Id, user.Id, DateTime.UtcNow);
        context.EventLocations.AddRange(physicalPlacement, tbaPlacement);

        var dayOne = new EventDay
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            LocalDate = DateOnly.FromDateTime(start.UtcDateTime),
            Label = "Projection day one",
            Description = "First day",
            BannerText = "Welcome",
            IsPublished = true,
            SortOrder = 1,
            AllowsDayScopeRegistration = true,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var dayTwo = new EventDay
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            LocalDate = DateOnly.FromDateTime(start.AddDays(1).UtcDateTime),
            Label = "Projection day two",
            Description = "Second day",
            BannerText = "Closing",
            IsPublished = true,
            SortOrder = 2,
            AllowsDayScopeRegistration = true,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.EventDays.AddRange(dayOne, dayTwo);

        var sessionOne = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            EventDayId = dayOne.Id,
            TenantId = tenant.Id,
            Tenant = null!,
            Title = "Projection session one",
            SortOrder = 1,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        sessionOne.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "Europe/Brussels", calculator);
        sessionOne.AssignEventLocation(physicalPlacement);
        sessionOne.RoomId = room.Id;
        var sessionTwo = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            EventDayId = dayTwo.Id,
            TenantId = tenant.Id,
            Tenant = null!,
            Title = "Projection session two",
            SortOrder = 2,
            EventSessionKindId = (int)EventSessionKindEnum.Workshop,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        sessionTwo.Reschedule(UtcInstantRange.Create(start.AddDays(1), start.AddDays(1).AddHours(1)), "Europe/Brussels", calculator);
        sessionTwo.AssignEventLocation(tbaPlacement);
        context.EventSessions.AddRange(sessionOne, sessionTwo);

        var group = new EventSessionGroup
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            Name = "Projection track",
            Slug = "projection-track",
            Description = "Full graph track",
            SortOrder = 1,
            IsPublished = true,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        group.AssignEventLocation(physicalPlacement);
        group.RoomId = room.Id;
        context.EventSessionGroups.Add(group);

        var agenda = new EventAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            EventDayId = dayOne.Id,
            Title = "Projection event agenda",
            Description = "Agenda description",
            SortOrder = 1,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        agenda.Reschedule(UtcInstantRange.Create(start.AddHours(1), start.AddHours(2)), "Europe/Brussels", calculator);
        agenda.AssignEventLocation(physicalPlacement);
        agenda.RoomId = room.Id;
        var sessionAgenda = new EventSessionAgendaItem
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = sessionOne.Id,
            EventSession = sessionOne,
            StartTime = start,
            EndTime = start.AddMinutes(30),
            Title = "Projection session agenda",
            Description = "Session agenda description",
            TenantId = tenant.Id,
            Tenant = null!
        };
        sessionAgenda.AssignEventLocation(physicalPlacement);
        context.EventAgendaItems.Add(agenda);
        context.EventSessionAgendaItems.Add(sessionAgenda);
        await context.SaveChangesAsync();

        context.EventSessionGroupSessions.AddRange(
            CreateGroupLink(tenant.Id, eventEntity.Id, group.Id, sessionOne.Id, true, 1),
            CreateGroupLink(tenant.Id, eventEntity.Id, group.Id, sessionTwo.Id, false, 2));
        var category = new Category
        {
            Id = Guid.CreateVersion7(),
            MasterCode = $"ATP-{Guid.NewGuid():N}",
            FullName = "ATProto category",
            TenantId = tenant.Id,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var sessionCategory = new Category
        {
            Id = Guid.CreateVersion7(),
            MasterCode = $"ATP-S-{Guid.NewGuid():N}",
            FullName = "ATProto session category",
            TenantId = tenant.Id,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var tag = new Tag
        {
            Id = Guid.CreateVersion7(),
            MasterCode = $"ATP-{Guid.NewGuid():N}",
            FullName = "ATProto tag",
            TenantId = tenant.Id,
            Tenant = null!
        };
        var sessionTag = new Tag
        {
            Id = Guid.CreateVersion7(),
            MasterCode = $"ATP-S-{Guid.NewGuid():N}",
            FullName = "ATProto session tag",
            TenantId = tenant.Id,
            Tenant = null!
        };
        context.AddRange(category, sessionCategory, tag, sessionTag);
        await context.SaveChangesAsync();

        Language language = await context.Languages.AsNoTracking().FirstAsync();
        context.EventCategories.Add(new Explore.Domain.EventCategories
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            CategoryId = category.Id,
            Category = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        context.EventTags.Add(new Explore.Domain.EventTags
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            TagId = tag.Id,
            Tag = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        context.EventSessionCategories.Add(new EventSessionCategory
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = sessionOne.Id,
            EventSession = null!,
            CategoryId = sessionCategory.Id,
            Category = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow
        });
        context.EventSessionTags.Add(new EventSessionTag
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = sessionOne.Id,
            EventSession = null!,
            TagId = sessionTag.Id,
            Tag = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow
        });
        context.EventSessionLanguages.Add(new EventSessionLanguage
        {
            EventSessionId = sessionOne.Id,
            EventSession = null!,
            LanguageId = language.Id,
            Language = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        context.EventSessionSpeakers.Add(new EventSessionSpeaker
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = sessionOne.Id,
            EventSession = null!,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        });

        var eventDefinition = new EventCustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            EventId = eventEntity.Id,
            Event = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            Namespace = "atproto",
            Key = "event-field",
            DisplayName = "Event field",
            PropertyType = PropertyType.Text,
            IsActive = true,
            ExposureLevel = ExposureLevel.Public,
            SortOrder = 1,
            InstantiatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var sessionDefinition = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            EventSessionId = sessionOne.Id,
            EventSession = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            Namespace = "atproto",
            Key = "session-field",
            DisplayName = "Session field",
            PropertyType = PropertyType.Option,
            IsActive = true,
            ExposureLevel = ExposureLevel.Public,
            SortOrder = 1,
            InstantiatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(eventDefinition, sessionDefinition);
        await context.SaveChangesAsync();

        var eventOption = new EventCustomPropertyOption
        {
            Id = Guid.CreateVersion7(),
            EventCustomPropertyDefinitionId = eventDefinition.Id,
            Namespace = "atproto",
            Key = "event-option",
            DisplayName = "Event option",
            Value = "event-option",
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var sessionOption = new EventSessionCustomPropertyOption
        {
            Id = Guid.CreateVersion7(),
            EventSessionCustomPropertyDefinitionId = sessionDefinition.Id,
            Namespace = "atproto",
            Key = "session-option",
            DisplayName = "Session option",
            Value = "session-option",
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(eventOption, sessionOption);
        await context.SaveChangesAsync();
        context.EventCustomPropertyValues.Add(new EventCustomPropertyValue
        {
            Id = Guid.CreateVersion7(),
            EventCustomPropertyDefinitionId = eventDefinition.Id,
            EventId = eventEntity.Id,
            TenantId = tenant.Id,
            Tenant = null!,
            Ordinal = 1,
            TextValue = "Event value",
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        context.EventSessionCustomPropertyValues.Add(new EventSessionCustomPropertyValue
        {
            Id = Guid.CreateVersion7(),
            EventSessionCustomPropertyDefinitionId = sessionDefinition.Id,
            EventSessionId = sessionOne.Id,
            TenantId = tenant.Id,
            Tenant = null!,
            Ordinal = 1,
            OptionId = sessionOption.Id,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        await context.SaveChangesAsync();
        return (tenant.Id, eventEntity.Id);
    }

    private static Actor CreateActor(Guid userId, string displayName)
    {
        Actor actor = Activator.CreateInstance<Actor>();
        actor.Pii = new ActorPii { DisplayName = displayName };
        actor.ActorTypeId = 1;
        actor.ActorType = null!;
        actor.UserId = userId;
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

    private static EventSessionGroupSession CreateGroupLink(
        Guid tenantId,
        Guid eventId,
        Guid groupId,
        Guid sessionId,
        bool isPrimary,
        int sortOrder)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventId = eventId,
            Event = null!,
            EventSessionGroupId = groupId,
            EventSessionGroup = null!,
            EventSessionId = sessionId,
            EventSession = null!,
            IsPrimary = isPrimary,
            SortOrder = sortOrder,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow
        };

    private sealed class CommandCountingInterceptor : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}
