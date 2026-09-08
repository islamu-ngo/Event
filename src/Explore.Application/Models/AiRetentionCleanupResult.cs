namespace Explore.Application.Models;

public sealed record AiRetentionCleanupResult(
    DateTime CutoffUtc,
    int RetentionDays,
    int EligibleConversations,
    int RedactedConversations,
    int RedactedMessages,
    int RedactedRuns,
    int RedactedReferences,
    int RedactedProposedActions,
    int RedactedToolExecutions,
    bool DryRun);
