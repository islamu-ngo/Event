using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Services;

/// <summary>
/// Background service that periodically refreshes secrets from external providers.
/// Implements exponential backoff on failures with jitter to prevent thundering herd.
/// </summary>
public sealed class SecretRefreshService : BackgroundService
{
    private readonly ISecretProvider _secretProvider;
    private readonly IConfigurationRoot? _configuration;
    private readonly SecretRefreshOptions _options;
    private readonly SecretRefreshMetrics _metrics;
    private readonly ILogger<SecretRefreshService> _logger;
    private readonly string _replicaId;

    private int _consecutiveFailures;
    private DateTime? _lastSuccessfulRefresh;

    public SecretRefreshService(
        ISecretProvider secretProvider,
        IConfiguration configuration,
        IOptions<SecretRefreshOptions> options,
        SecretRefreshMetrics metrics,
        ILogger<SecretRefreshService> logger,
        string? replicaId = null)
    {
        _secretProvider = secretProvider;
        _configuration = configuration as IConfigurationRoot;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
        _replicaId = string.IsNullOrWhiteSpace(replicaId)
            ? Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName
            : replicaId;
    }

    /// <summary>
    /// Gets the number of consecutive refresh failures.
    /// </summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>
    /// Gets the timestamp of the last successful refresh.
    /// </summary>
    public DateTime? LastSuccessfulRefresh => _lastSuccessfulRefresh;

    public SecretRotationLocalAcknowledgement? LastAcknowledgement { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Secret refresh service is disabled");
            return;
        }

        if (!_secretProvider.SupportsRefresh)
        {
            _logger.LogInformation(
                "Secret provider {Provider} does not support refresh, service will not run",
                _secretProvider.ProviderType);
            return;
        }

        _logger.LogInformation(
            "Secret refresh service starting with interval {Interval}",
            _options.RefreshInterval);

        // Apply initial delay with jitter to spread out startup refreshes
        var initialDelay = _options.AddJitter(_options.InitialDelay);
        _logger.LogDebug("Waiting {Delay} before first refresh", initialDelay);

        try
        {
            await Task.Delay(initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Use PeriodicTimer for efficient, drift-free scheduling
        using var timer = new PeriodicTimer(_options.RefreshInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshSecretsAsync(stoppingToken);

            try
            {
                // Calculate next wait time based on failure state
                var waitTime = CalculateNextWaitTime();

                if (waitTime != _options.RefreshInterval)
                {
                    // On failure, use Task.Delay for backoff
                    await Task.Delay(waitTime, stoppingToken);
                }
                else
                {
                    // Normal operation: use the periodic timer
                    await timer.WaitForNextTickAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Secret refresh service stopped");
    }

    private async Task RefreshSecretsAsync(CancellationToken cancellationToken)
    {
        var attemptId = Guid.CreateVersion7();
        using var operation = _metrics.StartRefreshOperation(_secretProvider.ProviderType);

        try
        {
            _logger.LogDebug(
                "Starting secret refresh (consecutive failures: {Failures})",
                _consecutiveFailures);

            await _secretProvider.RefreshAsync(cancellationToken);

            // Reload configuration if available
            if (_configuration is not null)
            {
                _configuration.Reload();
                _logger.LogDebug("Configuration reloaded after secret refresh");
            }

            _consecutiveFailures = 0;
            _lastSuccessfulRefresh = DateTime.UtcNow;
            LastAcknowledgement = Acknowledge(attemptId, SecretRotationLocalStatus.Activated);
            operation.Complete();

            _logger.LogInformation("secret_refresh_local_activated");
        }
        catch (SecretProviderException ex) when (ex.IsTransient)
        {
            _consecutiveFailures++;
            operation.Fail("transient");

            LastAcknowledgement = Acknowledge(attemptId, SecretRotationLocalStatus.Failed);
            _logger.LogWarning(
                "secret_refresh_local_failed kind=transient failures={Failures}",
                _consecutiveFailures);
        }
        catch (SecretProviderException)
        {
            _consecutiveFailures++;
            operation.Fail("permanent");

            LastAcknowledgement = Acknowledge(attemptId, SecretRotationLocalStatus.Failed);
            _logger.LogError(
                "secret_refresh_local_failed kind=permanent failures={Failures}",
                _consecutiveFailures);
        }
        catch (OperationCanceledException)
        {
            // Don't record cancellation as failure
            _logger.LogDebug("Secret refresh cancelled");
            throw;
        }
        catch (Exception)
        {
            _consecutiveFailures++;
            operation.Fail("unknown");
            LastAcknowledgement = Acknowledge(attemptId, SecretRotationLocalStatus.Failed);

            _logger.LogError(
                "secret_refresh_local_failed kind=unknown failures={Failures}",
                _consecutiveFailures);
        }
    }

    private TimeSpan CalculateNextWaitTime()
    {
        if (_consecutiveFailures == 0)
        {
            // Normal interval with jitter
            return _options.AddJitter(_options.RefreshInterval);
        }

        // Use exponential backoff for failures
        var backoff = _options.CalculateBackoffDelay(_consecutiveFailures);

        _logger.LogDebug(
            "Using backoff delay {Delay} after {Failures} consecutive failures",
            backoff,
            _consecutiveFailures);

        return backoff;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Secret refresh service stopping (last success: {LastSuccess}, failures: {Failures})",
            _lastSuccessfulRefresh,
            _consecutiveFailures);

        await base.StopAsync(cancellationToken);
    }

    private SecretRotationLocalAcknowledgement Acknowledge(
        Guid attemptId,
        SecretRotationLocalStatus status) =>
        new(attemptId, _replicaId, "provider-cache", status, DateTimeOffset.UtcNow);
}
