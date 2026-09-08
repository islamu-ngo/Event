namespace Explore.Application.Models;

public sealed record WebPushSendResult(
    bool Success,
    WebPushSendFailureKind FailureKind,
    int? StatusCode,
    string FailureCategory,
    string? SanitizedErrorMessage)
{
    public static WebPushSendResult Succeeded(int? statusCode = null)
    {
        return new WebPushSendResult(true, WebPushSendFailureKind.None, statusCode, "web_push_delivered", null);
    }

    public static WebPushSendResult StaleSubscription(int? statusCode, string? message)
    {
        return new WebPushSendResult(false, WebPushSendFailureKind.StaleSubscription, statusCode, "web_push_subscription_stale", message);
    }

    public static WebPushSendResult Retryable(int? statusCode, string? message)
    {
        return new WebPushSendResult(false, WebPushSendFailureKind.Retryable, statusCode, "web_push_retryable", message);
    }

    public static WebPushSendResult PermanentNonRetryable(int? statusCode, string? message)
    {
        return new WebPushSendResult(false, WebPushSendFailureKind.PermanentNonRetryable, statusCode, "web_push_permanent_provider_failure", message);
    }
}

public enum WebPushSendFailureKind
{
    None = 0,
    StaleSubscription = 1,
    Retryable = 2,
    PermanentNonRetryable = 3
}
