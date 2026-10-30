using Explore.Domain.Interfaces;

namespace Explore.Domain.Learning;

/// <summary>Source-authored material identity, never a client-supplied requirement or authority.</summary>
public sealed record LearningRequirementDefinition
{
    public static LearningRequirementDefinition Current { get; } = new("tenant-admin-orientation", 1);

    public LearningRequirementDefinition(string requirementKey, int materialVersion)
    {
        if (requirementKey != "tenant-admin-orientation")
            throw new ArgumentException("The requirement is not part of the shipped pack.", nameof(requirementKey));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(materialVersion);
        RequirementKey = requirementKey;
        MaterialVersion = materialVersion;
    }

    public string RequirementKey { get; }
    public int MaterialVersion { get; }
}

public sealed class LearningRequirementAcknowledgement : ITenantEntity
{
    private Guid _tenantId;
    private LearningRequirementAcknowledgement() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(LearningRequirementAcknowledgement));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(LearningRequirementAcknowledgement));
    }
    public Guid UserLearningStateId { get; private set; }
    public string RequirementKey { get; private set; } = null!;
    public int MaterialVersion { get; private set; }
    public DateTime AcknowledgedAtUtc { get; private set; }
    public string PolicySourceStamp { get; private set; } = null!;
    public Guid LastIntentId { get; private set; }
    public string LastIntentFingerprint { get; private set; } = null!;

    internal static LearningRequirementAcknowledgement Create(Guid id, UserLearningState owner,
        LearningRequirementDefinition definition) => new()
        {
            Id = id, TenantId = owner.TenantId, UserLearningStateId = owner.Id,
            RequirementKey = definition.RequirementKey, MaterialVersion = definition.MaterialVersion
        };

    internal void Acknowledge(string policySourceStamp, Guid intentId, string fingerprint, DateTime utcNow)
    {
        PolicySourceStamp = policySourceStamp;
        AcknowledgedAtUtc = utcNow;
        LastIntentId = intentId;
        LastIntentFingerprint = fingerprint;
    }
}
