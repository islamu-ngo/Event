namespace Explore.Domain;

/// <summary>
/// One native policy slot. Latest publication advances for drafts too; the active pointer changes
/// only with a fully qualified active publication. No draft silently withdraws an existing policy.
/// The writer owns the jurisdiction slot fence, including first insertion.
/// </summary>
public sealed class ModerationPolicySelection
{
    private ModerationPolicySelection() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid JurisdictionId { get; private set; }
    public Guid? JurisdictionTenantId { get; private set; }
    public long Revision { get; private set; }
    public Guid LatestPublicationId { get; private set; }
    public Guid? ActivePublicationId { get; private set; }

    public static ModerationPolicySelection Start(ModerationPolicyPublication publication)
    {
        if (publication.Revision != 1 || publication.AdoptedRevision != publication.Revision
            || publication.PreviousPublicationId is not null)
            throw new ArgumentException("Initial publication must start revision one.", nameof(publication));
        var selection = new ModerationPolicySelection
        {
            Id = publication.PolicyId,
            HostBindingId = publication.HostBindingId,
            TrustDomainId = publication.TrustDomainId,
            JurisdictionId = publication.Jurisdiction.Id,
            JurisdictionTenantId = publication.JurisdictionTenantId
        };
        selection.Replace(publication, 0, null);
        return selection;
    }

    public void Replace(ModerationPolicyPublication publication, long expectedRevision, Guid? expectedPublicationId)
    {
        if (publication.AdoptedRevision != publication.Revision
            || Revision != expectedRevision || (Revision == 0 ? null : (Guid?)LatestPublicationId) != expectedPublicationId
            || publication.PolicyId != Id || publication.HostBindingId != HostBindingId
            || publication.TrustDomainId != TrustDomainId || publication.Jurisdiction.Id != JurisdictionId
            || publication.JurisdictionTenantId != JurisdictionTenantId
            || publication.Revision != checked(Revision + 1)
            || publication.PreviousPublicationId != expectedPublicationId
            || publication.Id == LatestPublicationId
            || publication.PublishedActive && (publication.Evidence.Count == 0
                || publication.EvidenceValidUntil <= publication.PublishedAt
                || publication.EvidenceValidUntil is null))
            throw new InvalidOperationException("Publication replacement requires the exact complete next revision.");
        Revision = publication.Revision;
        LatestPublicationId = publication.Id;
        if (publication.PublishedActive)
            ActivePublicationId = publication.Id;
    }
}
