using Explore.Application.Contracts.Services;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Explore.API.BackgroundServices;

/// <summary>
/// Executes bounded deployment-selected authorization reconciliation during API startup.
/// </summary>
public sealed class CerbosPolicyBootSyncRunner(
    IServiceScopeFactory scopeFactory,
    IOptions<CerbosPolicyBootSyncOptions> options,
    AuthorizationProviderBootstrapState bootstrapState,
    ILogger<CerbosPolicyBootSyncRunner> logger)
{
    private readonly CerbosPolicyBootSyncOptions _options = options.Value;

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var shouldRetry = await RunAttemptAsync(cancellationToken).ConfigureAwait(false);
            if (!shouldRetry || attempt == maxAttempts)
            {
                return;
            }

            bootstrapState.MarkPendingAfterFailure();

            logger.LogInformation(
                "Authorization provider boot reconciliation will retry. Attempt={Attempt} MaxAttempts={MaxAttempts}",
                attempt + 1,
                maxAttempts);
            await Task.Delay(GetRetryDelay(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> RunAttemptAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GetTimeout());

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var configurationService = scope.ServiceProvider
                .GetRequiredService<IAuthorizationProviderConfigurationService>();

            var result = await configurationService
                .ReconcileDeploymentProviderAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            if (!result.Attempted)
            {
                logger.LogDebug("Authorization provider boot reconciliation skipped because deployment intent is unset");
                return false;
            }

            if (result.Succeeded)
            {
                logger.LogInformation("Authorization provider boot reconciliation completed successfully");
                return false;
            }

            logger.LogWarning("Authorization provider boot reconciliation did not complete: {Message}", result.Message);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Cerbos policy boot sync cancelled during shutdown");
            return false;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Cerbos policy boot sync timed out after {TimeoutSeconds}s",
                GetTimeout().TotalSeconds);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Cerbos policy boot sync failed with an unexpected error. FailureType={FailureType}",
                ex.GetType().Name);
            return true;
        }
    }

    private TimeSpan GetRetryDelay()
    {
        return TimeSpan.FromSeconds(Math.Max(0, _options.RetryDelaySeconds));
    }

    private TimeSpan GetTimeout()
    {
        return TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
    }
}
