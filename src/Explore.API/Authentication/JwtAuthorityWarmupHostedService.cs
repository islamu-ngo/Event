using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Explore.API.Authentication;

/// <summary>
/// Warms up the dynamic JWT bearer configuration on API startup.
/// </summary>
/// <remarks>
/// Without warmup, the first authenticated request triggers a lazy fetch of the OIDC discovery document
/// and JWKS from Keycloak inside the JwtBearer authentication handler. That network call (DNS + TLS + HTTP)
/// frequently exceeds the BFF's Polly attempt timeout (4s for bff-interactive), causing every initial
/// authenticated downstream call to time out and retry while the user is stuck on a half-loaded page.
/// This service forces the fetch once during startup so the first real request finds the keys cached.
/// Failures are logged but never thrown — the API must start even if Keycloak is briefly unreachable.
/// </remarks>
internal sealed class JwtAuthorityWarmupHostedService : IHostedService, IDisposable
{
    private readonly DynamicJwtConfigurationService _dynamicConfig;
    private readonly ILogger<JwtAuthorityWarmupHostedService> _logger;
    private CancellationTokenSource? _backgroundCts;

    private static readonly TimeSpan WarmupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2)
    ];

    public JwtAuthorityWarmupHostedService(
        DynamicJwtConfigurationService dynamicConfig,
        ILogger<JwtAuthorityWarmupHostedService> logger)
    {
        _dynamicConfig = dynamicConfig;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Attempt initial warmup with a bounded timeout so startup isn't delayed.
        // If Keycloak is unreachable, fire-and-forget a background retry loop.
        var warmedUp = await TryWarmupAsync(cancellationToken).ConfigureAwait(false);
        if (!warmedUp)
        {
            _backgroundCts = new CancellationTokenSource();
            _ = Task.Run(() => RetryWarmupInBackgroundAsync(_backgroundCts.Token), _backgroundCts.Token);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _backgroundCts?.Cancel();
        _backgroundCts?.Dispose();
        _backgroundCts = null;
    }

    private async Task<bool> TryWarmupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(WarmupTimeout);

            await _dynamicConfig.ReloadAsync(cts.Token).ConfigureAwait(false);

            var manager = _dynamicConfig.ConfigurationManager;
            if (manager is null)
            {
                _logger.LogInformation(
                    "[JWT-Warmup] No JWT authority configured yet. " +
                    "Warmup skipped; will be retried after onboarding completes.");
                return false;
            }

            var oidcConfig = await manager.GetConfigurationAsync(cts.Token).ConfigureAwait(false);
            var keyCount = oidcConfig?.SigningKeys?.Count ?? 0;
            _logger.LogInformation(
                "[JWT-Warmup] OIDC metadata + JWKS prefetched. Authority={Authority}, Issuer={Issuer}, SigningKeys={KeyCount}",
                _dynamicConfig.Authority ?? "<none>",
                oidcConfig?.Issuer ?? "<none>",
                keyCount);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "[JWT-Warmup] Timed out after {Timeout}s while prefetching OIDC/JWKS metadata. Background retry will continue.",
                WarmupTimeout.TotalSeconds);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[JWT-Warmup] Failed to prefetch OIDC/JWKS metadata within {Timeout}s. " +
                "Background retry will continue.",
                WarmupTimeout.TotalSeconds);
            return false;
        }
    }

    private async Task RetryWarmupInBackgroundAsync(CancellationToken cancellationToken)
    {
        foreach (var delay in RetryDelays)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(WarmupTimeout);
                await _dynamicConfig.ReloadAsync(cts.Token).ConfigureAwait(false);

                var manager = _dynamicConfig.ConfigurationManager;
                if (manager is null) continue;

                var oidcConfig = await manager.GetConfigurationAsync(cts.Token).ConfigureAwait(false);
                var keyCount = oidcConfig?.SigningKeys?.Count ?? 0;
                _logger.LogInformation(
                    "[JWT-Warmup] Background retry succeeded. Authority={Authority}, SigningKeys={KeyCount}",
                    _dynamicConfig.Authority ?? "<none>",
                    keyCount);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (delay == RetryDelays[^1])
            {
                _logger.LogError("[JWT-Warmup] All {Count} background retries exhausted. JWT auth will lazy-load on first request.", RetryDelays.Length);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[JWT-Warmup] Background retry attempt failed (next in {Delay}s).", delay.TotalSeconds);
            }
        }
    }
}
