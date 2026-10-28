using System.Text.Json.Serialization;

namespace ISLAMU.Wire.Contracts.Learning;

public sealed record LearningMutationReceiptDto : LearningResourceDto
{
    public required Guid Id { get; init; }
    public required string ScopeStamp { get; init; }
    public required long ResourceRevision { get; init; }
    public required long LearnerRevision { get; init; }
    public LearningContextDto? State { get; init; }
    public LearningPracticeDto? Practice { get; init; }
    public LearningTourClaimDto? Claim { get; init; }
    public LearningSpaceDto? Space { get; init; }
    public LearningSimulationStatus? SimulationStatus { get; init; }
}

public sealed record LearningSpaceDto : LearningResourceDto
{
    public required Guid Id { get; init; }
    public required LearningSpaceStatus Status { get; init; }
    public required string PackKey { get; init; }
    public required int MaterialVersion { get; init; }
    public required long Revision { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LearningManageSpaceRequest
{
    public required string ExpectedScopeStamp { get; init; }
    public required long ExpectedRevision { get; init; }
    [JsonConverter(typeof(LearningStringEnumJsonConverter<LearningSpaceAction>))]
    public required LearningSpaceAction Action { get; init; }
}
