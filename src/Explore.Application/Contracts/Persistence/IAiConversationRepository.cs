using Explore.Application.Models;
using Explore.Domain.Ai;

namespace Explore.Application.Contracts.Persistence;

public interface IAiConversationRepository : IGenericRepository<AiConversation, Guid>
{
    Task<AiConversation?> GetByIdWithDetailsAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<AiConversation?> GetByIdForUpdateAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<AiConversation?> GetByIdForUpdateAsync(Guid conversationId, Guid? admittedActorId, CancellationToken cancellationToken);
    Task<AiConversation?> GetByIdForPrivacyUpdateAsync(Guid conversationId, CancellationToken cancellationToken);
    /// <summary>Reads only fence existence for the freshly acquired graph's unchanged original owner and transaction.</summary>
    Task<bool> IsPrivacyFencedAsync(AiConversation conversation, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiConversation>> ListRecentForUserAsync(Guid userId, int limit, CancellationToken cancellationToken);
    /// <summary>Hard-deletes every conversation row and dependent graph row owned by the exact subject across tenants and soft-delete scopes.</summary>
    Task<int> HardDeleteUserConversationGraphAsync(Guid subjectId, CancellationToken cancellationToken);
    Task<int> CountUserMessagesSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken cancellationToken);
    Task<int> CountTenantMessagesSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken);
    Task<int> ReleaseStaleRunningConversationsForUserAsync(
        Guid userId,
        DateTime staleBeforeUtc,
        string failureCode,
        string failureMessage,
        DateTime utcNow,
        CancellationToken cancellationToken);
    Task<int> CountRunningConversationsForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<AiProposedAction?> GetProposedActionForUpdateAsync(Guid proposedActionId, CancellationToken cancellationToken);
    Task<AiProposedAction?> GetProposedActionForUpdateAsync(Guid proposedActionId, Guid? admittedActorId, CancellationToken cancellationToken);
    Task<AiProposedAction?> GetProposedActionAsync(Guid proposedActionId, CancellationToken cancellationToken);
    Task UpdateProposedActionAsync(AiProposedAction proposedAction, CancellationToken cancellationToken);
    Task CreateToolExecutionAsync(AiToolExecution toolExecution, CancellationToken cancellationToken);
    Task<IReadOnlyList<AiToolExecution>> ListToolExecutionsForProposedActionAsync(
        Guid proposedActionId,
        CancellationToken cancellationToken);
    Task<AiRetentionCleanupResult> RedactExpiredConversationsAsync(
        DateTime cutoffUtc,
        int retentionDays,
        DateTime utcNow,
        bool dryRun,
        CancellationToken cancellationToken);
}
