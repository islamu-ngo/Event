using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// The revisioned current source of one existing attributed case, not a new occurrence or
/// permission. Source generations and the case's opening provenance remain historical facts.
/// The native writer must prove current original humans, grants, material and policy and
/// retain final transaction custody; this entity checks only the supplied native records.
/// </summary>
public sealed class ModerationCaseAttributionHead
{
    private ModerationCaseAttributionHead() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid SolutionId { get; private set; }
    public Guid InstanceId { get; private set; }
    public Guid OccurrenceCaseId { get; private set; }
    public Guid OccurrenceId { get; private set; }
    public long OccurrenceEvidenceRevision { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid OpeningSourceId { get; private set; }
    public Guid SourceId { get; private set; }
    public long Generation { get; private set; }
    public long Revision { get; private set; }

    public static ModerationCaseAttributionHead Open(
        Guid id, ModerationCase owner, ModerationCaseAttributionSource source,
        ModerationHostBinding host, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(host);
        RequireUtc(now);
        owner.ValidatePersistedState();
        source.ValidatePersistedState();
        if (owner.Subject is null || owner.OccurrenceCaseId is null
            || owner.OccurrenceEvidenceRevision is null || owner.AttributionSourceId != source.Id
            || source.Generation != 1 || source.EvidenceCaseRevision != owner.OccurrenceEvidenceRevision
            || source.QualifiedAt > owner.CreatedAt || !source.IsCurrentAt(owner.CreatedAt)
            || !source.IsCurrentAt(now) || now < owner.CreatedAt)
            throw new InvalidOperationException("Opening requires the actual first qualified source and attributed case.");

        var head = new ModerationCaseAttributionHead
        {
            Id = id,
            CaseId = owner.Id,
            TenantId = source.TenantId,
            TrustDomainId = source.TrustDomainId,
            HostBindingId = host.Id,
            SolutionId = host.SolutionId,
            InstanceId = host.InstanceId,
            OccurrenceCaseId = owner.OccurrenceCaseId.Value,
            OccurrenceId = owner.OccurrenceId,
            OccurrenceEvidenceRevision = owner.OccurrenceEvidenceRevision.Value,
            SubjectKind = owner.Subject.Kind,
            SubjectId = owner.Subject.Id,
            OpeningSourceId = source.Id,
            SourceId = source.Id,
            Generation = 1,
            Revision = 1
        };
        head.ValidatePersistedState();
        head.RequireOwner(owner, host);
        head.RequireSource(source);
        return head;
    }

    /// <summary>
    /// Selects a freshly qualified consecutive generation without changing either source,
    /// adjudication or payload. Fresh grant/epoch identities alone cannot renew live facts.
    /// The expected tuple and persistence concurrency token fence competing writers.
    /// </summary>
    public void Advance(
        Guid expectedSourceId, long expectedGeneration, long expectedRevision,
        ModerationCase owner, ModerationCaseAttributionSource prior,
        ModerationCaseAttributionSource next, ModerationHostBinding host, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(host);
        RequireUtc(now);
        ValidatePersistedState();
        RequireOwner(owner, host);
        prior.ValidatePersistedState();
        next.ValidatePersistedState();
        RequireSource(prior);
        RequireSource(next);
        if (expectedSourceId != SourceId || expectedGeneration != Generation || expectedRevision != Revision
            || prior.Id != SourceId || prior.Generation != Generation || next.Id == prior.Id || next.Id == OpeningSourceId
            || next.Generation != checked(Generation + 1)
            || next.QualifiedAt != now || !next.IsCurrentAt(now) || now < prior.QualifiedAt
            || next.OccurrenceCaseRevision < prior.OccurrenceCaseRevision
            || next.EvidenceCaseRevision < prior.EvidenceCaseRevision)
            throw new InvalidOperationException("Advancement requires the exact head and a fresh consecutive source.");

        var newFacts = next.MaterialFactId != prior.MaterialFactId
            || next.EvidenceSourceId != prior.EvidenceSourceId
            || next.EvidenceSourceVersion != prior.EvidenceSourceVersion
            || next.NativeResponsibilityDigest != prior.NativeResponsibilityDigest
            || next.Basis != prior.Basis;
        if (now < prior.ValidUntil && !newFacts)
            throw new InvalidOperationException("An unchanged live attribution cannot renew its own lifetime.");

        var revision = checked(Revision + 1);
        SourceId = next.Id;
        Generation = next.Generation;
        Revision = revision;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, CaseId, TenantId, TrustDomainId, HostBindingId, SolutionId, InstanceId,
            OccurrenceCaseId, OccurrenceId, SubjectId, OpeningSourceId, SourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(id));
        if (CaseId == OccurrenceCaseId || OccurrenceEvidenceRevision < 1
            || !Enum.IsDefined(SubjectKind) || Generation < 1 || Revision != Generation
            || (Generation == 1 ? SourceId != OpeningSourceId : SourceId == OpeningSourceId))
            throw new ArgumentException("The attribution head requires finite ownership and positive consecutive revisions.");
    }

    private void RequireOwner(ModerationCase owner, ModerationHostBinding host)
    {
        owner.ValidatePersistedState();
        if (owner.Id != CaseId || owner.State != ModerationCaseState.Open
            || owner.Scope.Kind != ModerationScopeKind.Tenant || owner.Scope.Id != TenantId
            || owner.Scope.TrustDomainId != TrustDomainId || owner.ScopeSolutionId != SolutionId
            || owner.ScopeInstanceId != InstanceId || owner.OccurrenceCaseId != OccurrenceCaseId
            || owner.OccurrenceId != OccurrenceId || owner.OccurrenceEvidenceRevision != OccurrenceEvidenceRevision
            || owner.AttributionSourceId != OpeningSourceId || owner.Subject is not { } subject
            || subject.Kind != SubjectKind || subject.Id != SubjectId
            || subject.Origin != (SubjectKind == ModerationSubjectKind.Content ? owner.Scope : owner.Scope.Parent)
            || host.Id != HostBindingId || host.TrustDomainId != TrustDomainId
            || host.SolutionId != SolutionId || host.InstanceId != InstanceId)
            throw new InvalidOperationException("The head cannot rewrite or reopen its original subject case or host ancestry.");
    }

    private void RequireSource(ModerationCaseAttributionSource source)
    {
        if (source.CaseId != CaseId || source.TenantId != TenantId || source.TrustDomainId != TrustDomainId
            || source.HostBindingId != HostBindingId || source.OccurrenceCaseId != OccurrenceCaseId
            || source.OccurrenceId != OccurrenceId || source.SubjectKind != SubjectKind || source.SubjectId != SubjectId)
            throw new InvalidOperationException("The source must retain the head's exact subject, occurrence and environment.");
    }

    private static void RequireUtc(DateTimeOffset now)
    {
        if (now == default || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Attribution head transitions require an explicit finite UTC time.", nameof(now));
    }

    public override string ToString() => nameof(ModerationCaseAttributionHead);
}
