namespace Explore.Domain;

/// <summary>
/// Minimal non-renewal proof for an erased notice opportunity. Contains no human identity,
/// private content, receipt digest, sanction or recognition material.
/// </summary>
public sealed class ModerationPreFindingErasureTombstone
{
    private ModerationPreFindingErasureTombstone() { }

    public Guid CaseId { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }

    public static ModerationPreFindingErasureTombstone Record(Guid caseId, Guid host, Guid trust, Guid tenant)
    {
        foreach (var id in new[] { caseId, host, trust, tenant })
            ModerationHostBinding.RequireUuidV7(id, nameof(caseId));
        return new() { CaseId = caseId, HostBindingId = host, TrustDomainId = trust, TenantId = tenant };
    }

    public override string ToString() => nameof(ModerationPreFindingErasureTombstone);
}
