using System.Net;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebPush;

namespace Explore.Infrastructure.WebPush;

public sealed class WebPushNotificationSender(
    IOptions<WebPushSettings> options,
    HttpClient httpClient,
    WebPushEndpointSafetyPolicy endpointSafetyPolicy,
    ILogger<WebPushNotificationSender> logger) : IWebPushNotificationSender
{
    private readonly WebPushSettings _settings = options.Value;

    public async Task<WebPushSendResult> SendAsync(WebPushSendEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));

        try
        {
            var endpointSafety = await endpointSafetyPolicy.ValidateAsync(envelope.Endpoint, timeout.Token);
            if (!endpointSafety.IsAllowed)
            {
                return endpointSafety.IsRetryable
                    ? WebPushSendResult.Retryable(null, "Web Push endpoint DNS resolution failed.")
                    : WebPushSendResult.PermanentNonRetryable(null, "Web Push endpoint is not permitted.");
            }

            using var client = new WebPushClient(httpClient);
            var subscription = new PushSubscription(envelope.Endpoint, envelope.P256Dh, envelope.AuthSecret);
            var vapid = new VapidDetails(
                _settings.VapidSubject.Trim(),
                _settings.VapidPublicKey.Trim(),
                _settings.VapidPrivateKey.Trim());
            var options = new Dictionary<string, object>
            {
                ["vapidDetails"] = vapid,
                ["TTL"] = envelope.TimeToLiveSeconds,
                ["headers"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Topic"] = envelope.Topic,
                    ["Urgency"] = ToHeaderValue(envelope.Urgency)
                }
            };

            await client.SendNotificationAsync(subscription, envelope.PayloadJson, options, timeout.Token);
            return WebPushSendResult.Succeeded();
        }
        catch (WebPushException ex)
        {
            var statusCode = (int?)ex.StatusCode;
            return ClassifyStatus(statusCode, $"Web Push provider returned HTTP {statusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Web Push send timed out for correlation {CorrelationId}", envelope.CorrelationId);
            return WebPushSendResult.Retryable(null, "Web Push send timed out.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Web Push transport failed for correlation {CorrelationId}", envelope.CorrelationId);
            return WebPushSendResult.Retryable((int?)ex.StatusCode, "Web Push transport failed.");
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Web Push request/configuration is malformed for correlation {CorrelationId}", envelope.CorrelationId);
            return WebPushSendResult.PermanentNonRetryable(null, "Web Push request or VAPID configuration is malformed.");
        }
    }

    private static WebPushSendResult ClassifyStatus(int? statusCode, string message)
    {
        return statusCode switch
        {
            404 or 410 => WebPushSendResult.StaleSubscription(statusCode, message),
            429 => WebPushSendResult.Retryable(statusCode, message),
            >= 500 => WebPushSendResult.Retryable(statusCode, message),
            >= 300 and < 400 => WebPushSendResult.PermanentNonRetryable(statusCode, message),
            400 or 401 or 403 => WebPushSendResult.PermanentNonRetryable(statusCode, message),
            _ => WebPushSendResult.Retryable(statusCode, message)
        };
    }

    private static string ToHeaderValue(WebPushUrgency urgency) => urgency switch
    {
        WebPushUrgency.VeryLow => "very-low",
        WebPushUrgency.Low => "low",
        WebPushUrgency.Normal => "normal",
        WebPushUrgency.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(urgency), urgency, "Unsupported Web Push urgency.")
    };
}
