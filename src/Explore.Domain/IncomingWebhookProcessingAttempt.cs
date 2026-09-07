using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class IncomingWebhookProcessingAttempt : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid IncomingWebhookMessageId { get; private set; }
    public IncomingWebhookMessage? IncomingWebhookMessage { get; private set; }
    public int ProcessingGeneration { get; private set; }
    public long ProcessingFence { get; private set; }
    public int AttemptNumber { get; private set; }
    public int OutcomeId { get; private set; }
    public IncomingWebhookProcessingAttemptOutcomeLookup OutcomeLookup { get; private set; } = null!;
    [NotMapped]
    public IncomingWebhookProcessingAttemptOutcome Outcome
    {
        get => (IncomingWebhookProcessingAttemptOutcome)OutcomeId;
        private set => OutcomeId = (int)value;
    }
    public DateTime StartedAt { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public string? FailureCategory { get; private set; }
    public string? SafeDetail { get; private set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    internal static IncomingWebhookProcessingAttempt Create(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        int processingGeneration,
        long processingFence,
        int attemptNumber,
        IncomingWebhookProcessingAttemptOutcome outcome,
        DateTime startedAt,
        DateTime recordedAt,
        string? failureCategory,
        string? safeDetail)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(processingGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(processingFence);
        ArgumentOutOfRangeException.ThrowIfNegative(attemptNumber);
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));

        return new IncomingWebhookProcessingAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            IncomingWebhookMessageId = incomingWebhookMessageId,
            ProcessingGeneration = processingGeneration,
            ProcessingFence = processingFence,
            AttemptNumber = attemptNumber,
            Outcome = outcome,
            StartedAt = startedAt,
            RecordedAt = recordedAt,
            FailureCategory = IncomingWebhookMessage.NormalizeOptional(
                failureCategory,
                IncomingWebhookMessage.MaxFailureCodeLength,
                nameof(failureCategory)),
            SafeDetail = IncomingWebhookMessage.BoundSafeDetail(safeDetail),
            CreatedAt = recordedAt
        };
    }
}
