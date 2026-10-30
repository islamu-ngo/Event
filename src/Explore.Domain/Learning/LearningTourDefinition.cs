namespace Explore.Domain.Learning;

public sealed record LearningTourStep
{
    public LearningTourStep(string stepKey, int stepVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(stepKey);
        if (stepKey.Length > 96 || stepKey.Any(character =>
                character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
            throw new ArgumentException("Use a bounded semantic step key.", nameof(stepKey));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepVersion);
        StepKey = stepKey;
        StepVersion = stepVersion;
    }

    public string StepKey { get; }
    public int StepVersion { get; }
}

/// <summary>A snapshot of the applicable semantic identities, not presentation or authority.</summary>
public sealed record LearningTourDefinition
{
    public LearningTourDefinition(LearningTourFamily family, int materialVersion,
        IReadOnlyList<LearningTourStep> steps, IReadOnlyList<LearningTourStep>? authoredSteps = null)
    {
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        ArgumentOutOfRangeException.ThrowIfLessThan(materialVersion, 2);
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count is < 1 or > 32 || steps.Any(step => step is null)
            || steps.Select(step => step.StepKey).Distinct(StringComparer.Ordinal).Count() != steps.Count)
            throw new ArgumentException("A tour requires one to thirty-two distinct semantic steps.", nameof(steps));
        IReadOnlyList<LearningTourStep> inventory = authoredSteps ?? steps;
        if (inventory.Count is < 1 or > 32 || inventory.Any(step => step is null)
            || inventory.Select(step => step.StepKey).Distinct(StringComparer.Ordinal).Count() != inventory.Count
            || steps.Any(step => !inventory.Contains(step)))
            throw new ArgumentException("Applicable steps must belong to a bounded authored inventory.", nameof(authoredSteps));
        Family = family;
        MaterialVersion = materialVersion;
        Steps = Array.AsReadOnly(steps.ToArray());
        AuthoredSteps = Array.AsReadOnly(inventory.ToArray());
    }

    public LearningTourFamily Family { get; }
    public int MaterialVersion { get; }
    public IReadOnlyList<LearningTourStep> Steps { get; }
    public IReadOnlyList<LearningTourStep> AuthoredSteps { get; }
}
