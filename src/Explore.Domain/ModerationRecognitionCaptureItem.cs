using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Domain;

/// <summary>
/// Immutable computed material, never an identifier locator or deferred-computation promise.
/// Dropping eligibility does not alter original receipt material or extend the original interval.
/// </summary>
public sealed class ModerationRecognitionCaptureItem
{
    private ModerationRecognitionCaptureItem() { }
    public Guid Id { get; private set; }
    public Guid IntentId { get; private set; }
    public Guid ErasureSubjectId { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid SubjectSolutionId { get; private set; }
    public Guid SubjectInstanceId { get; private set; }
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long SourceGeneration { get; private set; }
    public Guid AdoptionId { get; private set; }
    public long AdoptionRevision { get; private set; }
    public long AdoptionGeneration { get; private set; }
    public long RingGeneration { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public AuthenticationProviderKind ProviderKind { get; private set; }
    public ModerationReferenceRecognition Purpose { get; private set; }
    public string KeyId { get; private set; } = string.Empty;
    public long KeyGeneration { get; private set; }
    public int SerializationVersion { get; private set; }
    public string Digest { get; private set; } = string.Empty;
    public string KeyVerificationTag { get; private set; } = string.Empty;
    public Guid SanctionId { get; private set; }
    public long SanctionRevision { get; private set; }
    public Guid DecisionId { get; private set; }
    public long DecisionRevision { get; private set; }
    public Guid FindingId { get; private set; }
    public Guid? ParentCaptureItemId { get; private set; }
    public Guid? ContinuityBindingId { get; private set; }
    public Guid RuleOwnerId { get; private set; }
    public string RuleCode { get; private set; } = string.Empty;
    public long RuleRevision { get; private set; }
    public int OriginalCapabilityMask { get; private set; }
    public int PurposeCapabilityMask { get; private set; }
    public ModerationScopeKind ScopeKind { get; private set; }
    public Guid? ScopeSolutionId { get; private set; }
    public Guid? ScopeInstanceId { get; private set; }
    public Guid? ScopeTenantId { get; private set; }
    public Guid? ScopeCommunityId { get; private set; }
    public Guid? ScopeResourceId { get; private set; }
    public Guid? ScopeEventId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public DateTimeOffset? ReviewAt { get; private set; }
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset OriginalCapturedAt { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }
    public DateTimeOffset? DroppedAt { get; private set; }
    public AuthorityScope Scope => AuthorityScope.FromCoordinates(ScopeKind, TrustDomainId,
        ScopeSolutionId, ScopeInstanceId, ScopeTenantId, ScopeCommunityId, ScopeResourceId, ScopeEventId);
    public ModerationRecognitionToken Token => new(TrustDomainId, ProviderKind, Purpose,
        KeyId, KeyGeneration, SerializationVersion, Digest);

    /// <summary>Commits every immutable receipt coordinate; eligibility corrections are excluded.</summary>
    public string ComputeMaterialReceipt() => ModerationRecognitionPending.ComputeItemReceipt(this);

    public static ModerationRecognitionCaptureItem Capture(Guid id, Guid intentId,
        ModerationDecision decision, ModerationRecognitionCapturedMaterial material,
        Guid sourceId, long sourceRevision, long sourceGeneration,
        Guid adoptionId, long adoptionRevision, long adoptionGeneration, long ringGeneration)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(material);
        var contribution = material.Contribution;
        if (decision.Id != contribution.DecisionId || decision.Revision != contribution.DecisionRevision
            || decision.Subject.Kind != ModerationSubjectKind.Human
            || decision.Subject.Origin.Kind != ModerationScopeKind.Instance || decision.Scope != contribution.Scope)
            throw new ArgumentException("Capture must preserve the original canonical subject and decision.");
        var scope = contribution.Scope.Coordinates;
        var row = new ModerationRecognitionCaptureItem
        {
            Id = id, IntentId = intentId, SubjectId = decision.SubjectId, ErasureSubjectId = decision.SubjectId,
            SubjectSolutionId = decision.Subject.Origin.Parent!.Id, SubjectInstanceId = decision.Subject.Origin.Id,
            SourceId = sourceId, SourceRevision = sourceRevision, SourceGeneration = sourceGeneration,
            AdoptionId = adoptionId, AdoptionRevision = adoptionRevision, AdoptionGeneration = adoptionGeneration,
            RingGeneration = ringGeneration, TrustDomainId = material.Token.TrustDomainId,
            ProviderKind = material.Token.ProviderKind, Purpose = material.Token.Purpose,
            KeyId = material.Token.KeyId, KeyGeneration = material.Token.KeyGeneration,
            SerializationVersion = material.Token.SerializationVersion, Digest = material.Token.Digest,
            KeyVerificationTag = material.KeyVerificationTag,
            SanctionId = contribution.SanctionId, SanctionRevision = contribution.SanctionRevision,
            DecisionId = contribution.DecisionId, DecisionRevision = contribution.DecisionRevision,
            FindingId = decision.FindingId ?? throw new ArgumentException("Capture requires the original final finding."),
            RuleOwnerId = contribution.Rule.OwnerId, RuleCode = contribution.Rule.Code, RuleRevision = contribution.Rule.Revision,
            OriginalCapabilityMask = decision.CapabilityMask,
            PurposeCapabilityMask = contribution.Capabilities.Aggregate(0, (mask, capability) => mask | (1 << (int)capability)),
            ScopeKind = scope.Kind, ScopeSolutionId = scope.SolutionId, ScopeInstanceId = scope.InstanceId,
            ScopeTenantId = scope.TenantId, ScopeCommunityId = scope.CommunityId, ScopeResourceId = scope.ResourceId,
            ScopeEventId = scope.EventId,
            StartsAt = contribution.StartsAt, EndsAt = contribution.EndsAt, ReviewAt = contribution.ReviewAt,
            CapturedAt = contribution.CapturedAt, OriginalCapturedAt = contribution.CapturedAt,
            RetainUntil = contribution.RetainUntil
        };
        row.ValidatePersistedState();
        return row;
    }

    /// <summary>
    /// Computes an alias receipt for the current erasure owner without issuing another sanction.
    /// The native writer must independently requalify the binding, parent receipt and retained fact.
    /// Every original contribution coordinate and its total retention ceiling remain unchanged.
    /// </summary>
    public static ModerationRecognitionCaptureItem Recapture(Guid id, Guid intentId, Guid erasureSubjectId,
        ModerationRecognitionCaptureItem original, ModerationRecognitionContinuityBinding binding,
        ModerationDecision decision, ModerationSanction sanction, ModerationRecognitionToken token,
        string keyVerificationTag, long ringGeneration, DateTimeOffset now)
    {
        original.ValidatePersistedState();
        binding.ValidatePersistedState();
        if (binding.FreshSubjectId != erasureSubjectId || erasureSubjectId == original.ErasureSubjectId
            || !binding.IsCurrent(original, decision, sanction, now)
            || token.TrustDomainId != original.TrustDomainId || token.Purpose != original.Purpose
            || token.ProviderKind == AuthenticationProviderKind.Local || intentId == original.IntentId || id == original.Id)
            throw new ArgumentException("Recapture requires the current owner's exact original bound contribution.");
        var row = (ModerationRecognitionCaptureItem)original.MemberwiseClone();
        row.Id = id;
        row.IntentId = intentId;
        row.ErasureSubjectId = erasureSubjectId;
        row.ParentCaptureItemId = original.Id;
        row.ContinuityBindingId = binding.Id;
        row.CapturedAt = now;
        row.ProviderKind = token.ProviderKind;
        row.KeyId = token.KeyId;
        row.KeyGeneration = token.KeyGeneration;
        row.SerializationVersion = token.SerializationVersion;
        row.Digest = token.Digest;
        row.KeyVerificationTag = keyVerificationTag;
        row.RingGeneration = ringGeneration;
        row.ValidatePersistedState();
        return row;
    }

    public bool PreservesOriginalContribution(ModerationRecognitionCaptureItem original) =>
        SubjectId == original.SubjectId && SubjectSolutionId == original.SubjectSolutionId
        && SubjectInstanceId == original.SubjectInstanceId && TrustDomainId == original.TrustDomainId
        && FindingId == original.FindingId && DecisionId == original.DecisionId && DecisionRevision == original.DecisionRevision
        && SanctionId == original.SanctionId && SanctionRevision == original.SanctionRevision
        && SourceId == original.SourceId && SourceRevision == original.SourceRevision && SourceGeneration == original.SourceGeneration
        && AdoptionId == original.AdoptionId && AdoptionRevision == original.AdoptionRevision
        && AdoptionGeneration == original.AdoptionGeneration && Scope == original.Scope
        && RuleOwnerId == original.RuleOwnerId && RuleCode == original.RuleCode && RuleRevision == original.RuleRevision
        && Purpose == original.Purpose && PurposeCapabilityMask == original.PurposeCapabilityMask
        && OriginalCapabilityMask == original.OriginalCapabilityMask && StartsAt == original.StartsAt
        && EndsAt == original.EndsAt && ReviewAt == original.ReviewAt && RetainUntil == original.RetainUntil
        && OriginalCapturedAt == original.OriginalCapturedAt;

    public bool MatchesCurrent(ModerationDecision decision, ModerationSanction sanction, DateTimeOffset now) =>
        DroppedAt is null && now.Offset == TimeSpan.Zero && now >= CapturedAt && now < RetainUntil
        && decision.Id == DecisionId && decision.SubjectKind == ModerationSubjectKind.Human && decision.SubjectId == SubjectId
        && decision.Subject.Origin == AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(TrustDomainId), SubjectSolutionId), SubjectInstanceId)
        && decision.Revision >= DecisionRevision && decision.FindingId == FindingId
        && decision.State is ModerationDecisionState.Active or ModerationDecisionState.Scheduled
        && decision.Scope == Scope && decision.CapabilityMask == OriginalCapabilityMask
        && decision.Rule == ModerationRuleReference.Create(RuleOwnerId, RuleCode, RuleRevision)
        && decision.StartsAt == StartsAt && decision.EndsAt == EndsAt && decision.ReviewAt == ReviewAt
        && sanction.Id == SanctionId && sanction.Revision >= SanctionRevision
        && sanction.DecisionId == DecisionId && sanction.Subject == decision.Subject && sanction.Scope == Scope
        && sanction.Rule == decision.Rule && sanction.CapabilityMask == OriginalCapabilityMask
        && sanction.StartsAt == StartsAt && sanction.EndsAt == EndsAt && sanction.ReviewAt == ReviewAt
        && now >= decision.UpdatedAt && now >= sanction.UpdatedAt && sanction.IsEffectiveAt(now);

    public void Drop(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || now < CapturedAt || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Correction requires a finite non-regressing observation.");
        DroppedAt ??= now;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, IntentId, SourceId, AdoptionId, SubjectSolutionId, SubjectInstanceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (ParentCaptureItemId is { } parent) ModerationHostBinding.RequireUuidV7(parent, nameof(ParentCaptureItemId));
        if (ContinuityBindingId is { } binding) ModerationHostBinding.RequireUuidV7(binding, nameof(ContinuityBindingId));
        _ = Scope;
        _ = Token;
        _ = ModerationRuleReference.Create(RuleOwnerId, RuleCode, RuleRevision);
        var actionMask = (1 << (int)ModerationCapability.Publish) | (1 << (int)ModerationCapability.Upload)
            | (1 << (int)ModerationCapability.ReadResource) | (1 << (int)ModerationCapability.Manage)
            | (1 << (int)ModerationCapability.Participate);
        var enrollmentMask = (1 << (int)ModerationCapability.Enrollment) | (1 << (int)ModerationCapability.Admission)
            | (1 << (int)ModerationCapability.PrivateLogistics);
        var purposeMask = Purpose == ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
            ? actionMask : enrollmentMask;
        if (SubjectId == Guid.Empty || ErasureSubjectId == Guid.Empty || FindingId == Guid.Empty
            || SanctionId == Guid.Empty || DecisionId == Guid.Empty
            || (ParentCaptureItemId is null) != (ContinuityBindingId is null) || ParentCaptureItemId == Id
            || ParentCaptureItemId is null && (ErasureSubjectId != SubjectId || OriginalCapturedAt != CapturedAt)
            || ParentCaptureItemId is not null && (ErasureSubjectId == SubjectId
                || Purpose != ModerationReferenceRecognition.SeparatelyQualifiedFinalAction
                || ProviderKind == AuthenticationProviderKind.Local)
            || SourceRevision != 1 || SourceGeneration is < 1 or long.MaxValue
            || AdoptionRevision != 1 || AdoptionGeneration <= SourceGeneration || AdoptionGeneration == long.MaxValue
            || RingGeneration is < 1 or long.MaxValue || SanctionRevision < 1 || DecisionRevision < 1
            || OriginalCapabilityMask <= 0 || PurposeCapabilityMask <= 0
            || (OriginalCapabilityMask & ~(actionMask | enrollmentMask)) != 0
            || PurposeCapabilityMask != (OriginalCapabilityMask & purposeMask)
            || KeyVerificationTag.Length != 64 || !KeyVerificationTag.All(char.IsAsciiHexDigitLower)
            || CapturedAt.Offset != TimeSpan.Zero || CapturedAt == DateTimeOffset.MinValue
            || OriginalCapturedAt.Offset != TimeSpan.Zero || OriginalCapturedAt == DateTimeOffset.MinValue
            || OriginalCapturedAt > CapturedAt
            || RetainUntil.Offset != TimeSpan.Zero || RetainUntil <= CapturedAt || RetainUntil == DateTimeOffset.MaxValue
            || StartsAt.Offset != TimeSpan.Zero || StartsAt > OriginalCapturedAt
            || EndsAt is { } end && (end.Offset != TimeSpan.Zero || end <= CapturedAt)
            || EndsAt is null && ReviewAt is null
            || ReviewAt is { } review && (review.Offset != TimeSpan.Zero || review <= StartsAt || review > EndsAt)
            || DroppedAt is { } dropped && (dropped.Offset != TimeSpan.Zero || dropped < CapturedAt
                || dropped == DateTimeOffset.MaxValue))
            throw new ArgumentException("A durable item requires complete original material and finite eligibility.");
    }

    public override string ToString() => nameof(ModerationRecognitionCaptureItem);
}
