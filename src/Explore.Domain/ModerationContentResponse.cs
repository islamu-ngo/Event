namespace Explore.Domain;

/// <summary>
/// A finite statement about the original Content decision, authored by an original Event
/// representative. This is neither Human finding evidence nor confidential allegation custody.
/// The row is also its submission receipt: expiry and erasure delete both, never leave a
/// success receipt claiming that erased text is still available.
/// </summary>
public sealed class ModerationContentResponse
{
    public const int MaximumStatementBytes = 8192;
    private ModerationContentResponse() { }
    private byte[] protectedStatement = [];
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid NoticeId { get; private set; }
    public Guid EffectId { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid ContentSubjectId { get; private set; }
    public Guid RepresentativeHumanId { get; private set; }
    public Guid AssignmentId { get; private set; }
    public long AssignmentRevision { get; private set; }
    public string SnapshotHash { get; private set; } = string.Empty;
    public string RequestDigest { get; private set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }
    public ReadOnlyMemory<byte> ProtectedStatement => protectedStatement.ToArray();

    public static ModerationContentResponse Capture(
        Guid operationId, ModerationNotice notice, ModerationNoticeSource source,
        ModerationNoticeDisclosureApproval approval, ModerationNoticeContentRepresentative representative,
        string requestDigest, ReadOnlySpan<byte> ciphertext, DateTimeOffset submittedAt)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(representative);
        approval.ValidatePersistedState();
        var metadata = ModerationNoticeSource.StatusMetadata.Read(source.StatusMetadataJson, source.StatusMetadataHash);
        if (source.SubjectKind != 4 || source.RetainUntil is not { } ceiling || metadata is null
            || metadata.CaseId != approval.CaseId || metadata.HostBindingId != approval.HostBindingId
            || metadata.WriterTenantId != approval.TenantId || metadata.SnapshotHash != source.SnapshotHash
            || !metadata.ContentRepresentatives.Contains(representative)
            || !approval.Intent.ContentRepresentatives.Contains(representative)
            || notice.Audience != ModerationNoticeAudience.ContentRepresentative
            || notice.RecipientSubjectKind != 1 || notice.RecipientSubjectId != representative.HumanId
            || notice.EffectId != source.EffectId || notice.TrustDomainId != source.TrustDomainId
            || notice.TenantId != source.TenantId || notice.CreatedAt != source.CapturedAt
            || approval.EffectId != source.EffectId || approval.DecisionId != source.DecisionId
            || approval.DecisionRevision != source.DecisionRevision || approval.TenantId != source.TenantId
            || approval.TrustDomainId != source.TrustDomainId || approval.RetainUntil != ceiling
            || approval.RevokedAt is not null || approval.ApprovedAt > source.CapturedAt
            || submittedAt < source.CapturedAt || submittedAt >= ceiling)
            throw new ArgumentException("A Content response requires its original approved representative occurrence and finite purpose.");
        var row = new ModerationContentResponse
        {
            Id = operationId, HostBindingId = approval.HostBindingId, TrustDomainId = source.TrustDomainId,
            TenantId = approval.TenantId, NoticeId = notice.Id, EffectId = source.EffectId, CaseId = approval.CaseId,
            ContentSubjectId = source.SubjectId, RepresentativeHumanId = representative.HumanId,
            AssignmentId = representative.AssignmentId, AssignmentRevision = representative.AssignmentRevision,
            SnapshotHash = source.SnapshotHash, RequestDigest = requestDigest,
            SubmittedAt = submittedAt, RetainUntil = ceiling, protectedStatement = ciphertext.ToArray()
        };
        row.ValidatePersistedState();
        return row;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, NoticeId, EffectId, CaseId, ContentSubjectId, AssignmentId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (RepresentativeHumanId == Guid.Empty || AssignmentRevision is < 1 or long.MaxValue
            || SnapshotHash.Length != 64 || !SnapshotHash.All(char.IsAsciiHexDigitLower)
            || RequestDigest.Length != 64 || !RequestDigest.All(char.IsAsciiHexDigitLower)
            || protectedStatement.Length is < 1 or > 16384
            || SubmittedAt.Offset != TimeSpan.Zero || RetainUntil.Offset != TimeSpan.Zero
            || SubmittedAt == DateTimeOffset.MinValue || RetainUntil == DateTimeOffset.MaxValue
            || RetainUntil <= SubmittedAt)
            throw new ArgumentException("Content response custody requires exact authorship, protected text and the original finite ceiling.");
    }

    public override string ToString() => nameof(ModerationContentResponse);
}
