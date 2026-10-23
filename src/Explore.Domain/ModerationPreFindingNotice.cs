using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// One explicitly approved recipient and immutable evidence revision. Confidential occurrence
/// coordinates and encrypted payloads never form a public notice DTO. Receipt is not a finding.
/// Source pins preserve custody provenance, not native authority to select the current head.
/// </summary>
public sealed class ModerationPreFindingNotice
{
    private ModerationPreFindingNotice() { }
    private byte[] protectedNotice = [];
    private byte[] protectedResponse = [];

    public Guid Id { get; private set; }
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustodyId { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public Guid OccurrenceCaseId { get; private set; }
    public long EvidenceRevision { get; private set; }
    public Guid AttributionSourceId { get; private set; }
    public long AttributionGeneration { get; private set; }
    public ModerationSubjectKind SubjectKind { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid RecipientHumanId { get; private set; }
    public Guid RecipientAssignmentId { get; private set; }
    public long RecipientAssignmentRevision { get; private set; }
    public Guid IssuedByHumanId { get; private set; }
    public Guid ReviewerAssignmentId { get; private set; }
    public long ReviewerAssignmentRevision { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }
    public Guid? ResponseId { get; private set; }
    public DateTimeOffset? PurgedAt { get; private set; }
    public bool Withdrawn { get; private set; }
    public long Revision { get; private set; }
    public ReadOnlyMemory<byte> ProtectedNotice => protectedNotice.ToArray();
    public ReadOnlyMemory<byte> ProtectedResponse => protectedResponse.ToArray();

    public static ModerationPreFindingNotice Issue(Guid id, ModerationCase owner,
        ModerationCaseAttributionSource attribution, ModerationPreFindingCustody custody,
        Guid issuer, Guid reviewerAssignmentId, long reviewerRevision,
        ModerationNoticeContentRepresentative recipient, ReadOnlySpan<byte> ciphertext,
        DateTimeOffset now, DateTimeOffset retainUntil)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(custody);
        owner.ValidatePersistedState();
        attribution.ValidatePersistedState();
        custody.ValidatePersistedState();
        if (!custody.IsCurrentAt(now) || custody.CaseId != owner.Id || custody.CaseRevision != owner.Revision
            || owner.State != ModerationCaseState.Open || owner.AttributionSourceId is null
            || owner.OccurrenceCaseId is null || owner.OccurrenceEvidenceRevision is null
            || owner.Subject is null || owner.Scope.Kind != ModerationScopeKind.Tenant
            || owner.Subject.Origin != (owner.SubjectKind == ModerationSubjectKind.Content ? owner.Scope : owner.Scope.Parent)
            || owner.Scope.Id != custody.TenantId
            || owner.Scope.TrustDomainId != custody.TrustDomainId
            || attribution.Id != custody.AttributionSourceId || attribution.Generation != custody.AttributionGeneration
            || attribution.EvidenceCaseRevision != custody.EvidenceRevision
            || attribution.EvidenceCaseRevision < owner.OccurrenceEvidenceRevision
            || attribution.CaseId != owner.Id || attribution.OccurrenceCaseId != owner.OccurrenceCaseId
            || attribution.OccurrenceId != owner.OccurrenceId || attribution.SubjectKind != owner.SubjectKind
            || attribution.SubjectId != owner.SubjectId || attribution.TenantId != custody.TenantId
            || attribution.TrustDomainId != custody.TrustDomainId || attribution.HostBindingId != custody.HostBindingId
            || !attribution.IsCurrentAt(now) || now < owner.CreatedAt
            || retainUntil > custody.ValidUntil || retainUntil > attribution.ValidUntil
            || retainUntil - now > custody.Terms.MaximumRetention
            || recipient.HumanId == issuer
            || owner.SubjectKind == ModerationSubjectKind.Human && recipient.HumanId != owner.SubjectId)
            throw new ArgumentException("Notice requires exact attributed evidence, independently accepted custody and an explicit recipient.");
        var row = new ModerationPreFindingNotice
        {
            Id = id,
            HostBindingId = custody.HostBindingId,
            TrustDomainId = custody.TrustDomainId,
            TenantId = custody.TenantId,
            CustodyId = custody.Id,
            CaseId = owner.Id,
            CaseRevision = owner.Revision,
            OccurrenceCaseId = owner.OccurrenceCaseId.Value,
            EvidenceRevision = attribution.EvidenceCaseRevision,
            AttributionSourceId = attribution.Id,
            AttributionGeneration = attribution.Generation,
            SubjectKind = owner.Subject.Kind,
            SubjectId = owner.Subject.Id,
            RecipientHumanId = recipient.HumanId,
            RecipientAssignmentId = recipient.AssignmentId,
            RecipientAssignmentRevision = recipient.AssignmentRevision,
            IssuedByHumanId = issuer,
            ReviewerAssignmentId = reviewerAssignmentId,
            ReviewerAssignmentRevision = reviewerRevision,
            IssuedAt = now,
            RetainUntil = retainUntil,
            protectedNotice = ciphertext.ToArray(),
            Revision = 1
        };
        row.ValidatePersistedState();
        return row;
    }

    public void Acknowledge(Guid actor, long expectedRevision, DateTimeOffset now)
    {
        RequireRecipient(actor, expectedRevision, now);
        if (AcknowledgedAt is not null)
            throw new InvalidOperationException("Acknowledgement cannot renew the original opportunity.");
        AcknowledgedAt = now;
        Revision = checked(Revision + 1);
    }

    public void Respond(Guid responseId, Guid actor, long expectedRevision,
        ReadOnlySpan<byte> ciphertext, DateTimeOffset now)
    {
        RequireRecipient(actor, expectedRevision, now);
        ModerationHostBinding.RequireUuidV7(responseId, nameof(responseId));
        if (ResponseId is not null || ciphertext.Length is < 1 or > 131072)
            throw new InvalidOperationException("The response must be finite and cannot replace a retained response.");
        protectedResponse = ciphertext.ToArray();
        ResponseId = responseId;
        RespondedAt = now;
        AcknowledgedAt ??= now;
        Revision = checked(Revision + 1);
    }

    public bool HasResponseAt(DateTimeOffset now) =>
        PurgedAt is null && ResponseId is not null && RespondedAt <= now
        && now >= IssuedAt && now < RetainUntil && protectedResponse.Length != 0;

    /// <summary>Deletion ends disclosure/qualification; retained coordinates are not substantive evidence.</summary>
    public void Purge(long expectedRevision, DateTimeOffset now, bool withdrawn)
    {
        ValidatePersistedState();
        if (expectedRevision != Revision || PurgedAt is not null || now < (RespondedAt ?? AcknowledgedAt ?? IssuedAt)
            || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue
            || !withdrawn && now < RetainUntil)
            throw new InvalidOperationException("Purge requires expiry or explicit withdrawal, never a premature success marker.");
        Array.Clear(protectedNotice);
        Array.Clear(protectedResponse);
        protectedNotice = [];
        protectedResponse = [];
        PurgedAt = now;
        Withdrawn = withdrawn;
        Revision = checked(Revision + 1);
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId, CustodyId, CaseId,
            OccurrenceCaseId, AttributionSourceId, SubjectId, RecipientHumanId, IssuedByHumanId, ReviewerAssignmentId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(IssuedAt, RetainUntil);
        var interactionRevision = PurgedAt is null ? Revision : Revision - 1;
        if (CaseRevision < 1 || AttributionGeneration < 1 || EvidenceRevision < 1 || CaseId == OccurrenceCaseId
            || ReviewerAssignmentRevision < 1 || RecipientHumanId == IssuedByHumanId
            || SubjectKind is not (ModerationSubjectKind.Human or ModerationSubjectKind.Content)
            || SubjectKind == ModerationSubjectKind.Human && (RecipientHumanId != SubjectId
                || RecipientAssignmentId != Guid.Empty || RecipientAssignmentRevision != 0)
            || SubjectKind == ModerationSubjectKind.Content && (RecipientAssignmentId == Guid.Empty
                || RecipientAssignmentRevision < 1)
            || AcknowledgedAt < IssuedAt || AcknowledgedAt >= RetainUntil
            || AcknowledgedAt is { } acknowledged && acknowledged.Offset != TimeSpan.Zero
            || (ResponseId is null) != (RespondedAt is null) || ResponseId == Guid.Empty
            || RespondedAt < AcknowledgedAt || RespondedAt >= RetainUntil
            || RespondedAt is { } responded && responded.Offset != TimeSpan.Zero
            || RespondedAt is not null && AcknowledgedAt is null
            || (ResponseId is null
                ? interactionRevision != (AcknowledgedAt is null ? 1 : 2)
                : interactionRevision != 3 && !(interactionRevision == 2 && AcknowledgedAt == RespondedAt))
            || PurgedAt < (RespondedAt ?? AcknowledgedAt ?? IssuedAt)
            || PurgedAt is { } purged
                && (purged.Offset != TimeSpan.Zero || purged == DateTimeOffset.MaxValue
                    || !Withdrawn && purged < RetainUntil)
            || PurgedAt is null && (Withdrawn || protectedNotice.Length is < 1 or > 131072
                || ResponseId is not null && protectedResponse.Length is < 1 or > 131072
                || ResponseId is null && protectedResponse.Length != 0)
            || PurgedAt is not null && (protectedNotice.Length != 0 || protectedResponse.Length != 0))
            throw new ArgumentException("Pre-finding notice custody or response provenance is inconsistent.");
        if (SubjectKind == ModerationSubjectKind.Content)
            ModerationHostBinding.RequireUuidV7(RecipientAssignmentId, nameof(RecipientAssignmentId));
        if (ResponseId is { } responseId)
            ModerationHostBinding.RequireUuidV7(responseId, nameof(ResponseId));
    }

    private void RequireRecipient(Guid actor, long expectedRevision, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (actor != RecipientHumanId || expectedRevision != Revision || PurgedAt is not null
            || now.Offset != TimeSpan.Zero
            || now < (AcknowledgedAt ?? IssuedAt) || now >= RetainUntil)
            throw new InvalidOperationException("Only the original currently qualified recipient can use finite notice custody.");
    }

    public override string ToString() => nameof(ModerationPreFindingNotice);
}
