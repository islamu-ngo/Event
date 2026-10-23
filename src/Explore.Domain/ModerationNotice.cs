using System.Text.Json;

namespace Explore.Domain;

public enum ModerationNoticeAudience { Subject = 1, Reviewer = 2, ContentRepresentative = 3 }

public enum ModerationNoticeDeliveryDisposition
{
    NoTenantProjection = 1,
    NoHumanProjection = 2,
    RecipientUnavailable = 3,
    NativeRoutingUnavailable = 4,
    NativePortalEmailUnavailable = 5,
    NativePortalEmailQueued = 6
}

/// <summary>
/// Durable safe portal occurrence independent of SMTP and other consequence completion.
/// Reading requires current subject/reviewer authority; historical recipient identity is not a grant.
/// </summary>
public sealed class ModerationNotice
{
    private ModerationNotice() { }

    public Guid Id { get; private set; }
    public Guid EffectId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid? TenantId { get; private set; }
    public ModerationNoticeAudience Audience { get; private set; }
    public int RecipientSubjectKind { get; private set; }
    public Guid RecipientSubjectId { get; private set; }
    public Guid? NativeIntentId { get; private set; }
    public ModerationNoticeDeliveryDisposition DeliveryDisposition { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ModerationNotice Create(
        Guid id, ModerationNoticeSource source, ModerationNoticeAudience audience,
        int recipientSubjectKind, Guid recipientSubjectId, Guid? nativeIntentId,
        ModerationNoticeDeliveryDisposition deliveryDisposition)
    {
        ArgumentNullException.ThrowIfNull(source);
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        if (source.RetainUntil is null || !Enum.IsDefined(audience) || !Enum.IsDefined(deliveryDisposition)
            || recipientSubjectKind is < 1 or > 4 || recipientSubjectId == Guid.Empty
            || nativeIntentId == Guid.Empty
            || audience == ModerationNoticeAudience.Subject
                && (recipientSubjectKind != source.SubjectKind || recipientSubjectId != source.SubjectId)
            || audience == ModerationNoticeAudience.Reviewer
                && (recipientSubjectKind != 1 || recipientSubjectId == source.SubjectId)
            || audience == ModerationNoticeAudience.ContentRepresentative
                && (recipientSubjectKind != 1 || !source.HasContentRepresentative(recipientSubjectId))
            || (nativeIntentId is not null) !=
                (deliveryDisposition is ModerationNoticeDeliveryDisposition.NativePortalEmailUnavailable
                    or ModerationNoticeDeliveryDisposition.NativePortalEmailQueued)
            || nativeIntentId is not null && (source.TenantId is null || recipientSubjectKind != 1))
            throw new ArgumentException("Notice recipient and native delivery must retain their actual authority.");
        return new()
        {
            Id = id,
            EffectId = source.EffectId,
            TrustDomainId = source.TrustDomainId,
            TenantId = source.TenantId,
            Audience = audience,
            RecipientSubjectKind = recipientSubjectKind,
            RecipientSubjectId = recipientSubjectId,
            NativeIntentId = nativeIntentId,
            DeliveryDisposition = deliveryDisposition,
            CreatedAt = source.CapturedAt
        };
    }

    /// <summary>Retains an unresolved original recipient, never a readable or delivered correction.</summary>
    public static ModerationNotice CreatePendingCorrection(
        Guid id, ModerationNoticeSource source, ModerationNoticeAudience audience,
        int recipientSubjectKind, Guid recipientSubjectId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        if (source.RetainUntil is null || !Enum.IsDefined(audience)
            || recipientSubjectKind is < 1 or > 4 || recipientSubjectId == Guid.Empty
            || audience == ModerationNoticeAudience.Subject
                && (recipientSubjectKind != source.SubjectKind || recipientSubjectId != source.SubjectId)
            || audience != ModerationNoticeAudience.Subject && recipientSubjectKind != 1
            || audience == ModerationNoticeAudience.Reviewer && recipientSubjectId == source.SubjectId
            || !IsPendingRecipient(source, audience, recipientSubjectKind, recipientSubjectId))
            throw new ArgumentException("Pending correction occurrences require an exact retained recipient obligation.");
        return new()
        {
            Id = id,
            EffectId = source.EffectId,
            TrustDomainId = source.TrustDomainId,
            TenantId = source.TenantId,
            Audience = audience,
            RecipientSubjectKind = recipientSubjectKind,
            RecipientSubjectId = recipientSubjectId,
            DeliveryDisposition = ModerationNoticeDeliveryDisposition.RecipientUnavailable,
            CreatedAt = source.CapturedAt
        };
    }

    private static bool IsPendingRecipient(ModerationNoticeSource source, ModerationNoticeAudience audience,
        int kind, Guid subjectId)
    {
        try
        {
            using var document = JsonDocument.Parse(source.SnapshotJson);
            var capture = document.RootElement;
            var pending = capture.GetProperty("PendingCorrection");
            return capture.GetProperty("Version").GetInt32() == 2
                && capture.GetProperty("WriterGeneration").GetString() == "event.moderation.capture.v2"
                && capture.GetProperty("Notice").ValueKind == JsonValueKind.Null
                && pending.GetProperty("RetainUntil").GetDateTimeOffset() == source.RetainUntil
                && pending.GetProperty("Recipients").EnumerateArray().Count(row =>
                    row.GetProperty("Audience").GetInt32() == (int)audience
                    && row.GetProperty("RecipientSubjectKind").GetInt32() == kind
                    && row.GetProperty("RecipientSubjectId").GetGuid() == subjectId
                    && row.GetProperty("Disposition").GetInt32()
                        == (int)ModerationNoticeDeliveryDisposition.RecipientUnavailable) == 1;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public override string ToString() => nameof(ModerationNotice);
}
