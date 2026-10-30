using Explore.Domain.Interfaces;

namespace Explore.Domain.Learning;

public sealed class LearningTourStepProgress : ITenantEntity
{
    private Guid _tenantId;
    private LearningTourStepProgress() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(LearningTourStepProgress));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(LearningTourStepProgress));
    }
    public Guid TourProgressId { get; private set; }
    public string StepKey { get; private set; } = string.Empty;
    public int StepVersion { get; private set; }
    public DateTime CompletedAtUtc { get; private set; }

    internal static LearningTourStepProgress Create(LearningTourProgress owner,
        LearningTourStep step, DateTime completedAtUtc) => new()
    {
        Id = Guid.CreateVersion7(completedAtUtc),
        TenantId = owner.TenantId,
        TourProgressId = owner.Id,
        StepKey = step.StepKey,
        StepVersion = step.StepVersion,
        CompletedAtUtc = completedAtUtc
    };
}
