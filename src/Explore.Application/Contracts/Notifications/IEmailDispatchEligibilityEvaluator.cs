namespace Explore.Application.Contracts.Notifications;

public interface IEmailDispatchEligibilityEvaluator
{
    Task<EmailDispatchEligibilityResult> EvaluateAndBeginProviderHandoffAsync(
        EmailDispatchEligibilityRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record EmailDispatchEligibilityRequest(
    Guid TenantId,
    Guid OutboxId,
    Guid ProcessingLeaseToken,
    int AttemptNumber,
    int GlobalSmtpRateLimitPerMinute,
    int TenantSmtpRateLimitPerMinute,
    string ConsumerId,
    DateTime EvaluatedAt);

public sealed record EmailDispatchEligibilityResult(
    EmailDispatchEligibilityOutcome Outcome,
    string? RecipientEmail,
    string? SkipReason,
    Guid? ReceiptId = null,
    int? AttemptNumber = null,
    DateTime? RetryAt = null);

public enum EmailDispatchEligibilityOutcome
{
    Eligible = 1,
    Skipped = 2,
    TenantPaused = 3,
    LostClaim = 4,
    RateDeferred = 5,
    ProcessorPaused = 6,
    Parked = 7
}
