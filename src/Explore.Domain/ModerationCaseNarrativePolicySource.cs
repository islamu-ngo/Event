using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

public enum ModerationCaseNarrativePurpose
{
    ConfidentialAllegation = 1,
    ParticipantInvestigation = 2,
    ProtectedContestation = 3
}

/// <summary>Explicit finite custody terms, never receipt lifetime, restriction authority or an erasure hold.</summary>
public sealed record ModerationCaseNarrativeTerms(
    ModerationCaseNarrativePurpose Purpose, TimeSpan MaximumRetention, int MaximumMaterialBytes)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Purpose)
            || MaximumRetention <= TimeSpan.Zero || MaximumRetention == TimeSpan.MaxValue
            || MaximumMaterialBytes is < 1 or > 65536)
            throw new ArgumentException("Narrative custody requires an explicit purpose and finite retention and size limits.");
    }
}

/// <summary>
/// Accountable governance assertions accepted separately by the named privacy owner. Canonical
/// controller and legal-mandate grounds are substantive evidence, not a role-derived approval.
/// Native acquisition independently rechecks every named human and each owner's actual role.
/// </summary>
public sealed record ModerationCaseNarrativeOwnership(
    Guid PrivacyOwnerHumanId, Guid PrivacyControllerHumanId, Guid GovernanceControllerHumanId,
    string IdentityAndIndependenceBasis, string PrivacyLegalMandateBasis)
{
    public void Validate(Guid recorder)
    {
        if (recorder == Guid.Empty || PrivacyOwnerHumanId == Guid.Empty || PrivacyControllerHumanId == Guid.Empty
            || GovernanceControllerHumanId == Guid.Empty || PrivacyOwnerHumanId == recorder
            || PrivacyControllerHumanId == GovernanceControllerHumanId || recorder == PrivacyControllerHumanId
            || PrivacyOwnerHumanId == GovernanceControllerHumanId
            || string.IsNullOrWhiteSpace(IdentityAndIndependenceBasis) || IdentityAndIndependenceBasis.Length > 2048
            || string.IsNullOrWhiteSpace(PrivacyLegalMandateBasis) || PrivacyLegalMandateBasis.Length > 2048)
            throw new ArgumentException("Narrative custody requires independent canonical controllers and substantive privacy mandate grounds.");
    }

    public override string ToString() => nameof(ModerationCaseNarrativeOwnership);
}

/// <summary>
/// Immutable tenant/target-kind/purpose source. No publication, profile, subject attribution,
/// adjudication or recognition key is required or conferred. New generations replace selection;
/// they never extend an already captured narrative's original physical deadline.
/// </summary>
public sealed class ModerationCaseNarrativePolicySource
{
    private ModerationCaseNarrativePolicySource() { }
    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public ModerationCaseTargetKind TargetKind { get; private set; }
    public ModerationCaseNarrativePurpose Purpose { get; private set; }
    public long MaximumRetentionTicks { get; private set; }
    public int MaximumMaterialBytes { get; private set; }
    public Guid PrivacyOwnerHumanId { get; private set; }
    public Guid PrivacyControllerHumanId { get; private set; }
    public Guid GovernanceControllerHumanId { get; private set; }
    public string IdentityAndIndependenceBasis { get; private set; } = string.Empty;
    public string PrivacyLegalMandateBasis { get; private set; } = string.Empty;
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public ModerationCaseNarrativeTerms Terms => new(Purpose, TimeSpan.FromTicks(MaximumRetentionTicks), MaximumMaterialBytes);
    public ModerationCaseNarrativeOwnership Ownership => new(PrivacyOwnerHumanId, PrivacyControllerHumanId,
        GovernanceControllerHumanId, IdentityAndIndependenceBasis, PrivacyLegalMandateBasis);

    public static ModerationCaseNarrativePolicySource Record(
        Guid id, Guid hostId, Guid trustId, Guid tenantId, ModerationCaseTargetKind targetKind,
        ModerationCaseNarrativeTerms terms, ModerationCaseNarrativeOwnership ownership,
        Guid recorder, long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(ownership);
        terms.Validate();
        ownership.Validate(recorder);
        var source = new ModerationCaseNarrativePolicySource
        {
            Id = id, HostBindingId = hostId, TrustDomainId = trustId, TenantId = tenantId, TargetKind = targetKind,
            Purpose = terms.Purpose, MaximumRetentionTicks = terms.MaximumRetention.Ticks,
            MaximumMaterialBytes = terms.MaximumMaterialBytes, PrivacyOwnerHumanId = ownership.PrivacyOwnerHumanId,
            PrivacyControllerHumanId = ownership.PrivacyControllerHumanId,
            GovernanceControllerHumanId = ownership.GovernanceControllerHumanId,
            IdentityAndIndependenceBasis = ownership.IdentityAndIndependenceBasis,
            PrivacyLegalMandateBasis = ownership.PrivacyLegalMandateBasis, RecordedByHumanId = recorder,
            WriterGeneration = writerGeneration, Revision = 1, RecordedAt = now, ValidUntil = validUntil
        };
        source.ValidatePersistedState();
        return source;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        Terms.Validate();
        Ownership.Validate(RecordedByHumanId);
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (!Enum.IsDefined(TargetKind)
            || Purpose == ModerationCaseNarrativePurpose.ParticipantInvestigation
                && TargetKind != ModerationCaseTargetKind.ParticipantIncident
            || Revision != 1 || WriterGeneration is < 1 or long.MaxValue
            || MaximumRetentionTicks >= DateTimeOffset.MaxValue.UtcTicks - RecordedAt.UtcTicks)
            throw new ArgumentException("Narrative policy requires exact generations and a representable finite custody deadline.");
    }

    public override string ToString() => nameof(ModerationCaseNarrativePolicySource);
}
