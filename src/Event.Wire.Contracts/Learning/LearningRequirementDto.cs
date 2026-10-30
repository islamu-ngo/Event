using System.Text.Json.Serialization;

namespace ISLAMU.Wire.Contracts.Learning;

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningRequirementStatus>))]
public enum LearningRequirementStatus
{
    NotApplicable = 0,
    Pending = 1,
    Satisfied = 2
}

/// <summary>Private acknowledgement evidence, never competence or production authority.</summary>
public sealed record LearningRequirementDto
{
    public required string RequirementKey { get; init; }
    public required int MaterialVersion { get; init; }
    public required LearningRequirementStatus Status { get; init; }
    public required string PolicySourceStamp { get; init; }
    public DateTimeOffset? AcknowledgedAtUtc { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LearningAcknowledgeRequirementRequest
{
    public required Guid IntentId { get; init; }
    public required string ExpectedScopeStamp { get; init; }
    public required long ExpectedRevision { get; init; }
    public required string ExpectedPolicySourceStamp { get; init; }
    public required int MaterialVersion { get; init; }
}
