namespace Explore.Domain;

public enum ModerationRecognitionEscalationStage { OwnerActionRequired = 1, LawfulDeadlineReached = 2 }

/// <summary>
/// A durable operational recipient obligation. Persisting this row is not delivery.
/// Delivery requires the actual native notification graph in the same commit; acknowledgement
/// additionally requires the exact recipient. No state here asserts guilt or an identity match.
/// </summary>
public sealed class ModerationRecognitionEscalation
{
    private ModerationRecognitionEscalation() { }
    public Guid Id { get; private set; }
    public Guid IntentId { get; private set; }
    public Guid TargetId { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid? ActionRecoveryId { get; private set; }
    public long PendingRevision { get; private set; }
    public Guid RecipientHumanId { get; private set; }
    public Guid OtherOwnerHumanId { get; private set; }
    public ModerationRecognitionEscalationStage Stage { get; private set; }
    public DateTimeOffset RequiredAt { get; private set; }
    public DateTimeOffset OriginalDeadline { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }
    public Guid? NotificationId { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public long Revision { get; private set; }

    public static ModerationRecognitionEscalation Require(Guid id, ModerationRecognitionPending pending,
        ModerationRecognitionActionRecovery? action, ModerationRecognitionPrivacySource source,
        Guid recipient, DateTimeOffset now)
    {
        pending.ValidatePersistedState();
        action?.ValidatePersistedState();
        source.ValidatePersistedState();
        if (recipient != source.PrivacyOwnerHumanId && recipient != source.RecoveryOwnerHumanId
            || source.RecordedAt > pending.ReceivedAt
            || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue
            || action is null && (pending.AuthoritySequence is not null || now < pending.EscalateAt)
            || action is not null && (action.OriginalIntentId != pending.IntentId || action.ResolvedAt is not null
                || action.PrivacyOwnerHumanId != pending.PrivacyOwnerHumanId
                || action.RecoveryOwnerHumanId != pending.RecoveryOwnerHumanId || now < action.ReceivedAt))
            throw new InvalidOperationException("Escalation requires the original unresolved request and exact named recipient.");
        var deadline = action?.DispositionBy ?? pending.SettleBy;
        var result = new ModerationRecognitionEscalation
        {
            Id = id,
            IntentId = pending.IntentId,
            TargetId = action?.Id ?? pending.IntentId,
            SourceId = source.Id,
            ActionRecoveryId = action?.Id,
            PendingRevision = pending.Revision,
            RecipientHumanId = recipient,
            OtherOwnerHumanId = recipient == source.PrivacyOwnerHumanId
                ? source.RecoveryOwnerHumanId : source.PrivacyOwnerHumanId,
            RequiredAt = now,
            OriginalDeadline = deadline,
            RetainUntil = (action?.ReceivedAt ?? pending.ReceivedAt).AddTicks(source.Retention.Single(item =>
                item.RecordClass == ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt).MaximumRetentionTicks),
            Stage = now >= deadline ? ModerationRecognitionEscalationStage.LawfulDeadlineReached
                : ModerationRecognitionEscalationStage.OwnerActionRequired,
            Revision = 1
        };
        result.ValidatePersistedState();
        return result;
    }

    public void Deliver(Guid notificationId, DateTimeOffset now)
    {
        ValidatePersistedState();
        ModerationHostBinding.RequireUuidV7(notificationId, nameof(notificationId));
        if (NotificationId is not null || now < RequiredAt || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue)
            throw new InvalidOperationException("A native recipient delivery must be exact, finite and recorded only once.");
        NotificationId = notificationId;
        DeliveredAt = now;
        Revision = 2;
    }

    public void Acknowledge(Guid recipient, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (recipient != RecipientHumanId || DeliveredAt is not { } delivered || now < delivered
            || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue)
            throw new InvalidOperationException("Only the exact recipient may acknowledge a delivered operational escalation.");
        if (AcknowledgedAt is not null) return;
        AcknowledgedAt = now;
        Revision = 3;
    }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, IntentId, TargetId, SourceId }) ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (RecipientHumanId == Guid.Empty || OtherOwnerHumanId == Guid.Empty || RecipientHumanId == OtherOwnerHumanId
            || PendingRevision < 1 || !Enum.IsDefined(Stage) || (ActionRecoveryId ?? IntentId) != TargetId
            || RequiredAt.Offset != TimeSpan.Zero || RequiredAt == DateTimeOffset.MinValue
            || OriginalDeadline.Offset != TimeSpan.Zero || OriginalDeadline == DateTimeOffset.MaxValue
            || RetainUntil <= RequiredAt || RetainUntil.Offset != TimeSpan.Zero || RetainUntil == DateTimeOffset.MaxValue
            || !(Revision == 1 && NotificationId is null && DeliveredAt is null && AcknowledgedAt is null
                || Revision == 2 && NotificationId is not null && DeliveredAt >= RequiredAt && AcknowledgedAt is null
                || Revision == 3 && NotificationId is not null && DeliveredAt >= RequiredAt && AcknowledgedAt >= DeliveredAt))
            throw new InvalidOperationException("Operational escalation must distinguish pending delivery from recipient acknowledgement.");
    }

    public override string ToString() => nameof(ModerationRecognitionEscalation);
}
