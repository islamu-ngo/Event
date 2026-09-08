namespace Explore.Application.Models.PublicExperience;

public sealed record PublicEventSectionPresetsConfig(
    int SchemaVersion = 1,
    IReadOnlyList<PublicEventSectionPresetConfig>? Presets = null);

public sealed record PublicEventSectionPresetConfig(
    string Id,
    string Label,
    PublicEventSectionOwnerFilter? Owners = null,
    PublicEventSectionEventFilter? Filters = null,
    string? Icon = null,
    int SortOrder = 0,
    bool IsEnabled = true,
    int? Limit = null);

public sealed record PublicEventSectionOwnerFilter(
    IReadOnlyList<Guid>? ActorIds = null,
    IReadOnlyList<Guid>? OrganizationIds = null,
    IReadOnlyList<Guid>? GroupIds = null);

public sealed record PublicEventSectionEventFilter(
    IReadOnlyList<Guid>? CategoryIds = null,
    IReadOnlyList<Guid>? TagIds = null,
    IReadOnlyList<int>? AudienceGenderIds = null,
    IReadOnlyList<int>? AudienceAgeIds = null,
    IReadOnlyList<int>? EventTypeIds = null,
    IReadOnlyList<int>? EventFormatIds = null,
    PublicEventSectionDateFilter? Date = null,
    IReadOnlyList<PublicEventSectionCustomPropertyFilter>? CustomProperties = null);

public sealed record PublicEventSectionDateFilter(
    PublicEventSectionDateWindow Window = PublicEventSectionDateWindow.Upcoming,
    DateOnly? StartsOnOrAfter = null,
    DateOnly? StartsOnOrBefore = null);

public sealed record PublicEventSectionCustomPropertyFilter(
    string Namespace,
    string Key,
    PublicEventSectionCustomPropertyOperator Operator,
    IReadOnlyList<string>? Values = null);

public enum PublicEventSectionDateWindow
{
    Upcoming = 0,
    Past = 1,
    Custom = 2
}

public enum PublicEventSectionCustomPropertyOperator
{
    Equals = 0,
    Contains = 1,
    AnyOf = 2
}
