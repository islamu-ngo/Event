using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Ai;

public class AiConversation : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid? ActorId { get; set; }
    public Actor? Actor { get; set; }
    public int StatusId { get; set; } = (int)AiConversationStatus.Active;
    public AiConversationStatusLookup? StatusLookup { get; set; }
    [NotMapped]
    public AiConversationStatus Status
    {
        get => (AiConversationStatus)StatusId;
        set => StatusId = (int)value;
    }
    public string? Title { get; set; }
    public string? Provider { get; set; }
    public string? ModelId { get; set; }
    public string? BlockedReason { get; set; }
    public long LastMessageSequence { get; set; }
    public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
    public ICollection<AiRun> Runs { get; set; } = new List<AiRun>();
    public ICollection<AiConversationReference> References { get; set; } = new List<AiConversationReference>();
    public ICollection<AiProposedAction> ProposedActions { get; set; } = new List<AiProposedAction>();
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public AiMessage AddMessage(
        AiMessageRole role,
        string content,
        Guid? userId,
        DateTime utcNow,
        string? imageAttachmentsJson = null)
    {
        if (Status == AiConversationStatus.Blocked)
        {
            throw new InvalidOperationException("Blocked AI conversations cannot accept new messages.");
        }

        if (Status == AiConversationStatus.Archived)
        {
            throw new InvalidOperationException("Archived AI conversations cannot accept new messages.");
        }

        var normalizedContent = content.Trim();
        var normalizedImageAttachmentsJson = string.IsNullOrWhiteSpace(imageAttachmentsJson)
            ? null
            : imageAttachmentsJson.Trim();

        if (string.IsNullOrWhiteSpace(normalizedContent) && string.IsNullOrWhiteSpace(normalizedImageAttachmentsJson))
        {
            throw new ArgumentException("AI messages require content or image attachments.", nameof(content));
        }

        var message = new AiMessage
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            ConversationId = Id,
            Sequence = ++LastMessageSequence,
            Role = role,
            Content = normalizedContent,
            ImageAttachmentsJson = normalizedImageAttachmentsJson,
            CreatedAt = utcNow,
            CreatedBy = userId
        };

        Messages.Add(message);
        UpdatedAt = utcNow;
        UpdatedBy = userId;
        return message;
    }

    public AiRun QueueRun(string provider, string modelId, DateTime utcNow)
    {
        if (Status == AiConversationStatus.Blocked)
        {
            throw new InvalidOperationException("Blocked AI conversations cannot queue runs.");
        }

        if (Status == AiConversationStatus.Archived)
        {
            throw new InvalidOperationException("Archived AI conversations cannot queue runs.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("An AI provider is required.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("An AI model id is required.", nameof(modelId));
        }

        var run = new AiRun
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            ConversationId = Id,
            Provider = provider.Trim(),
            ModelId = modelId.Trim(),
            QueuedAt = utcNow
        };

        Provider = run.Provider;
        ModelId = run.ModelId;
        Status = AiConversationStatus.Running;
        Runs.Add(run);
        UpdatedAt = utcNow;
        return run;
    }

    public AiConversationReference AddReference(AiReferenceKind kind, Guid referenceId, string displayName, string? summary, Guid? userId, DateTime utcNow)
    {
        if (referenceId == Guid.Empty)
        {
            throw new ArgumentException("AI references require a non-empty reference id.", nameof(referenceId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("AI references require a display name.", nameof(displayName));
        }

        var reference = new AiConversationReference
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            ConversationId = Id,
            Kind = kind,
            ReferenceId = referenceId,
            DisplayName = displayName.Trim(),
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            CreatedAt = utcNow,
            CreatedBy = userId
        };

        References.Add(reference);
        UpdatedAt = utcNow;
        UpdatedBy = userId;
        return reference;
    }

    public AiProposedAction ProposeAction(AiProposedActionKind kind, string payloadJson, Guid? messageId, Guid? userId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("AI proposed actions require a JSON payload.", nameof(payloadJson));
        }

        var action = new AiProposedAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            ConversationId = Id,
            MessageId = messageId,
            ActingActorId = ActorId,
            Kind = kind,
            PayloadJson = payloadJson.Trim(),
            CreatedAt = utcNow,
            CreatedBy = userId
        };

        ProposedActions.Add(action);
        UpdatedAt = utcNow;
        UpdatedBy = userId;
        return action;
    }

    public void CompleteRun(AiRun run, DateTime utcNow)
    {
        if (run.ConversationId != Id)
        {
            throw new InvalidOperationException("AI run does not belong to this conversation.");
        }

        run.Succeed(utcNow);
        Status = AiConversationStatus.Active;
        UpdatedAt = utcNow;
    }

    public void FailRun(AiRun run, string failureCode, string? failureMessage, DateTime utcNow)
    {
        if (run.ConversationId != Id)
        {
            throw new InvalidOperationException("AI run does not belong to this conversation.");
        }

        run.Fail(failureCode, failureMessage, utcNow);
        Status = AiConversationStatus.Blocked;
        BlockedReason = failureCode.Trim();
        UpdatedAt = utcNow;
    }

    public void CancelRun(AiRun run, DateTime utcNow)
    {
        if (run.ConversationId != Id)
        {
            throw new InvalidOperationException("AI run does not belong to this conversation.");
        }

        run.Cancel(utcNow);
        Status = AiConversationStatus.Active;
        BlockedReason = null;
        UpdatedAt = utcNow;
    }

    public void Block(string reason, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A blocked AI conversation requires a reason.", nameof(reason));
        }

        Status = AiConversationStatus.Blocked;
        BlockedReason = reason.Trim();
        UpdatedAt = utcNow;
    }

    public void Activate(DateTime utcNow)
    {
        Status = AiConversationStatus.Active;
        BlockedReason = null;
        UpdatedAt = utcNow;
    }

    public void Archive(Guid? userId, DateTime utcNow)
    {
        Status = AiConversationStatus.Archived;
        UpdatedAt = utcNow;
        UpdatedBy = userId;
    }
}
