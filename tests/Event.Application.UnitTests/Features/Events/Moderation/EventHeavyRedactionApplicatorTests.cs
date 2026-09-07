using Explore.Application.Authorization;
using Explore.Application.Features.Events.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.Events.Moderation;

public sealed class EventHeavyRedactionApplicatorTests
{
    [Test]
    public async Task Apply_RedactsEventOwnedGraphAndRequestsImageDeletion()
    {
        var moderatorUserId = Guid.NewGuid();
        var redactedAt = DateTimeOffset.UtcNow;
        var image = CreateStorageObject();
        var @event = CreateEvent(image.Id);
        var session = CreateSession(@event, image.Id);
        var day = CreateDay(@event, image.Id);
        var agendaItem = CreateAgendaItem(@event);
        var sessionAgendaItem = CreateSessionAgendaItem(session);
        var group = CreateSessionGroup(@event);
        var eventDefinition = CreateEventDefinition(@event);
        var eventProjection = CreateEventProjection(@event, eventDefinition);
        var sessionDefinition = CreateSessionDefinition(session);
        var sessionProjection = CreateSessionProjection(session, sessionDefinition);

        @event.Sessions.Add(session);
        @event.Days.Add(day);
        @event.AgendaItems.Add(agendaItem);
        @event.SessionGroups.Add(group);
        @event.TechAspect = new EventTechAspect
        {
            Id = @event.Id,
            GithubRepoUrl = "https://example.com/unsafe",
            HackathonTrack = "Unsafe Track",
            TechStackTags = "Unsafe Tags",
            PrizeCurrencyCode = "EUR"
        };

        var graph = new EventHeavyRedactionGraph(
            @event,
            [session],
            [day],
            [agendaItem],
            [sessionAgendaItem],
            [group],
            [eventDefinition],
            [eventProjection],
            [sessionDefinition],
            [sessionProjection],
            [image]);

        var result = EventHeavyRedactionApplicator.Apply(graph, moderatorUserId, redactedAt);

        await Assert.That(result.DeleteRequestedImageObjectCount).IsEqualTo(1);
        await Assert.That(@event.EventStatusId).IsEqualTo((int)EventStatusEnum.Moderated);
        await Assert.That(@event.Title).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(@event.Slug).StartsWith("redacted-event-");
        await Assert.That(@event.ProvenanceSource).IsNull();
        await Assert.That(@event.ProvenanceExternalId).IsNull();
        await Assert.That(@event.AtprotoRecordId).IsNull();
        await Assert.That(@event.FeaturedImageId).IsNull();
        await Assert.That(@event.BackgroundImageId).IsNull();
        await Assert.That(@event.UpdatedBy).IsEqualTo(moderatorUserId);

        await Assert.That(session.Title).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(session.Slug).StartsWith("redacted-event-session-");
        await Assert.That(session.FeaturedImageId).IsNull();
        await Assert.That(session.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Moderated);
        await Assert.That(session.IslamicAspect!.RitualRequirementsJson).IsNull();

        await Assert.That(day.Label).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(day.BannerImageId).IsNull();
        await Assert.That(agendaItem.Title).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(sessionAgendaItem.Title).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(group.Name).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(group.Slug).StartsWith("redacted-event-session-group-");

        await Assert.That(@event.TechAspect!.GithubRepoUrl).IsNull();
        await Assert.That(@event.TechAspect.HackathonTrack).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(@event.TechAspect.TechStackTags).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(@event.TechAspect.PrizeCurrencyCode).IsNull();

        await Assert.That(eventDefinition.Namespace).StartsWith("redacted-event-custom-property-definition-namespace-");
        await Assert.That(eventDefinition.DisplayName).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(eventDefinition.DefaultNumberValue).IsNull();
        await Assert.That(eventDefinition.RegexPattern).IsNull();
        await Assert.That(eventProjection.Namespace).IsEqualTo(eventDefinition.Namespace);
        await Assert.That(eventProjection.TextValue).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(eventProjection.NormalizedValue).IsNull();

        await Assert.That(sessionDefinition.Namespace).StartsWith("redacted-event-session-custom-property-definition-namespace-");
        await Assert.That(sessionDefinition.DisplayName).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(sessionProjection.Namespace).IsEqualTo(sessionDefinition.Namespace);
        await Assert.That(sessionProjection.TextValue).IsEqualTo(EventRedactionSentinelPolicy.DisplayText);
        await Assert.That(sessionProjection.NormalizedValue).IsNull();

        await Assert.That(image.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        await Assert.That(image.OwningResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(image.OwningResourceId).IsEqualTo(@event.Id);
        await Assert.That(image.UpdatedBy).IsEqualTo(moderatorUserId);
    }

    private static Explore.Domain.Event CreateEvent(Guid imageId) => new(EventStatusEnum.Published)
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Tenant = null!,
        ActorId = Guid.NewGuid(),
        Actor = null!,
        Title = "Illegal Title",
        Subtitle = "Illegal Subtitle",
        Description = "Illegal Description",
        Content = "Illegal Content",
        Slug = "illegal-title",
        Timezone = "Europe/Brussels",
        EventTimeZoneId = "Europe/Brussels",
        SourceTemplateKey = "unsafe-template",
        ProvenanceSource = "unsafe-import",
        ProvenanceExternalId = "external-unsafe-id",
        AtprotoRecordId = Guid.NewGuid(),
        BackgroundColor = "#ff0000",
        BackgroundEffect = "unsafe-effect",
        FeaturedImageId = imageId,
        BackgroundImageId = imageId,
        EventStatus = null!,
        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
        VisibilityType = null!,
        EventFormatId = (int)EventFormatEnum.Local,
        EventFormat = null!
    };

    private static EventSession CreateSession(Explore.Domain.Event @event, Guid imageId) => new(EventSessionStatusEnum.Draft)
    {
        Id = Guid.NewGuid(),
        EventId = @event.Id,
        Event = @event,
        TenantId = @event.TenantId,
        Tenant = null!,
        Title = "Illegal Session",
        Description = "Illegal Session Description",
        Slug = "illegal-session",
        SourceTemplateKey = "unsafe-session-template",
        FeaturedImageId = imageId,
        IslamicAspect = new EventSessionIslamicAspect { RitualRequirementsJson = "{\"unsafe\":true}" }
    };

    private static EventDay CreateDay(Explore.Domain.Event @event, Guid imageId) => new()
    {
        Id = Guid.NewGuid(),
        EventId = @event.Id,
        Event = @event,
        TenantId = @event.TenantId,
        Tenant = null!,
        LocalDate = new DateOnly(2026, 7, 1),
        Label = "Illegal Day",
        Description = "Illegal Day Description",
        BannerText = "Illegal Banner",
        BannerImageId = imageId
    };

    private static EventAgendaItem CreateAgendaItem(Explore.Domain.Event @event) => new()
    {
        Id = Guid.NewGuid(),
        EventId = @event.Id,
        Event = @event,
        TenantId = @event.TenantId,
        Tenant = null!,
        Title = "Illegal Agenda",
        Description = "Illegal Agenda Description"
    };

    private static EventSessionAgendaItem CreateSessionAgendaItem(EventSession session) => new()
    {
        Id = Guid.NewGuid(),
        EventSessionId = session.Id,
        EventSession = session,
        TenantId = session.TenantId,
        Tenant = null!,
        Title = "Illegal Session Agenda",
        Description = "Illegal Session Agenda Description"
    };

    private static EventSessionGroup CreateSessionGroup(Explore.Domain.Event @event) => new()
    {
        Id = Guid.NewGuid(),
        EventId = @event.Id,
        Event = @event,
        TenantId = @event.TenantId,
        Tenant = null!,
        Name = "Illegal Track",
        Description = "Illegal Track Description",
        Slug = "illegal-track",
        Color = "#ff0000"
    };

    private static EventCustomPropertyDefinition CreateEventDefinition(Explore.Domain.Event @event) => new()
    {
        Id = Guid.NewGuid(),
        EventId = @event.Id,
        Event = @event,
        TenantId = @event.TenantId,
        Tenant = null,
        Namespace = "unsafe",
        Key = "unsafe-key",
        DisplayName = "Illegal Field",
        Description = "Illegal Field Description",
        DefaultTextValue = "Illegal Default",
        DefaultNumberValue = 42,
        RegexPattern = "illegal",
        AllowedUrlSchemes = "https",
        SourceTemplateKey = "unsafe-template",
        PropertyType = PropertyType.Text,
        ExposureLevel = ExposureLevel.Public
    };

    private static EventCustomPropertyProjection CreateEventProjection(
        Explore.Domain.Event @event,
        EventCustomPropertyDefinition definition) => new()
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = @event,
            TenantId = @event.TenantId,
            Tenant = null,
            EventCustomPropertyDefinitionId = definition.Id,
            Definition = definition,
            EventCustomPropertyValueId = Guid.NewGuid(),
            Namespace = "unsafe",
            Key = "unsafe-key",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public,
            TextValue = "Illegal Projection",
            NormalizedValue = "illegal-projection"
        };

