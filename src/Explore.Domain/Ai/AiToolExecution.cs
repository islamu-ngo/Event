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

    public void MarkSucceeded(DateTime utcNow)
    {
        Succeeded = true;
        CompletedAt = utcNow;
        FailureCode = null;
        FailureMessage = null;
    }

    public void MarkFailed(string failureCode, string? failureMessage, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("An AI tool execution failure code is required.", nameof(failureCode));
        }

        Succeeded = false;
        FailureCode = failureCode.Trim();
        FailureMessage = string.IsNullOrWhiteSpace(failureMessage) ? null : failureMessage.Trim();
        CompletedAt = utcNow;
    }
}
