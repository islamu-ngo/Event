using Explore.Domain.Interfaces;

namespace Explore.Domain.Learning;

public sealed class LearningTourProgress : ITenantEntity
{
    private Guid _tenantId;
    private readonly List<LearningTourStepProgress> _steps = [];
    private LearningTourProgress() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(LearningTourProgress));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(LearningTourProgress));
    }
    public Guid UserLearningStateId { get; private set; }
    public LearningTourFamily Family { get; private set; }
    public int MaterialVersion { get; private set; }
    public LearningTourOutcome Outcome { get; private set; }
    public DateTime? SnoozedUntilUtc { get; private set; }
    public string? LastStepKey { get; private set; }
    public IReadOnlyCollection<LearningTourStepProgress> Steps => _steps.AsReadOnly();
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? LastIntentId { get; private set; }
    public string? LastIntentFingerprint { get; private set; }

    internal static LearningTourProgress Create(Guid id, UserLearningState owner,
        LearningTourFamily family, int materialVersion, DateTime utcNow) =>
        new()
        {
            Id = id,
            TenantId = owner.TenantId,
            UserLearningStateId = owner.Id,
            Family = family,
            MaterialVersion = materialVersion,
            UpdatedAtUtc = utcNow
        };

    internal void CarryCompatibleEvidence(LearningTourDefinition definition,
        IEnumerable<LearningTourProgress> history)
    {
        var compatible = history.SelectMany(tour => tour.Steps)
            .Where(completed => definition.AuthoredSteps.Any(step =>
                step.StepKey == completed.StepKey && step.StepVersion == completed.StepVersion))
            .GroupBy(completed => (completed.StepKey, completed.StepVersion))
            .Select(group => group.MinBy(completed => completed.CompletedAtUtc)!).ToArray();
        _steps.RemoveAll(completed => !definition.AuthoredSteps.Any(step =>
            step.StepKey == completed.StepKey && step.StepVersion == completed.StepVersion));
        foreach (LearningTourStepProgress completed in compatible)
            if (!_steps.Any(step => step.StepKey == completed.StepKey && step.StepVersion == completed.StepVersion))
                _steps.Add(LearningTourStepProgress.Create(this,
                    new(completed.StepKey, completed.StepVersion), completed.CompletedAtUtc));
    }

    internal bool Covers(LearningTourDefinition definition) =>
        definition.Steps.All(step => _steps.Any(completed =>
            completed.StepKey == step.StepKey && completed.StepVersion == step.StepVersion));

    internal void BeginSemantic(Guid intentId, string fingerprint, DateTime utcNow)
    {
        SnoozedUntilUtc = null;
        if (Outcome is not (LearningTourOutcome.Completed or LearningTourOutcome.Skipped))
            Outcome = LearningTourOutcome.InProgress;
        Remember(intentId, fingerprint, utcNow);
    }

    internal void RecordSemantic(LearningTourAction action, LearningTourStep? step,
        DateTime? snoozedUntilUtc, Guid intentId, string fingerprint, DateTime utcNow)
    {
        if (step is not null)
        {
            if (_steps.Any(completed => completed.StepKey == step.StepKey
                    && completed.StepVersion == step.StepVersion))
                throw new InvalidOperationException("Only new explicit semantic progress renews a claim.");
            _steps.RemoveAll(completed => completed.StepKey == step.StepKey);
            _steps.Add(LearningTourStepProgress.Create(this, step, utcNow));
            LastStepKey = step.StepKey;
        }
        SnoozedUntilUtc = snoozedUntilUtc;
        if (Outcome is not (LearningTourOutcome.Completed or LearningTourOutcome.Skipped))
            Outcome = action switch
            {
                LearningTourAction.Progress => LearningTourOutcome.InProgress,
                LearningTourAction.Complete => LearningTourOutcome.Completed,
                LearningTourAction.Skip => LearningTourOutcome.Skipped,
                _ => LearningTourOutcome.Interrupted
            };
        Remember(intentId, fingerprint, utcNow);
    }

    private void Remember(Guid intentId, string fingerprint, DateTime utcNow)
    {
        LastIntentId = intentId;
        LastIntentFingerprint = fingerprint;
        UpdatedAtUtc = utcNow;
    }
}
