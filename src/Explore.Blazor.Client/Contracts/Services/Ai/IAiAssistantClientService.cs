using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Ai;

namespace Explore.Blazor.Client.Contracts.Services.Ai;

public interface IAiAssistantClientService
{
    Task<HalResourceOfAiAssistantBootstrapDto?> GetBootstrapAsync(CancellationToken cancellationToken = default);

    Task<HalCollectionResourceOfAiConversationSummaryDto?> GetConversationCollectionAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HalResourceOfAiConversationSummaryDto>> GetConversationsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<HalResourceOfAiConversationDto?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<AiAssistantCommandResult> CreateConversationAsync(
        CreateAiConversationRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AiAssistantCommandResult> SendMessageAsync(
        Guid conversationId,
        string content,
        string? modelId = null,
        string? idempotencyKey = null,
        string? mode = null,
        Guid? actorId = null,
        IReadOnlyList<AiMessageImageInputDto>? images = null,
        IReadOnlyList<AiSelectedReferenceDto>? references = null,
        CancellationToken cancellationToken = default);

    Task<AiRunStatusResult> GetRunStatusAsync(
        Guid conversationId,
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<AiAssistantCommandResult> CancelRunAsync(
        Guid conversationId,
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HalResourceOfAiReferenceSearchResultDto>> SearchReferencesAsync(
        string searchTerm,
        int limit = 10,
        CancellationToken cancellationToken = default);

    Task<AiAssistantCommandResult> ConfirmProposedActionAsync(
        Guid conversationId,
        Guid proposedActionId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<AiAssistantCommandResult> RejectProposedActionAsync(
        Guid conversationId,
        Guid proposedActionId,
        CancellationToken cancellationToken = default);
}
