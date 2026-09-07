using System.Text;

namespace Explore.Application.Features.Events.Moderation;

public enum EventRedactionValueKind
{
    DisplayText = 1,
    DeterministicSlug = 2,
    DeterministicMachineKey = 3,
    Null = 4,
    DeleteStorageObject = 5,
    RebuildProjection = 6
}

public sealed record EventRedactionFieldRule(
    string EntityName,
    string FieldName,
    EventRedactionValueKind ValueKind,
    int? MaxLength,
    bool IsRequired,
    string Notes);

public static class EventRedactionSentinelPolicy
{
    public const string DisplayText = "Redacted";

    private const string SentinelPrefix = "redacted";

    public static IReadOnlyList<EventRedactionFieldRule> FieldRules { get; } =
    [
        new("Event", "Title", EventRedactionValueKind.DisplayText, 200, true, "Primary public display text."),
        new("Event", "Subtitle", EventRedactionValueKind.DisplayText, 200, false, "Secondary public display text."),
        new("Event", "Description", EventRedactionValueKind.DisplayText, 150, false, "Public summary text."),
        new("Event", "Content", EventRedactionValueKind.DisplayText, 5000, false, "Long public body text."),
        new("Event", "Slug", EventRedactionValueKind.DeterministicSlug, 200, false, "Route text must remain non-content and unique enough for the event."),
        new("Event", "Timezone", EventRedactionValueKind.Null, 100, false, "Operational text; clear for data minimization."),
        new("Event", "EventTimeZoneId", EventRedactionValueKind.Null, 100, false, "Operational text; clear for data minimization."),
        new("Event", "SourceTemplateKey", EventRedactionValueKind.Null, 100, false, "Template key can reveal event program shape."),
        new("Event", "ProvenanceSource", EventRedactionValueKind.Null, 100, false, "Import source can reveal event origin."),
        new("Event", "ProvenanceExternalId", EventRedactionValueKind.Null, 200, false, "External source id can reveal event origin."),
        new("Event", "AtprotoRecordId", EventRedactionValueKind.Null, null, false, "Federation record pointer can reveal external event identity."),
        new("Event", "BackgroundColor", EventRedactionValueKind.Null, 50, false, "Presentation value, not useful after redaction."),
        new("Event", "BackgroundEffect", EventRedactionValueKind.Null, 50, false, "Presentation value, not useful after redaction."),
        new("Event", "FeaturedImageId", EventRedactionValueKind.DeleteStorageObject, null, false, "Clear FK and delete backing object through storage abstraction."),
        new("Event", "BackgroundImageId", EventRedactionValueKind.DeleteStorageObject, null, false, "Clear FK and delete backing object through storage abstraction."),

        new("EventSession", "Title", EventRedactionValueKind.DisplayText, 500, false, "Session display text."),
        new("EventSession", "Slug", EventRedactionValueKind.DeterministicSlug, 200, false, "Session route text."),
        new("EventSession", "Description", EventRedactionValueKind.DisplayText, 500, false, "Session description."),
        new("EventSession", "SourceTemplateKey", EventRedactionValueKind.Null, 100, false, "Template key can reveal session program shape."),
        new("EventSession", "FeaturedImageId", EventRedactionValueKind.DeleteStorageObject, null, false, "Clear FK and delete backing object through storage abstraction."),

        new("EventDay", "Label", EventRedactionValueKind.DisplayText, 200, false, "Day display label."),
        new("EventDay", "Description", EventRedactionValueKind.DisplayText, 5000, false, "Day description."),
        new("EventDay", "BannerText", EventRedactionValueKind.DisplayText, 500, false, "Day banner copy."),
        new("EventDay", "BannerImageId", EventRedactionValueKind.DeleteStorageObject, null, false, "Clear FK and delete backing object through storage abstraction."),

        new("EventAgendaItem", "Title", EventRedactionValueKind.DisplayText, 500, true, "Event-level agenda title."),
        new("EventAgendaItem", "Description", EventRedactionValueKind.DisplayText, 2000, false, "Event-level agenda description."),
        new("EventSessionAgendaItem", "Title", EventRedactionValueKind.DisplayText, 500, true, "Session agenda title."),
        new("EventSessionAgendaItem", "Description", EventRedactionValueKind.DisplayText, 500, false, "Session agenda description."),

        new("EventSessionGroup", "Name", EventRedactionValueKind.DisplayText, 200, true, "Track/group display name."),
        new("EventSessionGroup", "Slug", EventRedactionValueKind.DeterministicSlug, 200, false, "Track/group route text with filtered uniqueness."),
        new("EventSessionGroup", "Description", EventRedactionValueKind.DisplayText, 2000, false, "Track/group description."),
        new("EventSessionGroup", "Color", EventRedactionValueKind.Null, 32, false, "Presentation value, not useful after redaction."),

        new("EventCustomPropertyDefinition", "Namespace", EventRedactionValueKind.DeterministicMachineKey, 100, true, "Required event-local machine key."),
        new("EventCustomPropertyDefinition", "Key", EventRedactionValueKind.DeterministicMachineKey, 100, true, "Required event-local machine key."),
        new("EventCustomPropertyDefinition", "DisplayName", EventRedactionValueKind.DisplayText, 200, true, "Event-local custom field label."),
        new("EventCustomPropertyDefinition", "Description", EventRedactionValueKind.DisplayText, 500, false, "Event-local custom field description."),
        new("EventCustomPropertyDefinition", "DefaultTextValue", EventRedactionValueKind.DisplayText, 1000, false, "Event-local default text."),
        new("EventCustomPropertyDefinition", "RegexPattern", EventRedactionValueKind.Null, 1000, false, "Validation pattern can preserve unsafe text."),
        new("EventCustomPropertyDefinition", "AllowedUrlSchemes", EventRedactionValueKind.Null, 500, false, "Validation metadata is cleared with event-local definition."),
        new("EventCustomPropertyDefinition", "SourceTemplateKey", EventRedactionValueKind.Null, 100, false, "Template key can reveal event program shape."),
        new("EventCustomPropertyOption", "Namespace", EventRedactionValueKind.DeterministicMachineKey, 100, true, "Required event-local option machine key."),
        new("EventCustomPropertyOption", "Key", EventRedactionValueKind.DeterministicMachineKey, 100, true, "Required event-local option machine key."),
        new("EventCustomPropertyOption", "DisplayName", EventRedactionValueKind.DisplayText, 200, true, "Event-local option display name."),
        new("EventCustomPropertyOption", "Description", EventRedactionValueKind.DisplayText, 500, false, "Event-local option description."),
        new("EventCustomPropertyOption", "Value", EventRedactionValueKind.DeterministicMachineKey, 500, true, "Required event-local option value."),
        new("EventCustomPropertyValue", "TextValue", EventRedactionValueKind.DisplayText, 4000, false, "Event-local custom field runtime text."),
        new("EventCustomPropertyProjection", "Namespace", EventRedactionValueKind.RebuildProjection, 100, true, "Projection must be rebuilt from redacted definition."),
        new("EventCustomPropertyProjection", "Key", EventRedactionValueKind.RebuildProjection, 100, true, "Projection must be rebuilt from redacted definition."),
        new("EventCustomPropertyProjection", "TextValue", EventRedactionValueKind.RebuildProjection, 4000, false, "Projection must be rebuilt from redacted value."),
        new("EventCustomPropertyProjection", "NormalizedValue", EventRedactionValueKind.RebuildProjection, 4000, false, "Projection must be rebuilt without original content."),

        new("EventSessionCustomPropertyDefinition", "Namespace", EventRedactionValueKind.DeterministicMachineKey, 100, true, "Required session-local machine key."),
        new("EventSessionCustomPropertyDefinition", "Key", EventRedactionValueKind.DeterministicMachineKey, 100, true, "Required session-local machine key."),
        new("EventSessionCustomPropertyDefinition", "DisplayName", EventRedactionValueKind.DisplayText, 200, true, "Session-local custom field label."),
        new("EventSessionCustomPropertyDefinition", "Description", EventRedactionValueKind.DisplayText, 500, false, "Session-local custom field description."),
        new("EventSessionCustomPropertyDefinition", "DefaultTextValue", EventRedactionValueKind.DisplayText, 1000, false, "Session-local default text."),
        new("EventSessionCustomPropertyDefinition", "RegexPattern", EventRedactionValueKind.Null, 1000, false, "Validation pattern can preserve unsafe text."),
        new("EventSessionCustomPropertyDefinition", "AllowedUrlSchemes", EventRedactionValueKind.Null, 500, false, "Validation metadata is cleared with session-local definition."),
        new("EventSessionCustomPropertyDefinition", "SourceTemplateKey", EventRedactionValueKind.Null, 100, false, "Template key can reveal session program shape."),
        new("EventSessionCustomPropertyOption", "Namespace", EventRedactionValueKind.DeterministicMachineKey, 100, true, "Required session-local option machine key."),
        new("EventSessionCustomPropertyOption", "Key", EventRedactionValueKind.DeterministicMachineKey, 100, true, "Required session-local option machine key."),
        new("EventSessionCustomPropertyOption", "DisplayName", EventRedactionValueKind.DisplayText, 200, true, "Session-local option display name."),
        new("EventSessionCustomPropertyOption", "Description", EventRedactionValueKind.DisplayText, 500, false, "Session-local option description."),
        new("EventSessionCustomPropertyOption", "Value", EventRedactionValueKind.DeterministicMachineKey, 500, true, "Required session-local option value."),
        new("EventSessionCustomPropertyValue", "TextValue", EventRedactionValueKind.DisplayText, 4000, false, "Session-local custom field runtime text."),
        new("EventSessionCustomPropertyProjection", "Namespace", EventRedactionValueKind.RebuildProjection, 100, true, "Projection must be rebuilt from redacted definition."),
        new("EventSessionCustomPropertyProjection", "Key", EventRedactionValueKind.RebuildProjection, 100, true, "Projection must be rebuilt from redacted definition."),
        new("EventSessionCustomPropertyProjection", "TextValue", EventRedactionValueKind.RebuildProjection, 4000, false, "Projection must be rebuilt from redacted value."),
        new("EventSessionCustomPropertyProjection", "NormalizedValue", EventRedactionValueKind.RebuildProjection, 4000, false, "Projection must be rebuilt without original content."),

        new("EventTechAspect", "GithubRepoUrl", EventRedactionValueKind.Null, 2048, false, "External URL can identify the event."),
        new("EventTechAspect", "HackathonTrack", EventRedactionValueKind.DisplayText, 200, false, "Event-local track text."),
        new("EventTechAspect", "TechStackTags", EventRedactionValueKind.DisplayText, 1000, false, "Event-local authored tag text."),
        new("EventTechAspect", "PrizeCurrencyCode", EventRedactionValueKind.Null, 3, false, "Short constrained code; clear rather than force display sentinel."),
        new("EventSessionIslamicAspect", "RitualRequirementsJson", EventRedactionValueKind.Null, null, false, "JSON may contain authored content; clear rather than write non-JSON text.")
    ];

    public static string BuildSlugSentinel(Guid resourceId, string scope, int maxLength)
    {
        return BuildConstrainedSentinel(resourceId, scope, maxLength);
    }

    public static string BuildMachineKeySentinel(Guid resourceId, string scope, int maxLength)
    {
        return BuildConstrainedSentinel(resourceId, scope, maxLength);
    }

    private static string BuildConstrainedSentinel(Guid resourceId, string scope, int maxLength)
    {
        if (resourceId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty resource id is required.", nameof(resourceId));
        }

        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Maximum length must be positive.");
        }

        var normalizedScope = NormalizeScope(scope);
        var sentinel = $"{SentinelPrefix}-{normalizedScope}-{resourceId:N}";
        if (sentinel.Length > maxLength)
        {
            throw new InvalidOperationException($"Redaction sentinel for scope '{scope}' exceeds max length {maxLength}.");
        }

        return sentinel;
    }

    private static string NormalizeScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Scope is required.", nameof(scope));
        }

        var builder = new StringBuilder(scope.Length);
        var previousWasSeparator = false;
        foreach (var raw in scope.Trim())
        {
            var c = char.ToLowerInvariant(raw);
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
