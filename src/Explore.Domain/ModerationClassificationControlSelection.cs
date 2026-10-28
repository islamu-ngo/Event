using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Domain;

/// <summary>
/// Immutable native classification control coordinates for accepted whole-Event material.
/// Validity bounds the retained receipts, not a sanction interval or permission to disclose.
/// The caller must qualify the selected profile composition, digest and current native receipts.
/// </summary>
public sealed class ModerationClassificationControlSelection
{
    private ModerationClassificationControlSelection() { }
    private readonly List<ModerationClassificationControlBinding> _bindings = [];
    public IReadOnlyList<ModerationClassificationControlBinding> Bindings => _bindings.AsReadOnly();
    public Guid Id { get; private set; }
    public long Revision { get; private set; }
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long SourceWriterGeneration { get; private set; }
    public DateTimeOffset SourceRecordedAt { get; private set; }
    public DateTimeOffset SourceValidUntil { get; private set; }
    public Guid ReviewId { get; private set; }
    public long ReviewRevision { get; private set; }
    public long ReviewWriterGeneration { get; private set; }
    public DateTimeOffset ReviewValidUntil { get; private set; }
    public Guid GenerationId { get; private set; }
    public long GenerationRevision { get; private set; }
    public long GenerationWriterGeneration { get; private set; }
    public DateTimeOffset GenerationObservedAt { get; private set; }
    public DateTimeOffset GenerationValidUntil { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid EventStamp { get; private set; }
    public Guid PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public Guid PublicationId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int ModeCode { get; private set; }
    public Guid OwnerHumanId { get; private set; }
    public Guid OwnerControllerHumanId { get; private set; }
    public Guid ReviewerHumanId { get; private set; }
    public Guid ReviewerControllerHumanId { get; private set; }
    public Guid AuthoredByHumanId { get; private set; }
    public long WriterGeneration { get; private set; }
    public string SelectionDigest { get; private set; } = string.Empty;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }

    public static ModerationClassificationControlSelection Record(
        Guid id, ModerationClassificationMaterialSource source, ModerationClassificationMaterialReview review,
        ModerationEventGeneration generation, Guid authoredByHumanId,
        IEnumerable<ModerationContributionBinding> bindings, string selectionDigest, long writerGeneration,
        DateTimeOffset now, DateTimeOffset validUntil)
    {
        source.ValidatePersistedState();
        review.ValidatePersistedState();
        generation.ValidatePersistedState();
        if (!review.MatchesCurrent(source, now) || source.EventGenerationId != generation.Id
            || source.EventGenerationRevision != generation.Revision || source.EventWriterGeneration != generation.WriterGeneration
            || source.EventId != generation.EventId || source.EventStamp != generation.EventStamp
            || source.HostBindingId != generation.HostBindingId || source.TrustDomainId != generation.TrustDomainId
            || source.TenantId != generation.TenantId)
            throw new ArgumentException("Classification control requires the exact independently accepted native Event material generation.");
        var row = new ModerationClassificationControlSelection
        {
            Id = id,
            Revision = 1,
            SourceId = source.Id,
            SourceRevision = source.Revision,
            SourceWriterGeneration = source.WriterGeneration,
            SourceRecordedAt = source.RecordedAt,
            SourceValidUntil = source.ValidUntil,
            ReviewId = review.Id,
            ReviewRevision = review.Revision,
            ReviewWriterGeneration = review.WriterGeneration,
            ReviewValidUntil = review.ValidUntil,
            GenerationId = generation.Id,
            GenerationRevision = generation.Revision,
            GenerationWriterGeneration = generation.WriterGeneration,
            GenerationObservedAt = generation.ObservedAt,
            GenerationValidUntil = generation.ValidUntil,
            HostBindingId = source.HostBindingId,
            TrustDomainId = source.TrustDomainId,
            TenantId = source.TenantId,
            EventId = source.EventId,
            EventStamp = source.EventStamp,
            PolicyId = source.PolicyId,
            PolicyRevision = source.PolicyRevision,
            PublicationId = source.PublicationId,
            ProfileId = source.ProfileId,
            ModeCode = source.ModeCode,
            OwnerHumanId = source.OwnerHumanId,
            OwnerControllerHumanId = source.OwnerControllerHumanId,
            ReviewerHumanId = review.ReviewerHumanId,
            ReviewerControllerHumanId = source.ReviewerControllerHumanId,
            AuthoredByHumanId = authoredByHumanId,
            WriterGeneration = writerGeneration,
            SelectionDigest = selectionDigest,
            StartsAt = review.AcceptedAt,
            RecordedAt = now,
            ValidUntil = validUntil
        };
        row._bindings.AddRange(bindings.Take(65).Select(ModerationClassificationControlBinding.Capture));
        row.ValidatePersistedState();
        return row;
    }

