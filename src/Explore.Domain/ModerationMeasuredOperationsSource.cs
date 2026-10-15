using System.Text.Json;

namespace Explore.Domain;

/// <summary>
/// Immutable report captured by the native writer, including actual primary database facts and
/// explicit missing evidence. Neither persistence nor independent acceptance qualifies AG05.
/// </summary>
public sealed class ModerationMeasuredOperationsSource
{
    private ModerationMeasuredOperationsSource() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int ModeCode { get; private set; }
    public string BindingDigest { get; private set; } = string.Empty;
    public string ProposedStateDigest { get; private set; } = string.Empty;
    public string EvidenceJson { get; private set; } = string.Empty;
    public Guid ReviewerHumanId { get; private set; }
    public Guid ReviewerControllerHumanId { get; private set; }
    public Guid RecorderControllerHumanId { get; private set; }
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? OriginalEffectId { get; private set; }
    public string? OriginalSnapshotHash { get; private set; }
    public string? OriginalEffectBindingHash { get; private set; }
    public DateTimeOffset? OriginalCustodyUntil { get; private set; }
    public ModerationMeasuredOperationsEvidence Evidence =>
        JsonSerializer.Deserialize<ModerationMeasuredOperationsEvidence>(EvidenceJson)
        ?? throw new ArgumentException("Missing measured report.");

    public static ModerationMeasuredOperationsSource Record(
        Guid id, Guid hostId, Guid trustId, Guid tenantId, Guid policyId, long policyRevision,
        Guid publicationId, Guid definitionId, Guid profileId, int modeCode, string bindingDigest,
        string proposedDigest, ModerationMeasuredOperationsEvidence evidence,
        Guid reviewer, Guid reviewerController, Guid recorderController, Guid recorder,
        long generation, DateTimeOffset now, DateTimeOffset validUntil,
        Guid? originalEffectId = null, string? originalSnapshotHash = null,
        string? originalEffectBindingHash = null, DateTimeOffset? originalCustodyUntil = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var source = new ModerationMeasuredOperationsSource
        {
            Id = id, HostBindingId = hostId, TrustDomainId = trustId, TenantId = tenantId,
            PolicyId = policyId, PolicyRevision = policyRevision, PublicationId = publicationId,
            DefinitionId = definitionId, ProfileId = profileId, ModeCode = modeCode,
            BindingDigest = bindingDigest, ProposedStateDigest = proposedDigest,
            EvidenceJson = JsonSerializer.Serialize(evidence), ReviewerHumanId = reviewer,
            ReviewerControllerHumanId = reviewerController, RecorderControllerHumanId = recorderController,
            RecordedByHumanId = recorder, WriterGeneration = generation, Revision = 1, RecordedAt = now, ValidUntil = validUntil,
            OriginalEffectId = originalEffectId, OriginalSnapshotHash = originalSnapshotHash,
            OriginalEffectBindingHash = originalEffectBindingHash, OriginalCustodyUntil = originalCustodyUntil
        };
        source.ValidatePersistedState();
        return source;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, PolicyId, PublicationId, DefinitionId, ProfileId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (OriginalEffectId is not null || OriginalSnapshotHash is not null
            || OriginalEffectBindingHash is not null || OriginalCustodyUntil is not null)
        {
            if (OriginalEffectId is not { } effect || OriginalSnapshotHash is null
                || OriginalEffectBindingHash is null || OriginalCustodyUntil is not { } custody
                || !ModerationDeploymentSource.Digest(OriginalSnapshotHash)
                || !ModerationDeploymentSource.Digest(OriginalEffectBindingHash) || ValidUntil > custody)
                throw new ArgumentException("Original measured reports require complete immutable purpose coordinates within original custody.");
            ModerationHostBinding.RequireUuidV7(effect, nameof(OriginalEffectId));
            ModerationQualificationPolicySource.RequireValidity(RecordedAt, custody);
        }
        if (Revision != 1 || PolicyRevision is < 1 or long.MaxValue || WriterGeneration is < 1 or long.MaxValue
            || ModeCode is < 1 or > 2 || !ModerationDeploymentSource.Digest(BindingDigest)
            || !ModerationDeploymentSource.Digest(ProposedStateDigest) || EvidenceJson.Length > 65536
            || ReviewerHumanId == Guid.Empty || RecordedByHumanId == Guid.Empty
            || ReviewerControllerHumanId == Guid.Empty || RecorderControllerHumanId == Guid.Empty
            || ReviewerHumanId == RecordedByHumanId || ReviewerControllerHumanId == RecorderControllerHumanId
            || ReviewerHumanId == RecorderControllerHumanId || RecordedByHumanId == ReviewerControllerHumanId)
            throw new ArgumentException("Measured reports require exact source coordinates and independent finite review.");
        var evidence = Evidence;
        evidence.Validate(RecordedAt);
        if (evidence.References().Any(row => row.ValidUntil < ValidUntil))
            throw new ArgumentException("A measured report cannot extend the observations it retains.");
    }

    public override string ToString() => nameof(ModerationMeasuredOperationsSource);
}
