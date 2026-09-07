namespace Explore.Application.Exceptions;

public sealed class AdmissionRecoveryRateLimitExceededException(int retryAfterSeconds) : Exception
{
    public int RetryAfterSeconds { get; } = Math.Max(1, retryAfterSeconds);
}
