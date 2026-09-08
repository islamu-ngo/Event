namespace Explore.Application.Models;

public sealed record AiRetentionCleanupRunResult(
    DateTime UtcNow,
    int TenantCount,
    int SucceededTenantCount,
    int FailedTenantCount,
    int EligibleConversations,
    int RedactedConversations,
    int RedactedMessages,
    int RedactedRuns,
    int RedactedReferences,
    int RedactedProposedActions,
    int RedactedToolExecutions,
    bool DryRun);
