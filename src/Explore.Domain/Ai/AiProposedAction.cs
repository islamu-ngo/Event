using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Ai;

public class AiProposedAction : ITenantEntity
{
    public static bool IsWithinLifetime(DateTime createdAt, DateTime utcNow) =>
        createdAt != default && utcNow >= createdAt && utcNow - createdAt <= TimeSpan.FromMinutes(30);

    /// <summary>
    /// Binds a displayed preview to its exact persisted effect and ownership.
    /// Mutable status is excluded so the same consent can recover its confirmed effect.
    /// This comparison token never grants authorization.
    /// </summary>
    public string ConsentVersionFor(Guid principalUserId)
    {
        string envelope = FormattableString.Invariant(
            $"{Id:N}|{TenantId:N}|{ConversationId:N}|{principalUserId:N}|{ActingActorId:N}|{MessageId:N}|{CreatedAt.Ticks}|{(int)Kind}|{AiToolExecution.DigestPayload(PayloadJson)}");
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(envelope)));
    }

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }
    public Guid? MessageId { get; set; }
    public AiMessage? Message { get; set; }
    public Guid? ActingActorId { get; set; }
    public Actor? ActingActor { get; set; }
    public int KindId { get; set; }
    public AiProposedActionKindLookup? KindLookup { get; set; }
    [NotMapped]
    public AiProposedActionKind Kind
    {
        get => (AiProposedActionKind)KindId;
        set => KindId = (int)value;
    }
    public int StatusId { get; set; } = (int)AiProposedActionStatus.Proposed;
    public AiProposedActionStatusLookup? StatusLookup { get; set; }
    [NotMapped]
    public AiProposedActionStatus Status
    {
        get => (AiProposedActionStatus)StatusId;
        set => StatusId = (int)value;
    }
    public required string PayloadJson { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public Guid? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public Guid? ResultResourceId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public void Confirm(Guid userId, DateTime utcNow)
    {
        if (Status != AiProposedActionStatus.Proposed)
        {
            throw new InvalidOperationException("Only proposed AI actions can be confirmed.");
        }

        Status = AiProposedActionStatus.Confirmed;
        ConfirmedBy = userId;
        ConfirmedAt = utcNow;
    }

    public void Reject(Guid userId, DateTime utcNow)
    {
        if (Status != AiProposedActionStatus.Proposed)
        {
            throw new InvalidOperationException("Only proposed AI actions can be rejected.");
        }

        Status = AiProposedActionStatus.Rejected;
        RejectedBy = userId;
        RejectedAt = utcNow;
    }

    public void MarkExecuted(Guid resultResourceId)
    {
        if (Status != AiProposedActionStatus.Confirmed)
        {
            throw new InvalidOperationException("Only confirmed AI actions can be marked executed.");
        }

        Status = AiProposedActionStatus.Executed;
        ResultResourceId = resultResourceId;
    }

    public void MarkFailed(string failureCode, string? failureMessage)
    {
        if (Status is AiProposedActionStatus.Rejected or AiProposedActionStatus.Executed)
        {
            throw new InvalidOperationException("Rejected or executed AI actions cannot fail.");
        }

        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("An AI action failure code is required.", nameof(failureCode));
        }

        Status = AiProposedActionStatus.Failed;
        FailureCode = failureCode.Trim();
        FailureMessage = string.IsNullOrWhiteSpace(failureMessage) ? null : failureMessage.Trim();
    }
}
