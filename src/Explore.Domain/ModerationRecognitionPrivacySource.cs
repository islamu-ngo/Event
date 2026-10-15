using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable AG02 terms for one exact host, publication, profile and mode. Stores no matching
/// inputs, tokens, subject history or key material. Pending capture must already contain complete
/// computed material before identifier purge; failure escalates to the named owners by the
/// adopted deadline for qualified recovery or legal erasure with affected paths closed.
/// Ordinary lawful erasure remains independent even if this source is absent or withdrawn.
/// </summary>
public sealed class ModerationRecognitionPrivacySource
{
    private ModerationRecognitionPrivacySource() { }
    private readonly List<ModerationRecognitionPrivacyRetention> retention = [];
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
    public string ProposedStateDigest { get; private set; } = string.Empty;
    public ModerationRecognitionPrivacyPurpose Purposes { get; private set; }
    public ModerationScopeKind ScopeCeiling { get; private set; }
    public ModerationRecognitionPrivacyLinkage Linkage { get; private set; }
    public ModerationRecognitionPrivacyOrdinaryErasure OrdinaryErasure { get; private set; }
    public ModerationRecognitionPrivacyCorrection Correction { get; private set; }
    public ModerationRecognitionPrivacyCatastrophicDisposition CatastrophicDisposition { get; private set; }
    public ModerationRecognitionPrivacyKeyObligations KeyObligations { get; private set; }
    public int ProtectedRemedyCalendarMonths { get; private set; }
    public long EscalationAfterTicks { get; private set; }
    public long LawfulRequestDeadlineTicks { get; private set; }
    public long MaximumBackupHorizonTicks { get; private set; }
    public long BackupSafetyMarginTicks { get; private set; }
    public Guid PrivacyOwnerHumanId { get; private set; }
    public Guid PrivacyControllerHumanId { get; private set; }
    public Guid RecoveryOwnerHumanId { get; private set; }
    public Guid RecoveryControllerHumanId { get; private set; }
    public Guid GovernanceControllerHumanId { get; private set; }
    public string IdentityAndIndependenceBasis { get; private set; } = string.Empty;
    public string PrivacyLegalMandateBasis { get; private set; } = string.Empty;
    public string RecoveryMandateBasis { get; private set; } = string.Empty;
    public Guid RecordedByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public long Revision { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public IReadOnlyList<ModerationRecognitionPrivacyRetention> Retention => retention.AsReadOnly();

    public ModerationRecognitionPrivacyTerms Terms => new(Purposes, ScopeCeiling, Linkage, OrdinaryErasure,
        Correction, CatastrophicDisposition, KeyObligations, ProtectedRemedyCalendarMonths,
        TimeSpan.FromTicks(EscalationAfterTicks), TimeSpan.FromTicks(LawfulRequestDeadlineTicks),
        TimeSpan.FromTicks(MaximumBackupHorizonTicks), TimeSpan.FromTicks(BackupSafetyMarginTicks));
    public ModerationRecognitionPrivacyOwnership Ownership => new(PrivacyOwnerHumanId, PrivacyControllerHumanId,
        RecoveryOwnerHumanId, RecoveryControllerHumanId, GovernanceControllerHumanId,
        IdentityAndIndependenceBasis, PrivacyLegalMandateBasis, RecoveryMandateBasis);

    /// <summary>
    /// A retirement obligation, not key-convergence evidence. Existing erased-input tokens remain
    /// on their original readable generation; no HMAC rehash can be invented after input erasure.
    /// Actual retirement additionally requires replica acknowledgement and restore-floor coverage.
    /// </summary>
    public TimeSpan MinimumReadableKeyHorizon => TimeSpan.FromTicks(checked(
        retention.Single(row => row.RecordClass == ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial).MaximumRetentionTicks
        + MaximumBackupHorizonTicks + BackupSafetyMarginTicks));

    public static ModerationRecognitionPrivacySource Record(
        Guid id, Guid hostId, Guid trustId, Guid tenantId, Guid policyId, long policyRevision,
        Guid publicationId, Guid definitionId, Guid profileId, int modeCode, string proposedStateDigest,
        ModerationRecognitionPrivacyTerms terms, ModerationRecognitionPrivacyOwnership ownership,
        IEnumerable<ModerationRecognitionPrivacyRetentionTerms> retentionTerms, Guid humanId,
        long writerGeneration, DateTimeOffset now, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(retentionTerms);
        terms.Validate();
        ownership.Validate(humanId);
        var source = new ModerationRecognitionPrivacySource
        {
            Id = id, HostBindingId = hostId, TrustDomainId = trustId, TenantId = tenantId,
            PolicyId = policyId, PolicyRevision = policyRevision, PublicationId = publicationId,
            DefinitionId = definitionId, ProfileId = profileId, ModeCode = modeCode, ProposedStateDigest = proposedStateDigest,
            Purposes = terms.Purposes, ScopeCeiling = terms.ScopeCeiling, Linkage = terms.Linkage,
            OrdinaryErasure = terms.OrdinaryErasure, Correction = terms.Correction, CatastrophicDisposition = terms.CatastrophicDisposition,
            KeyObligations = terms.KeyObligations, ProtectedRemedyCalendarMonths = terms.ProtectedRemedyCalendarMonths,
            EscalationAfterTicks = terms.EscalationAfter.Ticks, LawfulRequestDeadlineTicks = terms.LawfulRequestDeadline.Ticks,
            MaximumBackupHorizonTicks = terms.MaximumBackupHorizon.Ticks, BackupSafetyMarginTicks = terms.BackupSafetyMargin.Ticks,
            PrivacyOwnerHumanId = ownership.PrivacyOwnerHumanId, PrivacyControllerHumanId = ownership.PrivacyControllerHumanId,
            RecoveryOwnerHumanId = ownership.RecoveryOwnerHumanId, RecoveryControllerHumanId = ownership.RecoveryControllerHumanId,
            GovernanceControllerHumanId = ownership.GovernanceControllerHumanId,
            IdentityAndIndependenceBasis = ownership.IdentityAndIndependenceBasis,
            PrivacyLegalMandateBasis = ownership.PrivacyLegalMandateBasis, RecoveryMandateBasis = ownership.RecoveryMandateBasis,
            RecordedByHumanId = humanId, WriterGeneration = writerGeneration, Revision = 1, RecordedAt = now, ValidUntil = validUntil
        };
        source.retention.AddRange(retentionTerms.OrderBy(row => row.RecordClass)
            .Select(row => ModerationRecognitionPrivacyRetention.Capture(id, row)));
        source.ValidatePersistedState();
        return source;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, PolicyId, PublicationId, DefinitionId, ProfileId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        Terms.Validate();
        Ownership.Validate(RecordedByHumanId);
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (Revision != 1 || PolicyRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || WriterGeneration is < 1 or long.MaxValue || !ModerationDeploymentSource.Digest(ProposedStateDigest)
            || retention.Count != 4 || retention.Select(row => row.RecordClass).Distinct().Count() != 4)
            throw new ArgumentException("Recognition privacy requires exact native generations and every distinct record-class obligation.");
        foreach (var row in retention)
        {
            row.ToTerms().Validate();
            if (row.SourceId != Id) throw new ArgumentException("Retention belongs to the exact policy source.");
        }
        var pending = retention.Single(row => row.RecordClass == ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition);
        var matching = retention.Single(row => row.RecordClass == ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial);
        var correction = retention.Single(row => row.RecordClass == ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence);
        var receipts = retention.Single(row => row.RecordClass == ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt);
        var backup = checked(MaximumBackupHorizonTicks + BackupSafetyMarginTicks);
        // 31 days per month is a conservative storage ceiling check, never the actual remedy clock.
        // Actual protected eligibility is notification.AddMonths(ProtectedRemedyCalendarMonths).
        if (pending.MaximumRetentionTicks < LawfulRequestDeadlineTicks
            || correction.MaximumRetentionTicks < TimeSpan.FromDays(31L * ProtectedRemedyCalendarMonths).Ticks
            || receipts.MaximumRetentionTicks < backup
            || matching.MaximumRetentionTicks >= long.MaxValue - backup)
            throw new ArgumentException("Retention must cover finite settlement, calendar remedy floors and backup replay without overflowing key horizons.");
    }

    public override string ToString() => nameof(ModerationRecognitionPrivacySource);
}

public sealed class ModerationRecognitionPrivacyRetention
{
    private ModerationRecognitionPrivacyRetention() { }
    public Guid SourceId { get; private set; }
    public ModerationRecognitionPrivacyRecordClass RecordClass { get; private set; }
    public long MaximumRetentionTicks { get; private set; }
    public ModerationRecognitionPrivacyHold Hold { get; private set; }
    public long MaximumHoldTicks { get; private set; }
    public long HoldReviewWithinTicks { get; private set; }
    internal static ModerationRecognitionPrivacyRetention Capture(Guid sourceId, ModerationRecognitionPrivacyRetentionTerms terms)
    {
        terms.Validate();
        return new()
        {
            SourceId = sourceId, RecordClass = terms.RecordClass, MaximumRetentionTicks = terms.MaximumRetention.Ticks,
            Hold = terms.Hold, MaximumHoldTicks = terms.MaximumHold.Ticks, HoldReviewWithinTicks = terms.HoldReviewWithin.Ticks
        };
    }
    public ModerationRecognitionPrivacyRetentionTerms ToTerms() => new(RecordClass, TimeSpan.FromTicks(MaximumRetentionTicks),
        Hold, TimeSpan.FromTicks(MaximumHoldTicks), TimeSpan.FromTicks(HoldReviewWithinTicks));
}
