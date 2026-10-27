using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable renewal operation identity and expected pre-advance tuple for one existing
/// attributed case. It records intent and result linkage, not authority, payload or a
/// source/head transition. The native writer retains final transaction custody.
/// </summary>
public sealed class ModerationCaseAttributionRenewalReceipt
{
    private ModerationCaseAttributionRenewalReceipt() { }

    public Guid Id { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid HeadId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public long ExpectedCaseRevision { get; private set; }
    public Guid ExpectedSourceId { get; private set; }
    public long ExpectedGeneration { get; private set; }
    public long ExpectedHeadRevision { get; private set; }
    public DateTimeOffset QualifiedAt { get; private set; }

    /// <summary>
    /// Captures the owner revision and current head before advancement, binding the operation
    /// to a freshly qualified consecutive source without changing historical opening provenance.
    /// </summary>
    public static ModerationCaseAttributionRenewalReceipt Record(
        ModerationCase owner, ModerationCaseAttributionHead priorHead,
        ModerationCaseAttributionSource nextSource, ModerationHostBinding host, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(priorHead);
        ArgumentNullException.ThrowIfNull(nextSource);
        ArgumentNullException.ThrowIfNull(host);
        if (now == default || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue)
            throw new ArgumentException("Renewal receipts require an explicit finite UTC time.", nameof(now));
        owner.ValidatePersistedState();
        priorHead.ValidatePersistedState();
        nextSource.ValidatePersistedState();

        if (owner.Id != priorHead.CaseId || owner.State != ModerationCaseState.Open
            || owner.Scope.Kind != ModerationScopeKind.Tenant || owner.Scope.Id != priorHead.TenantId
            || owner.Scope.TrustDomainId != priorHead.TrustDomainId
            || owner.ScopeSolutionId != priorHead.SolutionId || owner.ScopeInstanceId != priorHead.InstanceId
            || owner.OccurrenceCaseId != priorHead.OccurrenceCaseId || owner.OccurrenceId != priorHead.OccurrenceId
            || owner.OccurrenceEvidenceRevision != priorHead.OccurrenceEvidenceRevision
            || owner.AttributionSourceId != priorHead.OpeningSourceId || owner.Subject is not { } subject
            || subject.Kind != priorHead.SubjectKind || subject.Id != priorHead.SubjectId
            || subject.Origin != (subject.Kind == ModerationSubjectKind.Content ? owner.Scope : owner.Scope.Parent)
            || host.Id != priorHead.HostBindingId || host.TrustDomainId != priorHead.TrustDomainId
            || host.SolutionId != priorHead.SolutionId || host.InstanceId != priorHead.InstanceId
            || now < owner.CreatedAt)
            throw new InvalidOperationException("Renewal must retain the actual open owner and its original opening and host ancestry.");

        if (nextSource.CaseId != priorHead.CaseId || nextSource.TenantId != priorHead.TenantId
            || nextSource.TrustDomainId != priorHead.TrustDomainId || nextSource.HostBindingId != priorHead.HostBindingId
            || nextSource.OccurrenceCaseId != priorHead.OccurrenceCaseId || nextSource.OccurrenceId != priorHead.OccurrenceId
            || nextSource.SubjectKind != priorHead.SubjectKind || nextSource.SubjectId != priorHead.SubjectId
            || nextSource.Id == priorHead.SourceId || nextSource.Id == priorHead.OpeningSourceId
            || nextSource.Generation != checked(priorHead.Generation + 1)
            || nextSource.QualifiedAt != now || !nextSource.IsCurrentAt(now))
            throw new InvalidOperationException("Renewal requires a fresh consecutive source for the exact subject, occurrence and environment.");

        var receipt = new ModerationCaseAttributionRenewalReceipt
        {
            Id = nextSource.Id,
            CaseId = owner.Id,
            HeadId = priorHead.Id,
            TenantId = priorHead.TenantId,
            TrustDomainId = priorHead.TrustDomainId,
            ExpectedCaseRevision = owner.Revision,
            ExpectedSourceId = priorHead.SourceId,
            ExpectedGeneration = priorHead.Generation,
            ExpectedHeadRevision = priorHead.Revision,
            QualifiedAt = now
        };
        receipt.ValidatePersistedState();
        return receipt;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, CaseId, HeadId, TenantId, TrustDomainId, ExpectedSourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(id));
        if (ExpectedCaseRevision < 1 || ExpectedGeneration < 1 || ExpectedHeadRevision < 1
            || ExpectedHeadRevision != ExpectedGeneration || Id == ExpectedSourceId
            || QualifiedAt == default || QualifiedAt.Offset != TimeSpan.Zero || QualifiedAt == DateTimeOffset.MaxValue)
            throw new ArgumentException("The renewal receipt requires a distinct source, positive expected tuple and UTC qualification time.");
    }

    public override string ToString() => nameof(ModerationCaseAttributionRenewalReceipt);
}
