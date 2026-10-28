using System.Text.Json.Serialization;

namespace ISLAMU.Wire.Contracts.Learning;

public sealed record LearningPracticeDto : LearningResourceDto
{
    public required Guid Id { get; init; }
    public required string ScopeStamp { get; init; }
    public required string ContextKey { get; init; }
    public required string RecipeKey { get; init; }
    public required int MaterialVersion { get; init; }
    public required long Revision { get; init; }
    public required long LearnerRevision { get; init; }
    public bool CanSimulate { get; init; }
    public bool CanClearPractice { get; init; }
    public required LearningSimulationStateDto Simulation { get; init; }
}

/// <summary>Only ChoiceSlot, Step, Value and IsConfirmed are needed to reconstruct authored presentation.</summary>
public sealed record LearningSimulationStateDto
{
    public required string RecipeKey { get; init; }
    public required int MaterialVersion { get; init; }
    public required string StepKey { get; init; }
    public string? ChoiceKey { get; init; }
    public required LearningPracticeChoiceSlot ChoiceSlot { get; init; }
    public required int Step { get; init; }
    public required int Value { get; init; }
    public required bool IsConfirmed { get; init; }
    public required LearningSimulationStatus Status { get; init; }
    public required LearningSyntheticPresentationDto Presentation { get; init; }
    public LearningSimulationReceiptDto? Receipt { get; init; }
}

public sealed record LearningSyntheticPresentationDto
{
    private IReadOnlyList<LearningSyntheticFactDto> _facts = Array.AsReadOnly(Array.Empty<LearningSyntheticFactDto>());

    public required string ScenarioKey { get; init; }
    public required LearningDisplayTextDto Heading { get; init; }
    public required LearningDisplayTextDto Description { get; init; }
    public IReadOnlyList<LearningSyntheticFactDto> Facts
    {
        get => _facts;
        init => _facts = LearningWireSnapshot.List(value, 8);
    }
}

public sealed record LearningSyntheticFactDto(string Key, string Value);

public sealed record LearningSimulationReceiptDto
{
    public required string RecipeKey { get; init; }
    public required int MaterialVersion { get; init; }
    public required string ChoiceKey { get; init; }
    public required string ResultKey { get; init; }
    public bool IsSimulation => true;
    public bool ProductionEffects => false;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LearningApplyPracticeActionRequest
{
    public required string ExpectedScopeStamp { get; init; }
    public required long ExpectedRevision { get; init; }
    public required Guid IntentId { get; init; }
    public required long ExpectedPracticeRevision { get; init; }
    public Guid? PracticeId { get; init; }
    public required string ContextKey { get; init; }
    public required string RecipeKey { get; init; }
    public required int MaterialVersion { get; init; }
    [JsonConverter(typeof(LearningStringEnumJsonConverter<LearningPracticeAction>))]
    public required LearningPracticeAction Action { get; init; }
    public string? ChoiceKey { get; init; }
}
