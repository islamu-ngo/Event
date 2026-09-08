using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Explore.Application.Hateoas;

namespace Explore.Application.DTOs.Ai;

public sealed record CreateAiConversationRequestDto
{
    public string? Title { get; init; }
    public Guid? ActorId { get; init; }
}

public sealed record SendAiMessageRequestDto
{
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<AiMessageImageInputDto> Images { get; init; } = [];
    public IReadOnlyList<AiSelectedReferenceDto> References { get; init; } = [];
    public string IdempotencyKey { get; init; } = string.Empty;
    public Guid? ActorId { get; init; }
    public string? ModelId { get; init; }
    public string Mode { get; init; } = AiAssistantInteractionModes.Build;
}

public sealed record AiMessageImageInputDto
{
    public string MediaType { get; init; } = string.Empty;
    public string Data { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public long? SizeBytes { get; init; }
}

public sealed record AiMessageImageDto
{
    public string MediaType { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public long? SizeBytes { get; init; }
}

public record AiConversationSummaryDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public Guid? ActorId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Provider { get; init; }
    public string? ModelId { get; init; }
    public string? BlockedReason { get; init; }
    public long LastMessageSequence { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record AiConversationDto : AiConversationSummaryDto
{
    public IReadOnlyList<AiMessageDto> Messages { get; init; } = [];
    public IReadOnlyList<AiRunDto> Runs { get; init; } = [];
    public IReadOnlyList<AiConversationReferenceDto> References { get; init; } = [];
    public IReadOnlyList<AiProposedActionDto> ProposedActions { get; init; } = [];
}

public sealed record AiMessageDto
{
    public Guid Id { get; init; }
    public long Sequence { get; init; }
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public IReadOnlyList<AiMessageImageDto> Images { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}

public sealed record AiRunDto
{
    public Guid Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public DateTime QueuedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed record AiConversationReferenceDto
{
    public Guid Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public Guid ReferenceId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record AiProposedActionDto
{
    private IReadOnlyDictionary<string, HalLink>? _links;

    public Guid Id { get; init; }
    public Guid? MessageId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public Guid? ConfirmedBy { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public Guid? RejectedBy { get; init; }
    public DateTime? RejectedAt { get; init; }
    public Guid? ResultResourceId { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("_links")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, HalLink>? Links
    {
        get => _links;
        set => _links = value is null
            ? null
            : new ReadOnlyDictionary<string, HalLink>(new Dictionary<string, HalLink>(value, StringComparer.Ordinal));
    }
}
