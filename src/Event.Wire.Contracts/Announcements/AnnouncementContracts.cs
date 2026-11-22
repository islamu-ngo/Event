using System.Text.Json.Serialization;

namespace ISLAMU.Wire.Contracts.Announcements;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementOwnerDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required Guid? TenantId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementMaterialDto
{
    public required string Message { get; init; }
    public required string LinkText { get; init; }
    public required string LinkUrl { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementPublicationDto
{
    public required Guid Edition { get; init; }
    public required AnnouncementMaterialDto Material { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementGroupDto
{
    public required Guid SelectionEpoch { get; init; }
    public required bool Enabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required AnnouncementPublicationDto? Publication { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementSourceDto
{
    public required AnnouncementOwnerDto Owner { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required AnnouncementGroupDto? Group { get; init; }

    public required bool IsLocked { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementSourceWitnessDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required Guid? InstanceSelectionEpoch { get; init; }

    public required bool InstanceIsLocked { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required Guid? TenantSelectionEpoch { get; init; }

    public required bool TenantIsLocked { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementObservationDto
{
    public required Guid TenantId { get; init; }
    public required AnnouncementOwnerDto Owner { get; init; }
    public required Guid Edition { get; init; }
    public required AnnouncementSourceWitnessDto SourceWitness { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementDismissalMarkerDto
{
    public required AnnouncementObservationDto Observation { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementChangeDto
{
    /// <summary>One of publish, set-enabled, set-lock, or remove-override.</summary>
    public required string Kind { get; init; }
    public AnnouncementMaterialDto? Material { get; init; }
    public bool? Enabled { get; init; }
    public bool? Redisplay { get; init; }
    public bool? IsLocked { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementUpdateDto
{
    public required AnnouncementChangeDto Change { get; init; }
    public required AnnouncementSourceDto ExpectedSource { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementDismissRequestDto
{
    public required AnnouncementObservationDto Observed { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required AnnouncementDismissalMarkerDto? ExpectedMarker { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementPublicStateDto
{
    public required Guid TenantId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required AnnouncementObservationDto? Observation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required AnnouncementMaterialDto? Material { get; init; }

    public required bool IsVisible { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementSelfStateDto
{
    public required Guid TenantId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required AnnouncementObservationDto? CurrentObservation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required AnnouncementDismissalMarkerDto? Marker { get; init; }
}

public enum AnnouncementPublicationOutcomeDto
{
    Applied,
    Unchanged,
    SourceChanged,
    Locked
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementPublicationReceiptDto
{
    public required AnnouncementPublicationOutcomeDto Outcome { get; init; }
    public required AnnouncementSourceDto Source { get; init; }
}

public enum AnnouncementDismissalDecisionDto
{
    Apply,
    AlreadyDismissed,
    SourceChanged,
    MarkerChanged,
    NotVisible
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AnnouncementDismissalReceiptDto
{
    public required AnnouncementDismissalDecisionDto Decision { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required AnnouncementDismissalMarkerDto? Marker { get; init; }
}
