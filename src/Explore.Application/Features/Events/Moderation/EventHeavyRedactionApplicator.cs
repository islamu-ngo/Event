using Explore.Application.Authorization;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Application.Features.Events.Moderation;

public static class EventHeavyRedactionApplicator
{
    private const int SlugMaxLength = 200;
    private const int MachineKeyMaxLength = 100;
    private const int MachineValueMaxLength = 500;

    public static EventHeavyRedactionSummary Apply(
        EventHeavyRedactionGraph graph,
        Guid? moderatorUserId,
        DateTimeOffset redactedAt)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var utcNow = redactedAt.UtcDateTime;
        var redactedImageObjectIds = graph.ImageStorageObjects
            .Where(storageObject => storageObject.ObjectKey is not null)
            .Select(storageObject => storageObject.Id)
            .Distinct()
            .ToArray();

        RedactRootEvent(graph.Event, moderatorUserId, utcNow);

        foreach (var session in graph.Sessions)
        {
            session.Title = EventRedactionSentinelPolicy.DisplayText;
            session.Description = EventRedactionSentinelPolicy.DisplayText;
            session.Slug = Slug(session.Id, "event-session");
            session.SourceTemplateKey = null;
            session.FeaturedImageId = null;
            session.FeaturedImage = null;
            session.ApplyParentModeration(utcNow);
            Touch(session, moderatorUserId, utcNow);

            if (session.IslamicAspect is not null)
            {
                session.IslamicAspect.RitualRequirementsJson = null;
            }
        }

        foreach (var day in graph.Days)
        {
            day.Label = EventRedactionSentinelPolicy.DisplayText;
            day.Description = EventRedactionSentinelPolicy.DisplayText;
            day.BannerText = EventRedactionSentinelPolicy.DisplayText;
            day.BannerImageId = null;
            day.BannerImage = null;
            Touch(day, moderatorUserId, utcNow);
        }

        foreach (var agendaItem in graph.AgendaItems)
        {
            agendaItem.Title = EventRedactionSentinelPolicy.DisplayText;
            agendaItem.Description = EventRedactionSentinelPolicy.DisplayText;
            Touch(agendaItem, moderatorUserId, utcNow);
        }

        foreach (var agendaItem in graph.SessionAgendaItems)
        {
            agendaItem.Title = EventRedactionSentinelPolicy.DisplayText;
            agendaItem.Description = EventRedactionSentinelPolicy.DisplayText;
        }

        foreach (var group in graph.SessionGroups)
        {
            group.Name = EventRedactionSentinelPolicy.DisplayText;
            group.Description = EventRedactionSentinelPolicy.DisplayText;
            group.Slug = Slug(group.Id, "event-session-group");
            group.Color = null;
            Touch(group, moderatorUserId, utcNow);
        }

        if (graph.Event.TechAspect is not null)
        {
            graph.Event.TechAspect.GithubRepoUrl = null;
            graph.Event.TechAspect.HackathonTrack = EventRedactionSentinelPolicy.DisplayText;
            graph.Event.TechAspect.TechStackTags = EventRedactionSentinelPolicy.DisplayText;
            graph.Event.TechAspect.PrizeCurrencyCode = null;
        }

        RedactEventCustomProperties(graph.EventCustomPropertyDefinitions, graph.EventCustomPropertyProjections, moderatorUserId, utcNow);
        RedactSessionCustomProperties(graph.SessionCustomPropertyDefinitions, graph.SessionCustomPropertyProjections, moderatorUserId, utcNow);

        foreach (var storageObject in graph.ImageStorageObjects)
        {
            storageObject.RequestDelete();
            storageObject.OwningResourceKind = ResourceKinds.Event;
            storageObject.OwningResourceId = graph.Event.Id;
            storageObject.UpdatedAt = utcNow;
            storageObject.UpdatedBy = moderatorUserId;
        }

