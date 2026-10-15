namespace Explore.Domain;

/// <summary>
/// Insert-once evidence that the subject or its original qualified Content representative acknowledged one original safe
/// explanation. This receipt grants no account, case or enforcement authority and cannot renew
/// notification time. The immutable privacy ceiling is never extended by acknowledgement.
/// </summary>
public sealed class ModerationNoticeNotificationReceipt
{
    private ModerationNoticeNotificationReceipt() { }

    public Guid Id { get; private set; }
    public Guid NoticeId { get; private set; }
    public Guid EffectId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SubjectId { get; private set; }
    public int SubjectKind { get; private set; }
    public Guid AcknowledgedByHumanId { get; private set; }
    public string SnapshotHash { get; private set; } = string.Empty;
    public int ContestationCalendarMonths { get; private set; }
    public DateTimeOffset NotifiedAt { get; private set; }
    public DateTimeOffset RemedyUntil { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }

    public static ModerationNoticeNotificationReceipt Acknowledge(
        Guid operationId, ModerationNotice notice, ModerationNoticeSource source,
        Guid tenantId, Guid canonicalRecipientId, string expectedSnapshotHash,
        int contestationCalendarMonths, DateTimeOffset retainUntil, DateTimeOffset notifiedAt)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(source);
        ModerationHostBinding.RequireUuidV7(operationId, nameof(operationId));
        if (notice.RecipientSubjectKind != 1 || notice.RecipientSubjectId != canonicalRecipientId
            || !(notice.Audience == ModerationNoticeAudience.Subject && source.SubjectKind == 1
                    && source.SubjectId == canonicalRecipientId
                || notice.Audience == ModerationNoticeAudience.ContentRepresentative && source.SubjectKind == 4
                    && source.HasContentRepresentative(canonicalRecipientId))
            || notice.EffectId != source.EffectId || notice.TrustDomainId != source.TrustDomainId
            || notice.TenantId != source.TenantId || source.TenantId is { } sourceTenant && sourceTenant != tenantId
            || expectedSnapshotHash != source.SnapshotHash || notifiedAt < source.CapturedAt
            || source.RetainUntil != retainUntil
            || !ModerationNoticeRetention.CanNotifyAt(notifiedAt, contestationCalendarMonths, retainUntil))
            throw new ArgumentException("Acknowledgement requires the original subject, proof and sufficient protected calendar time.");
        var receipt = new ModerationNoticeNotificationReceipt
        {
            Id = operationId, NoticeId = notice.Id, EffectId = source.EffectId,
            TrustDomainId = source.TrustDomainId, TenantId = tenantId, SubjectId = source.SubjectId,
            SubjectKind = source.SubjectKind, AcknowledgedByHumanId = canonicalRecipientId,
            SnapshotHash = expectedSnapshotHash, ContestationCalendarMonths = contestationCalendarMonths,
            NotifiedAt = notifiedAt, RemedyUntil = notifiedAt.AddMonths(contestationCalendarMonths),
            RetainUntil = retainUntil
        };
        receipt.ValidatePersistedState();
        return receipt;
    }

    public bool Matches(ModerationNoticeSource source, Guid tenantId, int calendarMonths, DateTimeOffset retainUntil)
    {
        ValidatePersistedState();
        return EffectId == source.EffectId && TrustDomainId == source.TrustDomainId
            && TenantId == tenantId && (source.TenantId is null || source.TenantId == tenantId)
            && SubjectId == source.SubjectId && SubjectKind == source.SubjectKind
            && (SubjectKind == 1 ? AcknowledgedByHumanId == SubjectId
                : SubjectKind == 4 && source.HasContentRepresentative(AcknowledgedByHumanId))
            && SnapshotHash == source.SnapshotHash && NotifiedAt >= source.CapturedAt
            && ContestationCalendarMonths == calendarMonths && RetainUntil == retainUntil
            && source.RetainUntil == retainUntil;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, NoticeId, EffectId, TrustDomainId, TenantId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (SubjectId == Guid.Empty || AcknowledgedByHumanId == Guid.Empty || SubjectKind is not (1 or 4)
            || SubjectKind == 1 && AcknowledgedByHumanId != SubjectId
            || SnapshotHash.Length != 64 || !SnapshotHash.All(char.IsAsciiHexDigitLower)
            || !ModerationNoticeRetention.CanNotifyAt(NotifiedAt, ContestationCalendarMonths, RetainUntil)
            || RemedyUntil != NotifiedAt.AddMonths(ContestationCalendarMonths))
            throw new ArgumentException("A notification receipt requires exact original proof and a finite protected calendar floor.");
    }

    public override string ToString() => nameof(ModerationNoticeNotificationReceipt);
}
