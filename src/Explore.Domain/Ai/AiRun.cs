using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Ai;

public class AiRun : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }
    public int StatusId { get; set; } = (int)AiRunStatus.Queued;
    public AiRunStatusLookup? StatusLookup { get; set; }
    [NotMapped]
    public AiRunStatus Status
    {
        get => (AiRunStatus)StatusId;
        set => StatusId = (int)value;
    }
    public required string Provider { get; set; }
    public required string ModelId { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public void Start(DateTime utcNow)
    {
        if (Status != AiRunStatus.Queued)
        {
            throw new InvalidOperationException("Only queued AI runs can start.");
        }

        Status = AiRunStatus.InProgress;
        StartedAt = utcNow;
    }

    public void Succeed(DateTime utcNow)
    {
        if (Status != AiRunStatus.InProgress)
        {
            throw new InvalidOperationException("Only in-progress AI runs can succeed.");
        }

        Status = AiRunStatus.Succeeded;
        CompletedAt = utcNow;
    }

    public void Fail(string failureCode, string? failureMessage, DateTime utcNow)
    {
        if (Status is AiRunStatus.Succeeded or AiRunStatus.Cancelled)
        {
            throw new InvalidOperationException("Completed or cancelled AI runs cannot fail.");
        }

        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("An AI run failure code is required.", nameof(failureCode));
        }

        Status = AiRunStatus.Failed;
        FailureCode = failureCode.Trim();
        FailureMessage = string.IsNullOrWhiteSpace(failureMessage) ? null : failureMessage.Trim();
        CompletedAt = utcNow;
    }

    public void Cancel(DateTime utcNow)
    {
        if (Status is AiRunStatus.Succeeded or AiRunStatus.Failed)
        {
            throw new InvalidOperationException("Completed AI runs cannot be cancelled.");
        }

        Status = AiRunStatus.Cancelled;
        CompletedAt = utcNow;
    }
}
