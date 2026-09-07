using System.Text.Json;
using Explore.Application.DTOs.EventAggregateView;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Views;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventAggregateViews;

internal static class EventAggregateViewMapper
{
    private const int TopSearchableFacetLimit = 5;

    public static EventWithSessionsViewDto MapDetail(
        EventWithSessionsView view,
        IReadOnlyCollection<EventCustomPropertyDefinition> eventDefinitions,
        IReadOnlyCollection<EventSessionCustomPropertyDefinition> sessionDefinitions,
        ExposureLevel exposureCeiling,
        ILogger logger)
    {
        return new EventWithSessionsViewDto
        {
            EventId = view.EventId,
            TenantId = view.TenantId,
            Title = view.Title,
            Slug = view.Slug,
            Description = view.Description,
            StartAt = view.StartAt,
            EndAt = view.EndAt,
            Status = view.Status,
            Visibility = view.Visibility,
            IsDeleted = view.IsDeleted,
            CreatedAt = view.CreatedAt,
            UpdatedAt = view.UpdatedAt,
            IslamicTheme = view.IslamicTheme,
            Madhab = view.Madhab,
            IsRamadan = view.IsRamadan,
            PrayerAware = view.PrayerAware,
            TechStack = view.TechStack,
            DifficultyLevel = view.DifficultyLevel,
            TargetAudience = view.TargetAudience,
            SessionCount = view.SessionCount,
            FirstSessionStartAt = view.FirstSessionStartAt,
            LastSessionEndAt = view.LastSessionEndAt,
            HasInPersonSessions = view.HasInPersonSessions,
            HasVirtualSessions = view.HasVirtualSessions,
            AggregatedSessionIslamicThemes = view.AggregatedSessionIslamicThemes,
            EventCustomProperties = BuildEventFacets(view.EventCustomPropertyFacets, eventDefinitions, exposureCeiling, logger),
            EventSessionCustomProperties = BuildSessionFacets(view.EventSessionCustomPropertyFacets, sessionDefinitions, exposureCeiling, logger)
        };
    }

    public static EventListAggregateViewDto MapListItem(
        EventWithSessionsView view,
        IReadOnlyCollection<EventCustomPropertyDefinition> eventDefinitions,
        ExposureLevel exposureCeiling,
        ILogger logger)
    {
        var searchableFacets = BuildEventFacets(view.EventCustomPropertyFacets, eventDefinitions, exposureCeiling, logger)
            .Where(x => x.IsSearchable)
            .Take(TopSearchableFacetLimit)
            .ToList();

        return new EventListAggregateViewDto
        {
            EventId = view.EventId,
            TenantId = view.TenantId,
            Title = view.Title,
            Slug = view.Slug,
            Description = view.Description,
            StartAt = view.StartAt,
            EndAt = view.EndAt,
            Status = view.Status,
            Visibility = view.Visibility,
            IsDeleted = view.IsDeleted,
            CreatedAt = view.CreatedAt,
            UpdatedAt = view.UpdatedAt,
            IslamicTheme = view.IslamicTheme,
            Madhab = view.Madhab,
            IsRamadan = view.IsRamadan,
            PrayerAware = view.PrayerAware,
            TechStack = view.TechStack,
            DifficultyLevel = view.DifficultyLevel,
            TargetAudience = view.TargetAudience,
            SessionCount = view.SessionCount,
            FirstSessionStartAt = view.FirstSessionStartAt,
            LastSessionEndAt = view.LastSessionEndAt,
            HasInPersonSessions = view.HasInPersonSessions,
            HasVirtualSessions = view.HasVirtualSessions,
            AggregatedSessionIslamicThemes = view.AggregatedSessionIslamicThemes,
            SearchableFacets = searchableFacets
        };
    }

    private static IReadOnlyList<EventCustomPropertyFacetDto> BuildEventFacets(
        string json,
        IReadOnlyCollection<EventCustomPropertyDefinition> definitions,
        ExposureLevel exposureCeiling,
        ILogger logger)
    {
        var metadata = definitions.ToDictionary(
            keySelector: x => BuildFacetKey(x.Namespace, x.Key),
            elementSelector: x => new FacetMetadata(
                x.Namespace,
                x.Key,
                x.DisplayName,
                x.PropertyType,
                x.ExposureLevel,
                x.IsSearchable,
                x.IsFilterable,
                x.IsExportable,
                x.IsModerationRelevant,
                x.IsAnalyticsRelevant));

        return ParseFacetValues(json, logger)
            .Select(entry => TryCreateEventFacet(entry.Key, entry.Value, metadata, exposureCeiling))
            .Where(x => x is not null)
            .Cast<EventCustomPropertyFacetDto>()
            .OrderBy(x => x.Namespace)
            .ThenBy(x => x.Key)
            .ToList();
    }

