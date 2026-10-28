namespace ISLAMU.Wire.Contracts.Learning;

public sealed record LearningLessonDto
{
    private IReadOnlyList<LearningChoiceDto> _choices = Array.AsReadOnly(Array.Empty<LearningChoiceDto>());
    private IReadOnlyList<LearningRecipeTransitionDto> _transitions = Array.AsReadOnly(Array.Empty<LearningRecipeTransitionDto>());

    public required string PackKey { get; init; }
    public required int MaterialVersion { get; init; }
    public required string RecipeKey { get; init; }
    public required LearningDisplayTextDto Title { get; init; }
    public required LearningDisplayTextDto Description { get; init; }
    public required string RequiredCapabilityKey { get; init; }
    public required LearningDisplayTextDto CapabilityExplanation { get; init; }
    public required LearningDisplayTextDto Prerequisite { get; init; }

    public IReadOnlyList<LearningChoiceDto> Choices
    {
        get => _choices;
        init => _choices = LearningWireSnapshot.List(value, 3);
    }

    public IReadOnlyList<LearningRecipeTransitionDto> Transitions
    {
        get => _transitions;
        init => _transitions = LearningWireSnapshot.List(value, 32);
    }
}

public sealed record LearningDisplayTextDto(string Key, string Fallback);

public sealed record LearningChoiceDto
{
    public required string Key { get; init; }
    public required LearningPracticeChoiceSlot Slot { get; init; }
    public required int Value { get; init; }
    public required LearningDisplayTextDto Label { get; init; }
    public required LearningDisplayTextDto Explanation { get; init; }
    public required string ResultKey { get; init; }
    public required LearningSyntheticPresentationDto Result { get; init; }
}

public sealed record LearningRecipeTransitionDto(
    string FromStepKey, LearningPracticeAction Action, string? ChoiceKey, string ToStepKey);
