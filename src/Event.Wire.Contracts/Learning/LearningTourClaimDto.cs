using System.Text.Json.Serialization;

namespace ISLAMU.Wire.Contracts.Learning;

public sealed record LearningTourClaimDto : LearningResourceDto
{
    private DateTimeOffset _expiresAtUtc;

    public required Guid Token { get; init; }
    public required LearningTourFamily Family { get; init; }
    public required int MaterialVersion { get; init; }
    public required DateTimeOffset ExpiresAtUtc
    {
        get => _expiresAtUtc;
        init => _expiresAtUtc = value.ToUniversalTime();
    }
    public required long Revision { get; init; }
    public required string PlanFingerprint { get; init; }
    public required string PolicySourceStamp { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LearningClaimTourRequest
{
    public required string ExpectedScopeStamp { get; init; }
    public required long ExpectedRevision { get; init; }
    public required Guid IntentId { get; init; }
    [JsonConverter(typeof(LearningStringEnumJsonConverter<LearningTourFamily>))]
    public required LearningTourFamily Family { get; init; }
    public required int MaterialVersion { get; init; }
    public required string ContextKey { get; init; }
    public Guid? EventContextId { get; init; }
    public required string ExpectedPlanFingerprint { get; init; }
    public required string ExpectedPolicySourceStamp { get; init; }
    public required bool Replay { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LearningRecordTourOutcomeRequest
{
    public required string ExpectedScopeStamp { get; init; }
    public required long ExpectedRevision { get; init; }
    public required Guid IntentId { get; init; }
    public Guid? ClaimToken { get; init; }
    public required string ContextKey { get; init; }
    public Guid? EventContextId { get; init; }
    [JsonConverter(typeof(LearningStringEnumJsonConverter<LearningTourFamily>))]
    public required LearningTourFamily Family { get; init; }
    public required int MaterialVersion { get; init; }
    [JsonConverter(typeof(LearningStringEnumJsonConverter<LearningTourAction>))]
    public required LearningTourAction Action { get; init; }
    public string? StepKey { get; init; }
    public int? StepVersion { get; init; }
    public LearningSnoozeDuration? SnoozeDuration { get; init; }
    public required string ExpectedPlanFingerprint { get; init; }
    public required string ExpectedPolicySourceStamp { get; init; }
}
