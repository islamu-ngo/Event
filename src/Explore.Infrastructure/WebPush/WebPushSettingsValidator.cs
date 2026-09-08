using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.WebPush;

public sealed class WebPushSettingsValidator : IValidateOptions<WebPushSettings>
{
    public ValidateOptionsResult Validate(string? name, WebPushSettings options)
    {
        var failures = new List<string>();

        if (options.Enabled && string.IsNullOrWhiteSpace(options.VapidSubject)) failures.Add("WebPush:VapidSubject is required when Web Push is enabled.");
        if (options.Enabled && string.IsNullOrWhiteSpace(options.VapidPublicKey)) failures.Add("WebPush:VapidPublicKey is required when Web Push is enabled.");
        if (options.Enabled && string.IsNullOrWhiteSpace(options.VapidPrivateKey)) failures.Add("WebPush:VapidPrivateKey is required when Web Push is enabled.");
        if (options.Enabled && !IsValidVapidSubject(options.VapidSubject)) failures.Add("WebPush:VapidSubject must be an absolute mailto: or https: URI.");
        if (options.Enabled && !HasDecodedLength(options.VapidPublicKey, 65)) failures.Add("WebPush:VapidPublicKey must be a URL-safe Base64 encoded uncompressed P-256 public key.");
        if (options.Enabled && !HasDecodedLength(options.VapidPrivateKey, 32)) failures.Add("WebPush:VapidPrivateKey must be a URL-safe Base64 encoded P-256 private key.");
        if (options.RequestTimeoutSeconds <= 0) failures.Add("WebPush:RequestTimeoutSeconds must be greater than zero.");
        if (options.PollingIntervalSeconds <= 0) failures.Add("WebPush:PollingIntervalSeconds must be greater than zero.");
        if (options.BatchSize <= 0) failures.Add("WebPush:BatchSize must be greater than zero.");
        if (options.MaxAttemptCount <= 0) failures.Add("WebPush:MaxAttemptCount must be greater than zero.");
        if (options.InitialRetryDelaySeconds <= 0) failures.Add("WebPush:InitialRetryDelaySeconds must be greater than zero.");
        if (options.MaxRetryDelaySeconds < options.InitialRetryDelaySeconds) failures.Add("WebPush:MaxRetryDelaySeconds must be greater than or equal to InitialRetryDelaySeconds.");
        if (options.ProcessingLeaseTimeoutSeconds <= 0) failures.Add("WebPush:ProcessingLeaseTimeoutSeconds must be greater than zero.");
        if (options.HealthDueDispatchWarningThreshold is < 1 or > 100000) failures.Add("WebPush:HealthDueDispatchWarningThreshold must be between 1 and 100000.");
        if (options.HealthStaleProcessingWarningThreshold is < 1 or > 10000) failures.Add("WebPush:HealthStaleProcessingWarningThreshold must be between 1 and 10000.");
        if (options.HealthTerminalFailureWarningThreshold is < 1 or > 10000) failures.Add("WebPush:HealthTerminalFailureWarningThreshold must be between 1 and 10000.");
        if (string.IsNullOrWhiteSpace(options.ConsumerId)) failures.Add("WebPush:ConsumerId is required.");
        if (!IsSafeRelativePath(options.NotificationOpenPath)) failures.Add("WebPush:NotificationOpenPath must be a relative path starting with '/'.");
        if (!IsSafeRelativePath(options.NotificationRefreshPath)) failures.Add("WebPush:NotificationRefreshPath must be a relative path starting with '/'.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSafeRelativePath(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith('/')
            && !value.StartsWith("//", StringComparison.Ordinal)
            && !value.Contains("://", StringComparison.Ordinal);
    }

    private static bool IsValidVapidSubject(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeMailto || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool HasDecodedLength(string value, int expectedLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            return false;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            return Convert.FromBase64String(base64).Length == expectedLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
