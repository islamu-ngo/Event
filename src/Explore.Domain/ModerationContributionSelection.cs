using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Domain;

/// <summary>
/// Immutable authored complete contribution for one future effect. Native target generations and
/// their independent adoption remain separate authorities; recording this draft grants nothing.
/// </summary>
public sealed class ModerationContributionSelection
{
    public required Guid Id { get; init; }
    public required Guid EffectId { get; init; }
    public required Guid DecisionId { get; init; }
    public required long DecisionRevision { get; init; }
    public required Guid CaseId { get; init; }
    public required long CaseRevision { get; init; }
    public required Guid PolicyId { get; init; }
    public required long PolicyRevision { get; init; }
    public required Guid PublicationId { get; init; }
    public required Guid ProfileId { get; init; }
    public required Guid HostBindingId { get; init; }
    public required Guid TrustDomainId { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid AuthoredByHumanId { get; init; }
    public required long WriterGeneration { get; init; }
    public required long Revision { get; init; }
    public required string SelectionDigest { get; init; }
    public required DateTimeOffset StartsAt { get; init; }
    public required DateTimeOffset? EventWindowEnd { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    private List<ModerationContributionBinding> _bindings = [];
    public required IReadOnlyList<ModerationContributionBinding> Bindings
    { get => _bindings.AsReadOnly(); init => _bindings = value.ToList(); }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, EffectId, DecisionId, CaseId, PolicyId, PublicationId, ProfileId,
            HostBindingId, TrustDomainId, TenantId, AuthoredByHumanId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (DecisionRevision is < 2 or long.MaxValue || CaseRevision is < 1 or long.MaxValue
            || PolicyRevision is < 1 or long.MaxValue
            || WriterGeneration is < 1 or long.MaxValue || Revision != 1
            || SelectionDigest is null || SelectionDigest.Length != 64 || !SelectionDigest.All(char.IsAsciiHexDigitLower)
            || !Instant(StartsAt) || !Instant(RecordedAt) || !Instant(ValidUntil) || ValidUntil <= RecordedAt
            || EventWindowEnd is { } end && (!Instant(end) || end <= StartsAt)
            || _bindings.Count is < 2 or > 64
            || !_bindings.Select(row => row.Ordinal).Order().SequenceEqual(Enumerable.Range(0, _bindings.Count))
            || !_bindings.Any(row => row.Kind == ModerationContributionBindingKind.Effect)
            || _bindings.Count(row => row.Kind == ModerationContributionBindingKind.Consequence && row.Strategy is null) != 1)
            throw new ArgumentException("A contribution draft requires complete bounded immutable bindings and exact native provenance.");
        foreach (var binding in _bindings)
        {
            binding.ValidatePersistedState();
            if (binding.SelectionId != Id || binding.Scope.TrustDomainId != TrustDomainId)
                throw new ArgumentException("Contribution bindings cannot be transplanted between selection or trust domains.");
        }
    }

    private static bool Instant(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
    public override string ToString() => nameof(ModerationContributionSelection);
}

public enum ModerationContributionBindingKind { Effect = 1, Consequence = 2 }

/// <summary>Typed selection coordinates, never a current native source stamp or approved target.</summary>
public sealed class ModerationContributionBinding
{
    public required Guid SelectionId { get; init; }
    public required int Ordinal { get; init; }
    public required ModerationContributionBindingKind Kind { get; init; }
    public required ModerationReferenceEffectKind? EffectKind { get; init; }
    public required ModerationReferenceEffectMode? EffectMode { get; init; }
    public required ModerationReferenceTarget? EffectTarget { get; init; }
    public required ModerationCapability? Capability { get; init; }
    public required ModerationReferenceExecution? Execution { get; init; }
    public required ModerationAdditiveStrategyKind? Strategy { get; init; }
    public ModerationScopeKind ScopeKind { get; private set; }
    public Guid ScopeTrustDomainId { get; private set; }
    public Guid? ScopeSolutionId { get; private set; }
    public Guid? ScopeInstanceId { get; private set; }
    public Guid? ScopeTenantId { get; private set; }
    public Guid? ScopeCommunityId { get; private set; }
    public Guid? ScopeResourceId { get; private set; }
    public Guid? ScopeEventId { get; private set; }
    public required AuthorityScope Scope
    {
        get => AuthorityScope.FromCoordinates(ScopeKind, ScopeTrustDomainId, ScopeSolutionId,
            ScopeInstanceId, ScopeTenantId, ScopeCommunityId, ScopeResourceId, ScopeEventId);
        init => (ScopeKind, ScopeTrustDomainId, ScopeSolutionId, ScopeInstanceId,
            ScopeTenantId, ScopeCommunityId, ScopeResourceId, ScopeEventId)
            = value.Coordinates;
    }
    public required ModerationSubjectKind SubjectKind { get; init; }
    public required Guid SubjectId { get; init; }
    public required Guid TargetId { get; init; }
    public required long TargetRevision { get; init; }
    public required string Operation { get; init; }
    public required DateTimeOffset? WindowEnd { get; init; }
    public ModerationReferenceEffect? Effect => Kind == ModerationContributionBindingKind.Effect
        ? new(EffectKind!.Value, EffectMode!.Value, EffectTarget!.Value, Capability, Execution!.Value) : null;

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(SelectionId, nameof(SelectionId));
        if (Ordinal is < 0 or >= 64 || !Enum.IsDefined(Kind) || !Enum.IsDefined(SubjectKind)
            || SubjectId == Guid.Empty || TargetId == Guid.Empty || TargetRevision is < 1 or long.MaxValue
            || Operation is null || Operation.Length is < 1 or > 100
            || !Operation.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            || WindowEnd is { } end && (end.Offset != TimeSpan.Zero || end == DateTimeOffset.MinValue || end == DateTimeOffset.MaxValue))
            throw new ArgumentException("A contribution binding requires an exact qualified operation partition.");
        _ = Scope;
        if (Kind == ModerationContributionBindingKind.Effect
            ? EffectKind is null || EffectMode is null || EffectTarget is null || Execution is null || Strategy is not null
                || !Enum.IsDefined(EffectKind.Value) || !Enum.IsDefined(EffectMode.Value)
                || !Enum.IsDefined(EffectTarget.Value) || !Enum.IsDefined(Execution.Value)
                || Capability is { } capability && !Enum.IsDefined(capability)
            : EffectKind is not null || EffectMode is not null || EffectTarget is not null || Execution is not null
                || Capability is not null || Strategy is { } strategy && !Enum.IsDefined(strategy))
            throw new ArgumentException("A binding must retain exactly one complete effect or base/additive consequence meaning.");
    }

    public override string ToString() => nameof(ModerationContributionBinding);
}
