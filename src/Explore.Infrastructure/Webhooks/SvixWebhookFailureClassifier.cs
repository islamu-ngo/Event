using Svix;

namespace Explore.Infrastructure.Webhooks;

internal static class SvixWebhookFailureClassifier
{
    public static SvixWebhookFailure Classify(ApiException exception)
    {
        if (exception.ErrorCode is 401 or 403)
        {
            return new SvixWebhookFailure("svix_auth_failed", false, $"SvixApi:{exception.ErrorCode}");
        }

        if (exception.ErrorCode is 400 or 404 or 409 or 422)
        {
            return new SvixWebhookFailure("svix_request_rejected", false, $"SvixApi:{exception.ErrorCode}");
        }

        if (exception.ErrorCode == 429 || exception.ErrorCode >= 500)
        {
            return new SvixWebhookFailure("svix_provider_unavailable", true, $"SvixApi:{exception.ErrorCode}");
        }

        return new SvixWebhookFailure("svix_provider_failed", true, $"SvixApi:{exception.ErrorCode}");
    }
}

internal sealed record SvixWebhookFailure(string Category, bool IsRetryable, string SafeDetail);