    private static EventSessionCustomPropertyDefinition CreateSessionDefinition(EventSession session) => new()
    {
        Id = Guid.NewGuid(),
        EventSessionId = session.Id,
        EventSession = session,
        TenantId = session.TenantId,
        Tenant = null,
        Namespace = "unsafe",
        Key = "unsafe-key",
        DisplayName = "Illegal Session Field",
        Description = "Illegal Session Field Description",
        DefaultTextValue = "Illegal Session Default",
        RegexPattern = "illegal",
        AllowedUrlSchemes = "https",
        SourceTemplateKey = "unsafe-template",
        PropertyType = PropertyType.Text,
        ExposureLevel = ExposureLevel.Public
    };

    private static EventSessionCustomPropertyProjection CreateSessionProjection(
        EventSession session,
        EventSessionCustomPropertyDefinition definition) => new()
        {
            Id = Guid.NewGuid(),
            EventSessionId = session.Id,
            EventSession = session,
            TenantId = session.TenantId,
            Tenant = null,
            EventSessionCustomPropertyDefinitionId = definition.Id,
            Definition = definition,
            EventSessionCustomPropertyValueId = Guid.NewGuid(),
            Namespace = "unsafe",
            Key = "unsafe-key",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public,
            TextValue = "Illegal Session Projection",
            NormalizedValue = "illegal-session-projection"
        };

    private static StorageObject CreateStorageObject() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Tenant = null!,
        FileTypeId = (int)FileTypeEnum.Image,
        FileType = null!,
        Provider = StorageProviders.Local,
        ObjectKey = "tenants/test/illegal.png",
        Uri = "/images/illegal.png",
        FullName = "illegal.png",
        SafeDisplayName = "illegal.png",
        Extension = ".png",
        Size = 100,
        Visibility = StorageObjectVisibilities.PublicImage,
        Purpose = StorageObjectPurposes.EventImage,
        LifecycleState = StorageObjectLifecycleStates.Active
    };
}