    private static IReadOnlyList<EventSessionCustomPropertyFacetDto> BuildSessionFacets(
        string json,
        IReadOnlyCollection<EventSessionCustomPropertyDefinition> definitions,
        ExposureLevel exposureCeiling,
        ILogger logger)
    {
        var metadata = definitions.ToDictionary(
            keySelector: x => BuildFacetKey(x.Namespace, x.Key),
            elementSelector: x => new FacetMetadata(
                x.Namespace,
                x.Key,
                x.DisplayName,
                x.PropertyType,
                x.ExposureLevel,
                x.IsSearchable,
                x.IsFilterable,
                x.IsExportable,
                x.IsModerationRelevant,
                x.IsAnalyticsRelevant));

        return ParseFacetValues(json, logger)
            .Select(entry => TryCreateSessionFacet(entry.Key, entry.Value, metadata, exposureCeiling))
            .Where(x => x is not null)
            .Cast<EventSessionCustomPropertyFacetDto>()
            .OrderBy(x => x.Namespace)
            .ThenBy(x => x.Key)
            .ToList();
    }

    private static Dictionary<string, IReadOnlyList<JsonElement>> ParseFacetValues(string json, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning("Aggregate facet JSON root was {ValueKind} instead of Object.", document.RootElement.ValueKind);
                return [];
            }

            var result = new Dictionary<string, IReadOnlyList<JsonElement>>(StringComparer.Ordinal);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    result[property.Name] = property.Value.EnumerateArray().Select(x => x.Clone()).ToList();
                    continue;
                }

                result[property.Name] = [property.Value.Clone()];
            }

            return result;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize aggregate facet JSON. Returning empty facet list.");
            return [];
        }
    }

    private static EventCustomPropertyFacetDto? TryCreateEventFacet(
        string compositeKey,
        IReadOnlyList<JsonElement> values,
        IReadOnlyDictionary<string, FacetMetadata> metadata,
        ExposureLevel exposureCeiling)
    {
        if (!metadata.TryGetValue(compositeKey, out var facetMetadata))
            return null;

        if (!IsVisible(facetMetadata.ExposureLevel, exposureCeiling))
            return null;

        return new EventCustomPropertyFacetDto
        {
            Namespace = facetMetadata.Namespace,
            Key = facetMetadata.Key,
            DisplayName = facetMetadata.DisplayName,
            PropertyType = facetMetadata.PropertyType,
            ExposureLevel = facetMetadata.ExposureLevel,
            Values = values,
            IsSearchable = facetMetadata.IsSearchable,
            IsFilterable = facetMetadata.IsFilterable,
            IsExportable = facetMetadata.IsExportable,
            IsModerationRelevant = facetMetadata.IsModerationRelevant,
            IsAnalyticsRelevant = facetMetadata.IsAnalyticsRelevant
        };
    }

    private static EventSessionCustomPropertyFacetDto? TryCreateSessionFacet(
        string compositeKey,
        IReadOnlyList<JsonElement> values,
        IReadOnlyDictionary<string, FacetMetadata> metadata,
        ExposureLevel exposureCeiling)
    {
        if (!metadata.TryGetValue(compositeKey, out var facetMetadata))
            return null;

        if (!IsVisible(facetMetadata.ExposureLevel, exposureCeiling))
            return null;

        return new EventSessionCustomPropertyFacetDto
        {
            Namespace = facetMetadata.Namespace,
            Key = facetMetadata.Key,
            DisplayName = facetMetadata.DisplayName,
            PropertyType = facetMetadata.PropertyType,
            ExposureLevel = facetMetadata.ExposureLevel,
            Values = values,
            IsSearchable = facetMetadata.IsSearchable,
            IsFilterable = facetMetadata.IsFilterable,
            IsExportable = facetMetadata.IsExportable,
            IsModerationRelevant = facetMetadata.IsModerationRelevant,
            IsAnalyticsRelevant = facetMetadata.IsAnalyticsRelevant
        };
    }

    private static bool IsVisible(ExposureLevel facetExposure, ExposureLevel exposureCeiling)
        => GetExposureRank(facetExposure) <= GetExposureRank(exposureCeiling);

    private static int GetExposureRank(ExposureLevel exposureLevel)
        => exposureLevel switch
        {
            ExposureLevel.Public => 0,
            ExposureLevel.TenantAdminOnly => 1,
            ExposureLevel.OrganizerOnly => 2,
            ExposureLevel.Internal => 3,
            _ => int.MaxValue
        };

    private static string BuildFacetKey(string namespaceValue, string key)
        => $"{namespaceValue}/{key}";

    private sealed record FacetMetadata(
        string Namespace,
        string Key,
        string DisplayName,
        PropertyType PropertyType,
        ExposureLevel ExposureLevel,
        bool IsSearchable,
        bool IsFilterable,
        bool IsExportable,
        bool IsModerationRelevant,
        bool IsAnalyticsRelevant);
}
