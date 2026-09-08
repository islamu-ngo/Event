using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.Events.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EventRedactionConstraintTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RepresentativeRedactedEventGraph_ShouldSatisfyDatabaseConstraints()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event) = await SetupRedactedEventAsync(context);
        var session = new EventSession(EventSessionStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = null!,
            Title = EventRedactionSentinelPolicy.DisplayText,
            Slug = Slug("event-session", Guid.NewGuid()),
            Description = EventRedactionSentinelPolicy.DisplayText,
            SourceTemplateKey = null,
            FeaturedImageId = null,
            TenantId = tenant.Id,
            Tenant = tenant
        };
        var day = new EventDay
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = null!,
            LocalDate = new DateOnly(2026, 9, 1),
            Label = EventRedactionSentinelPolicy.DisplayText,
            Description = EventRedactionSentinelPolicy.DisplayText,
            BannerText = EventRedactionSentinelPolicy.DisplayText,
            BannerImageId = null,
            TenantId = tenant.Id,
            Tenant = null!
        };
        var group = new EventSessionGroup
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = null!,
            Name = EventRedactionSentinelPolicy.DisplayText,
            Slug = Slug("event-session-group", Guid.NewGuid()),
            Description = EventRedactionSentinelPolicy.DisplayText,
            Color = null,
            TenantId = tenant.Id,
            Tenant = null!
        };
        var eventAgendaItem = new EventAgendaItem
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = null!,
            Title = EventRedactionSentinelPolicy.DisplayText,
            Description = EventRedactionSentinelPolicy.DisplayText,
            TenantId = tenant.Id,
            Tenant = null!
        };
        eventAgendaItem.Reschedule(
            UtcInstantRange.Create(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.Zero)),
            "UTC",
            new EventScheduleProjectionCalculator());
        var sessionAgendaItem = new EventSessionAgendaItem
        {
            Id = Guid.NewGuid(),
            EventSessionId = session.Id,
            EventSession = null!,
            StartTime = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.Zero),
            Title = EventRedactionSentinelPolicy.DisplayText,
            Description = EventRedactionSentinelPolicy.DisplayText,
            TenantId = tenant.Id,
            Tenant = null!
        };

        var eventDefinition = CreateEventDefinition(tenant.Id, @event.Id);
        var eventOption = CreateEventOption(eventDefinition.Id);
        var eventValue = new EventCustomPropertyValue
        {
            Id = Guid.NewGuid(),
            EventCustomPropertyDefinitionId = eventDefinition.Id,
            Definition = null,
            EventId = @event.Id,
            Event = null,
            TenantId = tenant.Id,
            Tenant = null,
            TextValue = EventRedactionSentinelPolicy.DisplayText,
            Ordinal = 0
        };
        var eventProjection = CreateEventProjection(tenant.Id, @event.Id, eventDefinition, eventValue);

        var sessionDefinition = CreateSessionDefinition(tenant.Id, session.Id);
        var sessionOption = CreateSessionOption(sessionDefinition.Id);
        var sessionValue = new EventSessionCustomPropertyValue
        {
            Id = Guid.NewGuid(),
            EventSessionCustomPropertyDefinitionId = sessionDefinition.Id,
            Definition = null,
            EventSessionId = session.Id,
            EventSession = null,
            TenantId = tenant.Id,
            Tenant = null,
            TextValue = EventRedactionSentinelPolicy.DisplayText,
            Ordinal = 0
        };
        var sessionProjection = CreateSessionProjection(tenant.Id, session.Id, sessionDefinition, sessionValue);

        context.EventSessions.Add(session);
        context.EventDays.Add(day);
        context.EventSessionGroups.Add(group);
        context.EventAgendaItems.Add(eventAgendaItem);
        context.EventSessionAgendaItems.Add(sessionAgendaItem);
        context.EventCustomPropertyDefinitions.Add(eventDefinition);
        context.EventCustomPropertyOptions.Add(eventOption);
        context.EventCustomPropertyValues.Add(eventValue);
        context.EventCustomPropertyProjections.Add(eventProjection);
        context.EventSessionCustomPropertyDefinitions.Add(sessionDefinition);
        context.EventSessionCustomPropertyOptions.Add(sessionOption);
        context.EventSessionCustomPropertyValues.Add(sessionValue);
        context.EventSessionCustomPropertyProjections.Add(sessionProjection);

        await context.SaveChangesAsync();

        var savedEvent = await context.Events
            .AsNoTracking()
            .SingleAsync(e => e.Id == @event.Id);
        await Assert.That(savedEvent.Title).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(savedEvent.FeaturedImageId).IsNull();
        await Assert.That(savedEvent.BackgroundImageId).IsNull();
        await Assert.That(await context.EventCustomPropertyProjections.CountAsync(p => p.EventId == @event.Id)).IsEqualTo(1);
        await Assert.That(await context.EventSessionCustomPropertyProjections.CountAsync(p => p.EventSessionId == session.Id)).IsEqualTo(1);
    }

    private static async Task<(Tenant Tenant, Explore.Domain.Event Event)> SetupRedactedEventAsync(ExploreDbContext context)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            FullName = "Redaction Constraint Tenant",
            Slug = "redaction-constraint-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.NewGuid(),
            Pii = new UserPii
            {
                Email = $"redaction-constraint-{Guid.NewGuid():N}@example.com",
                FirstName = "Redacted",
                LastName = "Owner"
            }
        };
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Id = Guid.NewGuid(),
            Pii = new ActorPii { DisplayName = "Redaction Constraint Actor" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var @event = new Explore.Domain.Event(EventStatusEnum.Moderated)
        {
            Id = eventId,
            Title = EventRedactionSentinelPolicy.DisplayText,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            Subtitle = EventRedactionSentinelPolicy.DisplayText,
            Description = EventRedactionSentinelPolicy.DisplayText,
            Content = EventRedactionSentinelPolicy.DisplayText,
            Slug = Slug("event", eventId),
            Timezone = null,
            EventTimeZoneId = null,
            SourceTemplateKey = null,
            BackgroundColor = null,
            BackgroundEffect = null,
            FeaturedImageId = null,
            BackgroundImageId = null,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            TotalViews = 0
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return (tenant, @event);
    }

    private static EventCustomPropertyDefinition CreateEventDefinition(Guid tenantId, Guid eventId)
    {
        var definitionId = Guid.NewGuid();
        return new EventCustomPropertyDefinition
        {
            Id = definitionId,
            EventId = eventId,
            Event = null,
            TenantId = tenantId,
            Tenant = null,
            Namespace = Key("event-definition-namespace", definitionId, 100),
            Key = Key("event-definition-key", definitionId, 100),
            DisplayName = EventRedactionSentinelPolicy.DisplayText,
            Description = EventRedactionSentinelPolicy.DisplayText,
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public,
            IsActive = true,
            IsSearchable = true,
            DefaultTextValue = EventRedactionSentinelPolicy.DisplayText,
            RegexPattern = null,
            AllowedUrlSchemes = null,
            SourceTemplateKey = null,
            InstantiatedAt = DateTimeOffset.UtcNow
        };
    }

    private static EventCustomPropertyOption CreateEventOption(Guid definitionId)
    {
        var optionId = Guid.NewGuid();
        return new EventCustomPropertyOption
        {
            Id = optionId,
            EventCustomPropertyDefinitionId = definitionId,
            Definition = null,
            Namespace = Key("event-option-namespace", optionId, 100),
            Key = Key("event-option-key", optionId, 100),
            DisplayName = EventRedactionSentinelPolicy.DisplayText,
            Description = EventRedactionSentinelPolicy.DisplayText,
            Value = Key("event-option-value", optionId, 500),
            IsActive = true
        };
    }

    private static EventCustomPropertyProjection CreateEventProjection(
        Guid tenantId,
        Guid eventId,
        EventCustomPropertyDefinition definition,
        EventCustomPropertyValue value)
    {
        return new EventCustomPropertyProjection
        {
            Id = Guid.NewGuid(),
            EventCustomPropertyDefinitionId = definition.Id,
            Definition = null,
            EventCustomPropertyValueId = value.Id,
            Value = null,
            EventId = eventId,
            Event = null,
            TenantId = tenantId,
            Tenant = null,
            Namespace = definition.Namespace,
            Key = definition.Key,
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public,
            TextValue = EventRedactionSentinelPolicy.DisplayText,
            NormalizedValue = Key("event-projection-normalized", value.Id, 4000),
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static EventSessionCustomPropertyDefinition CreateSessionDefinition(Guid tenantId, Guid sessionId)
    {
        var definitionId = Guid.NewGuid();
        return new EventSessionCustomPropertyDefinition
        {
            Id = definitionId,
            EventSessionId = sessionId,
            EventSession = null,
            TenantId = tenantId,
            Tenant = null,
            Namespace = Key("session-definition-namespace", definitionId, 100),
            Key = Key("session-definition-key", definitionId, 100),
            DisplayName = EventRedactionSentinelPolicy.DisplayText,
            Description = EventRedactionSentinelPolicy.DisplayText,
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public,
            IsActive = true,
            IsSearchable = true,
            DefaultTextValue = EventRedactionSentinelPolicy.DisplayText,
            RegexPattern = null,
            AllowedUrlSchemes = null,
            SourceTemplateKey = null,
            InstantiatedAt = DateTimeOffset.UtcNow
        };
    }

    private static EventSessionCustomPropertyOption CreateSessionOption(Guid definitionId)
    {
        var optionId = Guid.NewGuid();
        return new EventSessionCustomPropertyOption
        {
            Id = optionId,
            EventSessionCustomPropertyDefinitionId = definitionId,
            Definition = null,
            Namespace = Key("session-option-namespace", optionId, 100),
            Key = Key("session-option-key", optionId, 100),
            DisplayName = EventRedactionSentinelPolicy.DisplayText,
            Description = EventRedactionSentinelPolicy.DisplayText,
            Value = Key("session-option-value", optionId, 500),
            IsActive = true
        };
    }

    private static EventSessionCustomPropertyProjection CreateSessionProjection(
        Guid tenantId,
        Guid sessionId,
        EventSessionCustomPropertyDefinition definition,
        EventSessionCustomPropertyValue value)
    {
        return new EventSessionCustomPropertyProjection
        {
            Id = Guid.NewGuid(),
            EventSessionCustomPropertyDefinitionId = definition.Id,
            Definition = null,
            EventSessionCustomPropertyValueId = value.Id,
            Value = null,
            EventSessionId = sessionId,
            EventSession = null,
            TenantId = tenantId,
            Tenant = null,
            Namespace = definition.Namespace,
            Key = definition.Key,
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public,
            TextValue = EventRedactionSentinelPolicy.DisplayText,
            NormalizedValue = Key("session-projection-normalized", value.Id, 4000),
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static string Slug(string scope, Guid resourceId)
    {
        return EventRedactionSentinelPolicy.BuildSlugSentinel(resourceId, scope, 200);
    }

    private static string Key(string scope, Guid resourceId, int maxLength)
    {
        return EventRedactionSentinelPolicy.BuildMachineKeySentinel(resourceId, scope, maxLength);
    }
}
