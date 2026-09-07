using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Contracts.Webhooks;

public interface IIncomingWebhookProcessingService
{
    Task<IncomingWebhookClaimExecutionResult> ProcessAsync(
        IncomingWebhookClaim claim,
        CancellationToken cancellationToken);
}

public interface IIncomingWebhookClaimExecutor
{
    Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(
        IncomingWebhookClaim claim,
        CancellationToken cancellationToken);
}

public interface IIncomingWebhookDrainService
{
    Task<IncomingWebhookDrainResult> ProcessBatchAsync(CancellationToken cancellationToken);
}

public interface IIncomingWebhookEffectProcessingService
{
    Task<IncomingWebhookClaimExecutionResult> ProcessAsync(
        IncomingWebhookEffectClaim claim,
        CancellationToken cancellationToken);
}

public interface IIncomingWebhookEffectClaimExecutor
{
    Task<IncomingWebhookClaimExecutionResult> ExecuteAsync(
        IncomingWebhookEffectClaim claim,
        CancellationToken cancellationToken);
}

public interface IIncomingWebhookEffectDrainService
{
    Task<IncomingWebhookDrainResult> ProcessBatchAsync(CancellationToken cancellationToken);
}

public sealed record IncomingWebhookDrainResult(
    int ClaimedCount,
    int CompletedCount,
    int LeaseLostCount,
    int AuthorizationDeniedCount,
    int FailedCount);

public sealed record IncomingWebhookClaimExecutionResult(
    IncomingWebhookClaimExecutionOutcome Outcome,
    string? FailureCategory = null)
{
    public static IncomingWebhookClaimExecutionResult Completed(string? outcomeCategory = null) =>
        new(IncomingWebhookClaimExecutionOutcome.Completed, outcomeCategory);

    public static IncomingWebhookClaimExecutionResult LeaseLost() =>
        new(IncomingWebhookClaimExecutionOutcome.LeaseLost);

    public static IncomingWebhookClaimExecutionResult AuthorizationDenied() =>
        new(IncomingWebhookClaimExecutionOutcome.AuthorizationDenied, "incoming_webhook_worker_denied");
}

public enum IncomingWebhookClaimExecutionOutcome
{
    Completed = 1,
    LeaseLost = 2,
    AuthorizationDenied = 3
}
