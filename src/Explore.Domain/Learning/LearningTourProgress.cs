using Explore.Domain.Interfaces;

namespace Explore.Domain.Learning;

public sealed class LearningTourProgress : ITenantEntity
{
    private Guid _tenantId;
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
    public int Step { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? LastIntentId { get; private set; }
    public string? LastIntentFingerprint { get; private set; }

    internal static LearningTourProgress Create(Guid id, UserLearningState owner,
        LearningTourFamily family, int materialVersion, DateTime utcNow) =>
        new()
        {
            Id = id, TenantId = owner.TenantId, UserLearningStateId = owner.Id,
            Family = family, MaterialVersion = materialVersion, UpdatedAtUtc = utcNow
        };

    internal void Begin(Guid intentId, string fingerprint, DateTime utcNow)
    {
        Step = 0;
        if (Outcome is not (LearningTourOutcome.Completed or LearningTourOutcome.Skipped))
            Outcome = LearningTourOutcome.InProgress;
        Remember(intentId, fingerprint, utcNow);
    }

    internal void Record(LearningTourOutcome outcome, int step, Guid intentId, string fingerprint, DateTime utcNow)
    {
        if (step < Step)
            throw new InvalidOperationException("Tour progress cannot move backwards.");
        Step = step;
        // A manual replay never downgrades the durable terminal outcome.
        if (Outcome is not (LearningTourOutcome.Completed or LearningTourOutcome.Skipped))
            Outcome = outcome;
        Remember(intentId, fingerprint, utcNow);
    }

    private void Remember(Guid intentId, string fingerprint, DateTime utcNow)
    {
        LastIntentId = intentId;
        LastIntentFingerprint = fingerprint;
        UpdatedAtUtc = utcNow;
    }
}
