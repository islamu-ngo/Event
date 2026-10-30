using System.Text.Json.Serialization;

namespace ISLAMU.Wire.Contracts.Learning;

public sealed record LearningGuidancePolicyDto : LearningResourceDto
{
    public required int SchemaVersion { get; init; }
    public required string DefaultsVersion { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required Guid? ConcurrencyStamp { get; init; }

    public required LearningGuidancePolicyPayload Policy { get; init; }

    /// <summary>Server observation used only to materialize HAL, never serialized as a grant.</summary>
    [JsonIgnore]
    public bool CanEdit { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateLearningGuidancePolicyRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required Guid? ExpectedConcurrencyStamp { get; init; }
    public required LearningGuidancePolicyPayload Policy { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LearningGuidancePolicyPayload
{
    private IReadOnlyList<LearningGuidanceFamilyOverrideDto> _familyOverrides = [];

    public required bool Enabled { get; init; }
    public required bool AutomaticInvitations { get; init; }
    public required LearningGuidanceAudience InvitationAudience { get; init; }
    public required LearningGuidanceAudience ManualAccessAudience { get; init; }
    public required LearningRequirementPolicy RequirementPolicy { get; init; }
    public required IReadOnlyList<LearningGuidanceFamilyOverrideDto> FamilyOverrides
    {
        get => _familyOverrides;
        init => _familyOverrides = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LearningGuidanceFamilyOverrideDto
{
    public required LearningTourFamily Family { get; init; }
    public required bool Enabled { get; init; }
    public required bool AutomaticInvitations { get; init; }
    public LearningGuidanceAudience? InvitationAudience { get; init; }
    public LearningGuidanceAudience? ManualAccessAudience { get; init; }
}

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningGuidanceAudience>))]
public enum LearningGuidanceAudience
{
    ActiveMembers = 1,
    TenantAdministrators = 2
}

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningRequirementPolicy>))]
public enum LearningRequirementPolicy
{
    Optional = 0,
    TenantAdminOrientationAcknowledgement = 1
}
