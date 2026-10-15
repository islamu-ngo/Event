namespace Explore.Domain;

/// <summary>
/// Independent finite adoption of one complete retained profile selection.
/// Stored mode and prerequisite codes describe the adopted configuration, not measured deployment conformance.
/// </summary>
public sealed class ModerationProfileAdoption
{
    private ModerationProfileAdoption() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SelectionId { get; private set; }
    public long SelectionRevision { get; private set; }
    public long SelectionWriterGeneration { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid ProfileId { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public int ModeCode { get; private set; }
    public int PrerequisiteMask { get; private set; }
    public Guid AdoptedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset AdoptedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public Guid? RevokedByHumanId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static ModerationProfileAdoption Adopt(
        Guid id, ModerationContributionSelection selection, int modeCode, int prerequisiteMask,
        Guid humanId, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        selection.ValidatePersistedState();
        if (humanId == selection.AuthoredByHumanId || now < selection.RecordedAt || validUntil > selection.ValidUntil)
            throw new ArgumentException("Profile adoption requires independent approval of the exact live selection.");
        var row = new ModerationProfileAdoption
        {
            Id = id, HostBindingId = selection.HostBindingId, TrustDomainId = selection.TrustDomainId,
            TenantId = selection.TenantId, SelectionId = selection.Id, SelectionRevision = selection.Revision,
            SelectionWriterGeneration = selection.WriterGeneration, PublicationId = selection.PublicationId,
            ProfileId = selection.ProfileId, CaseId = selection.CaseId, CaseRevision = selection.CaseRevision,
            ModeCode = modeCode, PrerequisiteMask = prerequisiteMask, AdoptedByHumanId = humanId,
            WriterGeneration = writerGeneration, Revision = 1, AdoptedAt = now.ToUniversalTime(), ValidUntil = validUntil
        };
        row.ValidatePersistedState();
        return row;
    }

    public void Revoke(long expectedRevision, Guid humanId, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || RevokedAt is not null)
            throw new InvalidOperationException("Profile adoption revocation requires the exact live revision.");
        ModerationHostBinding.RequireUuidV7(humanId, nameof(humanId));
        now = now.ToUniversalTime();
        if (now < AdoptedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Profile revocation requires accountable administration and non-regressing finite time.");
        Revision = checked(Revision + 1);
        RevokedByHumanId = humanId;
        RevokedAt = now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, SelectionId,
            PublicationId, ProfileId, CaseId, AdoptedByHumanId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (Id == SelectionId || SelectionRevision != 1 || SelectionWriterGeneration is < 1 or long.MaxValue
            || CaseRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || PrerequisiteMask is < 1 or > 31 || (PrerequisiteMask & 17) != 17
            || WriterGeneration <= SelectionWriterGeneration || WriterGeneration == long.MaxValue)
            throw new ArgumentException("A profile adoption requires exact original coordinates, a known mode and explicit baseline prerequisites.");
        ModerationQualificationPolicySource.RequireValidity(AdoptedAt, ValidUntil);
        if (!(Revision == 1 && RevokedAt is null && RevokedByHumanId is null
            || Revision == 2 && RevokedAt is { } revoked && RevokedByHumanId is { } human && human != Guid.Empty
                && revoked >= AdoptedAt && revoked != DateTimeOffset.MaxValue && revoked.Offset == TimeSpan.Zero))
            throw new ArgumentException("Profile adoption revocation state is inconsistent.");
        if (RevokedByHumanId is { } revoker)
            ModerationHostBinding.RequireUuidV7(revoker, nameof(RevokedByHumanId));
    }

    public override string ToString() => nameof(ModerationProfileAdoption);
}
