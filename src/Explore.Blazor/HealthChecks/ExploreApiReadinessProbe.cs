using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.HealthChecks;

public interface IExploreApiReadinessProbe
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
}

public sealed class ExploreApiReadinessProbe(
    IInstanceMessagingSettingsClient apiClient,
    IOptions<ExploreApiReadinessOptions>? options = null,
    ILogger<ExploreApiReadinessProbe>? logger = null) : IExploreApiReadinessProbe
{
    private readonly ExploreApiReadinessOptions _options = options?.Value ?? new ExploreApiReadinessOptions();

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var timeout = _options.StartupTimeout;
        var retryDelay = _options.RetryDelay;
        var attemptTimeout = _options.AttemptTimeout;

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var combinedToken = linkedCts.Token;

        int attempt = 0;
        Exception? lastException = null;

        while (!combinedToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(combinedToken);
                attemptCts.CancelAfter(attemptTimeout);

                _ = await apiClient.GetInstanceResolverConfigurationAsync(
                    cancellationToken: attemptCts.Token).ConfigureAwait(false);

                if (attempt > 1)
                {
                    logger?.LogInformation(
                        "[ExploreApiReadinessProbe] Explore API became ready after {Attempt} attempt(s).",
                        attempt);
                }

                return;
            }
            catch (Exception ex) when (IsTransientReadinessException(ex, cancellationToken))
            {
                lastException = ex;

                if (cancellationToken.IsCancellationRequested)
                {
                    logger?.LogWarning(
                        "[ExploreApiReadinessProbe] Explore API readiness probe aborted because cancellation was requested.");
                    throw;
                }

                if (timeoutCts.IsCancellationRequested)
                {
                    logger?.LogError(
                        ex,
                        "[ExploreApiReadinessProbe] Explore API failed to become ready within {TimeoutSeconds}s after {Attempt} attempt(s).",
                        timeout.TotalSeconds,
                        attempt);
                    break;
                }

                logger?.LogWarning(
                    "[ExploreApiReadinessProbe] Explore API is not ready yet ({ErrorType}: {ErrorMessage}). Retrying in {RetryDelaySeconds:F1}s... (attempt {Attempt})",
                    ex.GetType().Name,
                    ex.Message,
                    retryDelay.TotalSeconds,
                    attempt);

                try
                {
                    await Task.Delay(retryDelay, combinedToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "Explore API readiness check was cancelled by the caller.",
                cancellationToken);
        }

        throw lastException ?? new TimeoutException(
            $"Explore API did not become ready within {timeout.TotalSeconds} seconds.");
    }

    private static bool IsTransientReadinessException(Exception ex, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested)
        {
            return false;
        }

        return ex switch
        {
            ApiException apiEx => apiEx.StatusCode is 404 or 408 or 500 or 502 or 503 or 504,
            HttpRequestException => true,
            TimeoutException => true,
            OperationCanceledException => true,
            _ => false
        };
    }
}

