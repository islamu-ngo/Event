namespace ISLAMU.Wire.Contracts.Learning;

/// <summary>
/// The current server-resolved plan. Stamps bind this snapshot, not client-supplied authority.
/// </summary>
public sealed record LearningTourPlanDto : LearningResourceDto
{
    private IReadOnlyList<LearningTourStepDto> _steps = Array.AsReadOnly(Array.Empty<LearningTourStepDto>());

    public required LearningTourFamily Family { get; init; }
    public required int MaterialVersion { get; init; }
    public required string ScopeStamp { get; init; }
    public required string PolicySourceStamp { get; init; }
    public required string PlanFingerprint { get; init; }
    public required long LearnerRevision { get; init; }
    public required string ContextKey { get; init; }
    public Guid? EventContextId { get; init; }
    public required bool IsAvailable { get; init; }
    public required bool CanClaim { get; init; }
    public LearningDisplayTextDto? UnavailableReason { get; init; }

    /// <summary>Only currently applicable semantic steps, in presentation order.</summary>
    public IReadOnlyList<LearningTourStepDto> Steps
    {
        get => _steps;
        init => _steps = LearningWireSnapshot.List(value, 32);
    }
}

/// <summary>
/// A semantic identity and localizable instruction. The client resolves TargetKey
/// through its rendered target registry, never as a selector, route or expression.
/// </summary>
public sealed record LearningTourStepDto
{
    public required string StepKey { get; init; }
    public required int StepVersion { get; init; }
    public required LearningDisplayTextDto Instruction { get; init; }
    public required string TargetKey { get; init; }
}