    /// <summary>Checks retained provenance only; it does not reauthenticate material completeness or native authority.</summary>
    public bool MatchesCurrent(ModerationClassificationMaterialSource source, ModerationClassificationMaterialReview review,
        ModerationEventGeneration generation, DateTimeOffset now) =>
        now.Offset == TimeSpan.Zero && now >= RecordedAt && now < ValidUntil
        && SourceId == source.Id && SourceRevision == source.Revision && SourceWriterGeneration == source.WriterGeneration
        && SourceRecordedAt == source.RecordedAt && SourceValidUntil == source.ValidUntil
        && ReviewId == review.Id && ReviewRevision == review.Revision && ReviewWriterGeneration == review.WriterGeneration
        && StartsAt == review.AcceptedAt && ReviewValidUntil == review.ValidUntil && review.MatchesCurrent(source, now)
        && GenerationId == generation.Id && GenerationRevision == generation.Revision
        && GenerationWriterGeneration == generation.WriterGeneration && GenerationObservedAt == generation.ObservedAt
        && GenerationValidUntil == generation.ValidUntil && EventId == generation.EventId && EventStamp == generation.EventStamp
        && HostBindingId == generation.HostBindingId && TrustDomainId == generation.TrustDomainId && TenantId == generation.TenantId
        && source.EventGenerationId == GenerationId && source.EventGenerationRevision == GenerationRevision
        && source.EventWriterGeneration == GenerationWriterGeneration && source.EventId == EventId && source.EventStamp == EventStamp
        && HostBindingId == source.HostBindingId && TrustDomainId == source.TrustDomainId && TenantId == source.TenantId
        && PolicyId == source.PolicyId && PolicyRevision == source.PolicyRevision && PublicationId == source.PublicationId
        && ProfileId == source.ProfileId && ModeCode == source.ModeCode && OwnerHumanId == source.OwnerHumanId
        && OwnerControllerHumanId == source.OwnerControllerHumanId && ReviewerHumanId == source.ReviewerHumanId
        && ReviewerControllerHumanId == source.ReviewerControllerHumanId;

    internal bool IsConflictingHuman(Guid humanId) =>
        humanId == OwnerHumanId || humanId == OwnerControllerHumanId || humanId == ReviewerHumanId
        || humanId == ReviewerControllerHumanId || humanId == AuthoredByHumanId;

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, SourceId, ReviewId, GenerationId, HostBindingId, TrustDomainId, TenantId,
            PolicyId, PublicationId, ProfileId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(SourceRecordedAt, SourceValidUntil);
        ModerationQualificationPolicySource.RequireValidity(StartsAt, ReviewValidUntil);
        ModerationQualificationPolicySource.RequireValidity(GenerationObservedAt, GenerationValidUntil);
        ModerationQualificationPolicySource.RequireValidity(RecordedAt, ValidUntil);
        if (new[] { Id, SourceId, ReviewId, GenerationId }.Distinct().Count() != 4
            || Revision != 1 || SourceRevision != 1 || ReviewRevision != 1 || GenerationRevision is < 1 or long.MaxValue
            || GenerationWriterGeneration is < 1 or long.MaxValue || SourceWriterGeneration <= GenerationWriterGeneration
            || ReviewWriterGeneration <= SourceWriterGeneration || WriterGeneration <= ReviewWriterGeneration
            || WriterGeneration == long.MaxValue || EventId == Guid.Empty || EventStamp == Guid.Empty
            || PolicyRevision is < 1 or long.MaxValue || ModeCode is < 1 or > 2
            || OwnerHumanId == Guid.Empty || OwnerControllerHumanId == Guid.Empty
            || ReviewerHumanId == Guid.Empty || ReviewerControllerHumanId == Guid.Empty || AuthoredByHumanId == Guid.Empty
            || ReviewerHumanId == OwnerHumanId || ReviewerHumanId == OwnerControllerHumanId
            || ReviewerControllerHumanId == OwnerHumanId || ReviewerControllerHumanId == OwnerControllerHumanId
            || AuthoredByHumanId == OwnerHumanId || AuthoredByHumanId == OwnerControllerHumanId
            || AuthoredByHumanId == ReviewerHumanId || AuthoredByHumanId == ReviewerControllerHumanId
            || StartsAt < SourceRecordedAt || StartsAt < GenerationObservedAt || RecordedAt < StartsAt
            || SourceValidUntil > GenerationValidUntil || ReviewValidUntil > SourceValidUntil || ValidUntil > ReviewValidUntil
            || SelectionDigest is null || SelectionDigest.Length != 64 || !SelectionDigest.All(char.IsAsciiHexDigitLower)
            || _bindings.Count is < 2 or > 64
            || !_bindings.Select(binding => binding.Ordinal).Order().SequenceEqual(Enumerable.Range(0, _bindings.Count))
            || !_bindings.Any(binding => binding.Kind == ModerationContributionBindingKind.Effect)
            || _bindings.Count(binding => binding.Kind == ModerationContributionBindingKind.Consequence && binding.Strategy is null) != 1
            || _bindings.Where(binding => binding.Kind == ModerationContributionBindingKind.Consequence && binding.Strategy is not null)
                .Select(binding => binding.Strategy).Distinct().Count()
                != _bindings.Count(binding => binding.Kind == ModerationContributionBindingKind.Consequence && binding.Strategy is not null)
            || _bindings.Where(binding => binding.Kind == ModerationContributionBindingKind.Effect)
                .Select(binding => binding.Value.Effect).Distinct().Count()
                != _bindings.Count(binding => binding.Kind == ModerationContributionBindingKind.Effect))
            throw new ArgumentException("Classification selection requires immutable complete typed bindings and finite independent receipt provenance.");
        foreach (var binding in _bindings)
        {
            binding.Value.ValidatePersistedState();
            if (binding.SelectionId != Id || binding.SubjectKind != ModerationSubjectKind.Content
                || binding.SubjectId != EventId || binding.TargetId != EventId || binding.TargetRevision != GenerationRevision
                || binding.ScopeKind != ModerationScopeKind.Event || binding.ScopeTrustDomainId != TrustDomainId
                || binding.ScopeTenantId != TenantId || binding.ScopeEventId != EventId
                || binding.Value.Scope != _bindings[0].Value.Scope || binding.WindowEnd is not null
                || binding.Kind == ModerationContributionBindingKind.Effect
                    && (binding.Execution != ModerationReferenceExecution.NativeOwner
                        || binding.EffectTarget != ModerationReferenceTarget.ClassifiedContentVersions))
                throw new ArgumentException("Classification control binds only exact native Content/Event partitions without sanction timing.");
        }
    }

    public override string ToString() => nameof(ModerationClassificationControlSelection);
}

