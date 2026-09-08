using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class IncomingWebhookRedriveRecord : ITenantEntity, IAuditableEntity
{
    public const int MaxActorIdLength = 200;
    public const int MaxReasonLength = 1000;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid IncomingWebhookMessageId { get; private set; }
    public IncomingWebhookMessage? IncomingWebhookMessage { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTime RequestedAt { get; private set; }
    public int SourceProcessingGeneration { get; private set; }
    public int TargetProcessingGeneration { get; private set; }
    public int ResultId { get; private set; }
    public IncomingWebhookRedriveResultLookup ResultLookup { get; private set; } = null!;
    [NotMapped]
    public IncomingWebhookRedriveResult Result
    {
        get => (IncomingWebhookRedriveResult)ResultId;
        private set => ResultId = (int)value;
    }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    internal static IncomingWebhookRedriveRecord Create(
        Guid tenantId,
        Guid incomingWebhookMessageId,
        string actorId,
        string reason,
        DateTime requestedAt,
        int sourceProcessingGeneration,
        int targetProcessingGeneration,
        IncomingWebhookRedriveResult result)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceProcessingGeneration, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(targetProcessingGeneration, sourceProcessingGeneration);
        if (!Enum.IsDefined(result)) throw new ArgumentOutOfRangeException(nameof(result));

        return new IncomingWebhookRedriveRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            IncomingWebhookMessageId = incomingWebhookMessageId,
            ActorId = IncomingWebhookMessage.NormalizeRequired(actorId, MaxActorIdLength, nameof(actorId)),
            Reason = IncomingWebhookMessage.NormalizeRequired(reason, MaxReasonLength, nameof(reason)),
            RequestedAt = requestedAt,
            SourceProcessingGeneration = sourceProcessingGeneration,
            TargetProcessingGeneration = targetProcessingGeneration,
            Result = result,
            CreatedAt = requestedAt
        };
    }
}
