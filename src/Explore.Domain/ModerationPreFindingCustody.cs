using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Case-specific policy terms, not a legal default or a receipt lifetime. The named privacy
/// owner must independently accept the complete purpose and finite limit before any capture.
/// </summary>
public sealed record ModerationPreFindingCustodyTerms(
    TimeSpan MaximumRetention, int MaximumTextBytes, string Purpose, string PrivacyLegalMandate)
{
    public void Validate()
    {
        if (MaximumRetention <= TimeSpan.Zero || MaximumRetention == TimeSpan.MaxValue
            || MaximumTextBytes is < 1 or > 65536
            || string.IsNullOrWhiteSpace(Purpose) || Purpose.Length > 2048
            || string.IsNullOrWhiteSpace(PrivacyLegalMandate) || PrivacyLegalMandate.Length > 2048)
            throw new ArgumentException("Pre-finding custody requires substantive purpose, mandate and explicit finite limits.");
    }

    public override string ToString() => nameof(ModerationPreFindingCustodyTerms);
}

/// <summary>
/// A separately accepted pre-finding purpose. It cannot borrow confidential allegation or
/// post-decision retention. Replacement policy cannot renew any existing notice or response.
/// The supplied source is pinned; current-head selection remains the native writer's responsibility.
/// </summary>
public sealed class ModerationPreFindingCustody
{
    private ModerationPreFindingCustody() { }

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public Guid AttributionSourceId { get; private set; }
    public long AttributionGeneration { get; private set; }
    public long EvidenceRevision { get; private set; }
    public Guid ProposedByHumanId { get; private set; }
    public Guid PrivacyOwnerHumanId { get; private set; }
    public long MaximumRetentionTicks { get; private set; }
    public int MaximumTextBytes { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public string PrivacyLegalMandate { get; private set; } = string.Empty;
    public DateTimeOffset ProposedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public long Revision { get; private set; }
    public ModerationPreFindingCustodyTerms Terms =>
        new(TimeSpan.FromTicks(MaximumRetentionTicks), MaximumTextBytes, Purpose, PrivacyLegalMandate);

    public static ModerationPreFindingCustody Propose(Guid id, Guid hostId, ModerationCase owner,
        ModerationCaseAttributionSource attribution, Guid recorder, Guid privacyOwner, ModerationPreFindingCustodyTerms terms,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(terms);
        owner.ValidatePersistedState();
        attribution.ValidatePersistedState();
        terms.Validate();
        if (owner.AttributionSourceId is null || owner.Subject is null || owner.State != ModerationCaseState.Open
            || owner.Scope.Kind != ModerationScopeKind.Tenant
            || owner.SubjectKind is not (ModerationSubjectKind.Content or ModerationSubjectKind.Human)
            || owner.Subject.Origin != (owner.SubjectKind == ModerationSubjectKind.Content ? owner.Scope : owner.Scope.Parent)
            || owner.OccurrenceCaseId != attribution.OccurrenceCaseId || owner.OccurrenceId != attribution.OccurrenceId
            || owner.OccurrenceEvidenceRevision is null || attribution.EvidenceCaseRevision < owner.OccurrenceEvidenceRevision
            || attribution.CaseId != owner.Id || attribution.SubjectKind != owner.SubjectKind
            || attribution.SubjectId != owner.SubjectId || attribution.TenantId != owner.Scope.Id
            || attribution.TrustDomainId != owner.Scope.TrustDomainId || attribution.HostBindingId != hostId
            || !attribution.IsCurrentAt(now) || now < owner.CreatedAt || validUntil > attribution.ValidUntil)
            throw new ArgumentException("Custody requires one independently attributed human or Content case.");
        var row = new ModerationPreFindingCustody
        {
            Id = id,
            HostBindingId = hostId,
            TrustDomainId = owner.Scope.TrustDomainId,
            TenantId = owner.Scope.Id,
            CaseId = owner.Id,
            CaseRevision = owner.Revision,
            AttributionSourceId = attribution.Id,
            AttributionGeneration = attribution.Generation,
            EvidenceRevision = attribution.EvidenceCaseRevision,
            ProposedByHumanId = recorder,
            PrivacyOwnerHumanId = privacyOwner,
            MaximumRetentionTicks = terms.MaximumRetention.Ticks,
            MaximumTextBytes = terms.MaximumTextBytes,
            Purpose = terms.Purpose,
            PrivacyLegalMandate = terms.PrivacyLegalMandate,
            ProposedAt = now,
            ValidUntil = validUntil,
            Revision = 1
        };
        row.ValidatePersistedState();
        return row;
    }

    public void Accept(Guid actor, long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (actor != PrivacyOwnerHumanId || expectedRevision != Revision || Revision != 1
            || now.Offset != TimeSpan.Zero || now < ProposedAt || now >= ValidUntil)
            throw new InvalidOperationException("Only the named independent privacy owner can accept current custody terms.");
        AcceptedAt = now;
        Revision = 2;
    }

    public void Withdraw(Guid actor, long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (actor != PrivacyOwnerHumanId || expectedRevision != Revision || WithdrawnAt is not null
            || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue
            || now < (AcceptedAt ?? ProposedAt))
            throw new InvalidOperationException("Custody withdrawal requires its named owner and exact revision.");
        WithdrawnAt = now;
        Revision = checked(Revision + 1);
    }

    public bool IsCurrentAt(DateTimeOffset now) =>
        AcceptedAt is { } accepted && WithdrawnAt is null && now >= accepted && now < ValidUntil;

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, CaseId,
            AttributionSourceId, ProposedByHumanId, PrivacyOwnerHumanId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        Terms.Validate();
        ModerationQualificationPolicySource.RequireValidity(ProposedAt, ValidUntil);
        if (CaseRevision < 1 || AttributionGeneration < 1 || EvidenceRevision < 1
            || ProposedByHumanId == PrivacyOwnerHumanId
            || AcceptedAt < ProposedAt || AcceptedAt >= ValidUntil
            || AcceptedAt is { } accepted && accepted.Offset != TimeSpan.Zero
            || WithdrawnAt < (AcceptedAt ?? ProposedAt)
            || WithdrawnAt is { } withdrawn
                && (withdrawn.Offset != TimeSpan.Zero || withdrawn == DateTimeOffset.MaxValue)
            || Revision != 1 + (AcceptedAt is null ? 0 : 1) + (WithdrawnAt is null ? 0 : 1)
            || MaximumRetentionTicks >= DateTimeOffset.MaxValue.UtcTicks - ProposedAt.UtcTicks)
            throw new ArgumentException("Pre-finding custody has inconsistent ownership or finite state.");
    }

    public override string ToString() => nameof(ModerationPreFindingCustody);
}
