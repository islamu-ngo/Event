using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Ai;

public class AiToolExecution : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProposedActionId { get; set; }
    public AiProposedAction? ProposedAction { get; set; }
    public required string ToolName { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public Guid ConversationId { get; private set; }
    public Guid PrincipalUserId { get; private set; }
    public Guid ActingActorId { get; private set; }
    public DateTime ActionCreatedAt { get; private set; }
    public DateTime ActionConfirmedAt { get; private set; }
    public Guid? ActionMessageId { get; private set; }
    public string PayloadDigest { get; private set; } = string.Empty;
    public Guid? ReservedEventId { get; private set; }
    public Guid? PrimaryEventId { get; private set; }
    public DateTime? PrimaryEffectLinkedAt { get; private set; }
    public Guid ConcurrencyStamp { get; private set; } = Guid.CreateVersion7();

    // Retain only exact source identity and custody metadata, never another copy of attachment bytes.
    public Guid? ImageSourceMessageId { get; private set; }
    public string? ImageSourceDigest { get; private set; }
    public string? ImageUploadOperationKey { get; private set; }
    public Guid? ImageUploadSessionId { get; private set; }
    public Guid? ImageStorageObjectId { get; private set; }

    [NotMapped]
    public bool HasDurablePrimaryReservation => ReservedEventId is { } eventId && eventId != Guid.Empty;

    public static string DigestPayload(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public static AiToolExecution ReserveEventDraft(
        Guid executionId, Guid eventId, AiProposedAction action,
        Guid principalUserId, Guid actingActorId,
        Guid? imageSourceMessageId, string? imageSourceDigest)
    {
        if (executionId == Guid.Empty || eventId == Guid.Empty || principalUserId == Guid.Empty
            || actingActorId == Guid.Empty || action.Id == Guid.Empty || action.TenantId == Guid.Empty
            || action.ConversationId == Guid.Empty || action.Kind != AiProposedActionKind.CreateEventDraft
            || action.Status != AiProposedActionStatus.Confirmed || action.ConfirmedBy != principalUserId
            || action.ConfirmedAt is null || action.ActingActorId != actingActorId
            || imageSourceMessageId.HasValue != (imageSourceDigest is not null))
            throw new InvalidOperationException("An event draft reservation requires exact confirmed action evidence.");
        return new AiToolExecution
        {
            Id = executionId,
            TenantId = action.TenantId,
            ProposedActionId = action.Id,
            ToolName = nameof(AiProposedActionKind.CreateEventDraft),
            StartedAt = action.ConfirmedAt.Value,
            ConversationId = action.ConversationId,
            PrincipalUserId = principalUserId,
            ActingActorId = actingActorId,
            ActionCreatedAt = action.CreatedAt,
            ActionConfirmedAt = action.ConfirmedAt.Value,
            ActionMessageId = action.MessageId,
            PayloadDigest = DigestPayload(action.PayloadJson),
            ReservedEventId = eventId,
            ImageSourceMessageId = imageSourceMessageId,
            ImageSourceDigest = imageSourceDigest,
            ImageUploadOperationKey = imageSourceMessageId.HasValue ? $"ai-execution:{executionId:N}:featured-image" : null
        };
    }

    public void LinkPrimaryEvent(Guid eventId, DateTime utcNow)
    {
        if (!HasDurablePrimaryReservation || eventId != ReservedEventId
            || PrimaryEventId is { } linked && linked != eventId || CompletedAt.HasValue)
            throw new InvalidOperationException("Only the reserved event can become this execution's primary effect.");
        if (PrimaryEventId.HasValue) return;
        if (!AiProposedAction.IsWithinLifetime(ActionCreatedAt, utcNow))
            throw new InvalidOperationException("An expired AI action cannot link a new primary effect.");
        PrimaryEventId = eventId;
        PrimaryEffectLinkedAt = utcNow;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    /// <summary>Records custody only after primary creation; callers persist it under the exact execution fence.</summary>
    public void BindImageUploadSession(Guid sourceMessageId, string sourceDigest, Guid uploadSessionId)
    {
        if (!PrimaryEventId.HasValue || CompletedAt.HasValue || uploadSessionId == Guid.Empty
            || sourceMessageId != ImageSourceMessageId
            || !string.Equals(sourceDigest, ImageSourceDigest, StringComparison.Ordinal)
            || ImageUploadSessionId is { } existing && existing != uploadSessionId)
            throw new InvalidOperationException("Image custody requires the original source and one reserved upload session.");
        if (ImageUploadSessionId.HasValue) return;
        ImageUploadSessionId = uploadSessionId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void BindImageStorageObject(Guid uploadSessionId, Guid storageObjectId)
    {
        if (!PrimaryEventId.HasValue || CompletedAt.HasValue || storageObjectId == Guid.Empty
            || ImageUploadSessionId != uploadSessionId
            || ImageStorageObjectId is { } existing && existing != storageObjectId)
            throw new InvalidOperationException("Image custody cannot replace its original upload or storage object.");
        if (ImageStorageObjectId.HasValue) return;
        ImageStorageObjectId = storageObjectId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void MarkSucceeded(DateTime utcNow)
    {
        if (HasDurablePrimaryReservation && (!PrimaryEventId.HasValue || CompletedAt.HasValue && !Succeeded))
            throw new InvalidOperationException("A durable execution succeeds only after its primary effect is linked.");
        if (CompletedAt.HasValue && Succeeded) return;
        Succeeded = true;
        CompletedAt = utcNow;
        FailureCode = null;
        FailureMessage = null;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void MarkFailed(string failureCode, string? failureMessage, DateTime utcNow)
    {
        if (HasDurablePrimaryReservation && (PrimaryEventId.HasValue || CompletedAt.HasValue))
            throw new InvalidOperationException("A committed primary effect or settled execution cannot be failed or reopened.");
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("An AI tool execution failure code is required.", nameof(failureCode));
        }

        Succeeded = false;
        FailureCode = failureCode.Trim();
        FailureMessage = string.IsNullOrWhiteSpace(failureMessage) ? null : failureMessage.Trim();
        CompletedAt = utcNow;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}