/// <summary>
/// Separate persistence identity for a captured typed contribution binding. It cannot refer to
/// a decision selection; Value reuses the existing binding contract without sharing its EF identity.
/// </summary>
public sealed class ModerationClassificationControlBinding
{
    private ModerationClassificationControlBinding() { }
    public Guid SelectionId { get; private set; }
    public int Ordinal { get; private set; }
    public ModerationContributionBindingKind Kind { get; private set; }
    public ModerationReferenceEffectKind? EffectKind { get; private set; }
    public ModerationReferenceEffectMode? EffectMode { get; private set; }
    public ModerationReferenceTarget? EffectTarget { get; private set; }
    public ModerationCapability? Capability { get; private set; }
    public ModerationReferenceExecution? Execution { get; private set; }
    public ModerationAdditiveStrategyKind? Strategy { get; private set; }
    public ModerationScopeKind ScopeKind { get; private set; }
    public Guid ScopeTrustDomainId { get; private set; }
    public Guid? ScopeSolutionId { get; private set; }
    public Guid? ScopeInstanceId { get; private set; }
    public Guid? ScopeTenantId { get; private set; }
    public Guid? ScopeCommunityId { get; private set; }
    public Guid? ScopeResourceId { get; private set; }
    public Guid? ScopeEventId { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid TargetId { get; private set; }
    public long TargetRevision { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public DateTimeOffset? WindowEnd { get; private set; }
    public ModerationContributionBinding Value => new()
    {
        SelectionId = SelectionId,
        Ordinal = Ordinal,
        Kind = Kind,
        EffectKind = EffectKind,
        EffectMode = EffectMode,
        EffectTarget = EffectTarget,
        Capability = Capability,
        Execution = Execution,
        Strategy = Strategy,
        Scope = AuthorityScope.FromCoordinates(ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId, ScopeEventId),
        SubjectKind = SubjectKind,
        SubjectId = SubjectId,
        TargetId = TargetId,
        TargetRevision = TargetRevision,
        Operation = Operation,
        WindowEnd = WindowEnd
    };

    internal static ModerationClassificationControlBinding Capture(ModerationContributionBinding binding)
    {
        binding.ValidatePersistedState();
        return new()
        {
            SelectionId = binding.SelectionId,
            Ordinal = binding.Ordinal,
            Kind = binding.Kind,
            EffectKind = binding.EffectKind,
            EffectMode = binding.EffectMode,
            EffectTarget = binding.EffectTarget,
            Capability = binding.Capability,
            Execution = binding.Execution,
            Strategy = binding.Strategy,
            ScopeKind = binding.ScopeKind,
            ScopeTrustDomainId = binding.ScopeTrustDomainId,
            ScopeSolutionId = binding.ScopeSolutionId,
            ScopeInstanceId = binding.ScopeInstanceId,
            ScopeTenantId = binding.ScopeTenantId,
            ScopeCommunityId = binding.ScopeCommunityId,
            ScopeResourceId = binding.ScopeResourceId,
            ScopeEventId = binding.ScopeEventId,
            SubjectKind = binding.SubjectKind,
            SubjectId = binding.SubjectId,
            TargetId = binding.TargetId,
            TargetRevision = binding.TargetRevision,
            Operation = binding.Operation,
            WindowEnd = binding.WindowEnd
        };
    }

    public override string ToString() => nameof(ModerationClassificationControlBinding);
}