        return new EventHeavyRedactionSummary(redactedImageObjectIds.Length);
    }

    private static void RedactRootEvent(Event @event, Guid? moderatorUserId, DateTime utcNow)
    {
        @event.Title = EventRedactionSentinelPolicy.DisplayText;
        @event.Subtitle = EventRedactionSentinelPolicy.DisplayText;
        @event.Description = EventRedactionSentinelPolicy.DisplayText;
        @event.Content = EventRedactionSentinelPolicy.DisplayText;
        @event.Slug = Slug(@event.Id, "event");
        @event.Timezone = null;
        @event.EventTimeZoneId = null;
        @event.SourceTemplateKey = null;
        @event.ProvenanceSource = null;
        @event.ProvenanceExternalId = null;
        @event.AtprotoRecordId = null;
        @event.AtprotoRecord = null;
        @event.BackgroundColor = null;
        @event.BackgroundEffect = null;
        @event.FeaturedImageId = null;
        @event.FeaturedImage = null;
        @event.BackgroundImageId = null;
        @event.BackgroundImage = null;
        @event.ApplyHeavyModeration(utcNow);
        Touch(@event, moderatorUserId, utcNow);
    }

    private static void RedactEventCustomProperties(
        IReadOnlyList<EventCustomPropertyDefinition> definitions,
        IReadOnlyList<EventCustomPropertyProjection> projections,
        Guid? moderatorUserId,
        DateTime utcNow)
    {
        var definitionsById = definitions.ToDictionary(definition => definition.Id);

        foreach (var definition in definitions)
        {
            definition.Namespace = MachineKey(definition.Id, "event-custom-property-definition-namespace");
            definition.Key = MachineKey(definition.Id, "event-custom-property-definition-key");
            definition.DisplayName = EventRedactionSentinelPolicy.DisplayText;
            definition.Description = EventRedactionSentinelPolicy.DisplayText;
            definition.DefaultTextValue = EventRedactionSentinelPolicy.DisplayText;
            definition.DefaultNumberValue = null;
            definition.DefaultBooleanValue = null;
            definition.DefaultDateTimeValue = null;
            definition.DefaultOptionId = null;
            definition.DefaultOption = null;
            definition.RegexPattern = null;
            definition.AllowedUrlSchemes = null;
            definition.SourceTemplateKey = null;
            Touch(definition, moderatorUserId, utcNow);

            foreach (var option in definition.Options)
            {
                option.Namespace = MachineKey(option.Id, "event-custom-property-option-namespace");
                option.Key = MachineKey(option.Id, "event-custom-property-option-key");
                option.DisplayName = EventRedactionSentinelPolicy.DisplayText;
                option.Description = EventRedactionSentinelPolicy.DisplayText;
                option.Value = MachineValue(option.Id, "event-custom-property-option-value");
                option.IsDefault = false;
                Touch(option, moderatorUserId, utcNow);
            }

            foreach (var value in definition.Values)
            {
                value.TextValue = EventRedactionSentinelPolicy.DisplayText;
                value.NumberValue = null;
                value.BooleanValue = null;
                value.DateTimeValue = null;
                value.OptionId = null;
                value.Option = null;
                Touch(value, moderatorUserId, utcNow);
            }
        }

        foreach (var projection in projections)
        {
            if (definitionsById.TryGetValue(projection.EventCustomPropertyDefinitionId, out var definition))
            {
                projection.Namespace = definition.Namespace;
                projection.Key = definition.Key;
            }
            else
            {
                projection.Namespace = MachineKey(projection.Id, "event-custom-property-projection-namespace");
                projection.Key = MachineKey(projection.Id, "event-custom-property-projection-key");
            }

            projection.TextValue = EventRedactionSentinelPolicy.DisplayText;
            projection.NumberValue = null;
            projection.BooleanValue = null;
            projection.DateTimeValue = null;
            projection.NormalizedValue = null;
            projection.OptionId = null;
            projection.Option = null;
            projection.UpdatedAt = utcNow;
        }
    }

    private static void RedactSessionCustomProperties(
        IReadOnlyList<EventSessionCustomPropertyDefinition> definitions,
        IReadOnlyList<EventSessionCustomPropertyProjection> projections,
        Guid? moderatorUserId,
        DateTime utcNow)
    {
        var definitionsById = definitions.ToDictionary(definition => definition.Id);

        foreach (var definition in definitions)
        {
            definition.Namespace = MachineKey(definition.Id, "event-session-custom-property-definition-namespace");
            definition.Key = MachineKey(definition.Id, "event-session-custom-property-definition-key");
            definition.DisplayName = EventRedactionSentinelPolicy.DisplayText;
            definition.Description = EventRedactionSentinelPolicy.DisplayText;
            definition.DefaultTextValue = EventRedactionSentinelPolicy.DisplayText;
            definition.DefaultNumberValue = null;
            definition.DefaultBooleanValue = null;
            definition.DefaultDateTimeValue = null;
            definition.DefaultOptionId = null;
            definition.DefaultOption = null;
            definition.RegexPattern = null;
            definition.AllowedUrlSchemes = null;
            definition.SourceTemplateKey = null;
            Touch(definition, moderatorUserId, utcNow);

            foreach (var option in definition.Options)
            {
                option.Namespace = MachineKey(option.Id, "event-session-custom-property-option-namespace");
                option.Key = MachineKey(option.Id, "event-session-custom-property-option-key");
                option.DisplayName = EventRedactionSentinelPolicy.DisplayText;
                option.Description = EventRedactionSentinelPolicy.DisplayText;
                option.Value = MachineValue(option.Id, "event-session-custom-property-option-value");
                option.IsDefault = false;
                Touch(option, moderatorUserId, utcNow);
            }

            foreach (var value in definition.Values)
            {
                value.TextValue = EventRedactionSentinelPolicy.DisplayText;
                value.NumberValue = null;
                value.BooleanValue = null;
                value.DateTimeValue = null;
                value.OptionId = null;
                value.Option = null;
                Touch(value, moderatorUserId, utcNow);
            }
        }

        foreach (var projection in projections)
        {
            if (definitionsById.TryGetValue(projection.EventSessionCustomPropertyDefinitionId, out var definition))
            {
                projection.Namespace = definition.Namespace;
                projection.Key = definition.Key;
            }
            else
            {
                projection.Namespace = MachineKey(projection.Id, "event-session-custom-property-projection-namespace");
                projection.Key = MachineKey(projection.Id, "event-session-custom-property-projection-key");
            }

            projection.TextValue = EventRedactionSentinelPolicy.DisplayText;
            projection.NumberValue = null;
            projection.BooleanValue = null;
            projection.DateTimeValue = null;
            projection.NormalizedValue = null;
            projection.OptionId = null;
            projection.Option = null;
            projection.UpdatedAt = utcNow;
        }
    }

    private static void Touch(IAuditableEntity entity, Guid? moderatorUserId, DateTime utcNow)
    {
        entity.UpdatedAt = utcNow;
        entity.UpdatedBy = moderatorUserId;
    }

    private static string Slug(Guid id, string scope) =>
        EventRedactionSentinelPolicy.BuildSlugSentinel(id, scope, SlugMaxLength);

    private static string MachineKey(Guid id, string scope) =>
        EventRedactionSentinelPolicy.BuildMachineKeySentinel(id, scope, MachineKeyMaxLength);

    private static string MachineValue(Guid id, string scope) =>
        EventRedactionSentinelPolicy.BuildMachineKeySentinel(id, scope, MachineValueMaxLength);
}

public sealed record EventHeavyRedactionSummary(int DeleteRequestedImageObjectCount);
