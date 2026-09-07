// Retries transient SMTP failures (timeouts, 421/451) with exponential backoff.

using Explore.Application.Models;
using Polly;
using Polly.Retry;

namespace Explore.Infrastructure.Mail;

/// <summary>
/// Resilience pipelines for email operations using Polly v8.
/// </summary>
public static class EmailResiliencePipelines
{
    /// <summary>
    /// Creates a retry pipeline for email sending: 3 attempts with exponential backoff.
    /// <para>
    /// Retries on: timeouts, connection errors, SMTP 421/451 (transient).
    /// Does NOT retry on: auth failure, SMTP 5xx (permanent), bad address.
    /// </para>
    /// </summary>
    public static ResiliencePipeline<EmailResult> CreateSendPipeline()
    {
        return new ResiliencePipelineBuilder<EmailResult>()
            .AddRetry(new RetryStrategyOptions<EmailResult>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<EmailResult>()
                    .HandleResult(r => !r.Success && IsTransient(r.ErrorMessage)),
            })
            .Build();
    }

    /// <summary>
    /// Determines if an error message indicates a transient (retryable) failure.
    /// </summary>
    public static bool IsTransient(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return false;

        // Transient: timeouts, connection drops, temporary SMTP server errors
        return errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("421", StringComparison.Ordinal)   // SMTP 421: service not available
            || errorMessage.Contains("451", StringComparison.Ordinal)   // SMTP 451: temporary failure
            || errorMessage.Contains("452", StringComparison.Ordinal);  // SMTP 452: insufficient storage
    }
}
