// ABOUTME: Centralizes BFF auth provider scheme resolution and readiness checks.
// ABOUTME: Keeps provider discovery and minimal-config fallback logic out of auth endpoint handlers.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Blazor.Constants;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

namespace Explore.Blazor.Services.Auth;

public interface IBffProviderReadinessService
{
    string? ResolveProviderScheme(string? provider);

    string? MapSchemeToProviderQueryValue(string scheme);

    Task<string?> ResolvePreferredProviderForDirectLoginAsync(CancellationToken cancellationToken);

    Task<bool> IsProviderReadyAsync(string scheme, CancellationToken cancellationToken);

    Task<BffProviderReadiness> GetProviderReadinessAsync(string scheme, CancellationToken cancellationToken);

    bool HasMinimalProviderConfig(string scheme);
}

public sealed record BffProviderReadiness(bool IsReady, string? FailureCode);

public sealed class BffProviderReadinessService(
    IDynamicAuthSchemeManager schemeManager,
    IOptionsMonitor<OpenIdConnectOptions> optionsMonitor,
    IWebHostEnvironment environment,
    ILogger<BffProviderReadinessService> logger,
    AtprotoAuthenticationMetrics atprotoMetrics,
    ApiBackedAtprotoTransientStore transientStore,
    IMemoryCache cache,
    TimeProvider clock,
    AtprotoOAuthClientFactory? atprotoFactory = null)
    : IBffProviderReadinessService, IDisposable
{
    private readonly SemaphoreSlim atprotoReadinessGate = new(1, 1);
    private static readonly object AtprotoReadinessCacheKey = new();
    private static readonly TimeSpan AtprotoReadinessCacheTtl = TimeSpan.FromSeconds(10);
    private static readonly Regex GoogleClientIdPattern = new(
        @"^[0-9]+-[a-zA-Z0-9\-]+\.apps\.googleusercontent\.com$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string? ResolveProviderScheme(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        return provider.ToLowerInvariant() switch
        {
            "keycloak" => AuthSchemeNames.Keycloak,
            "google" => AuthSchemeNames.Google,
            "atproto" => AuthSchemeNames.Atproto,
            _ => null
        };
    }

    public string? MapSchemeToProviderQueryValue(string scheme)
    {
        return scheme switch
        {
            AuthSchemeNames.Keycloak => "keycloak",
            AuthSchemeNames.Google => "google",
            AuthSchemeNames.Atproto => "atproto",
            _ => null
        };
    }

    public async Task<string?> ResolvePreferredProviderForDirectLoginAsync(CancellationToken cancellationToken)
    {
        var registered = await schemeManager.GetRegisteredProviderSchemesAsync();
        var readyButtonProviders = new List<string>();

        foreach (var scheme in registered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (scheme is not (AuthSchemeNames.Keycloak or AuthSchemeNames.Google))
            {
                continue;
            }

            if (!await IsProviderReadyAsync(scheme, cancellationToken))
            {
                continue;
            }

            var providerQuery = MapSchemeToProviderQueryValue(scheme);
            if (!string.IsNullOrWhiteSpace(providerQuery))
            {
                readyButtonProviders.Add(providerQuery);
            }
        }

        return readyButtonProviders.Count == 1 ? readyButtonProviders[0] : null;
    }

    public async Task<bool> IsProviderReadyAsync(string scheme, CancellationToken cancellationToken)
    {
        var readiness = await GetProviderReadinessAsync(scheme, cancellationToken);
        return readiness.IsReady;
    }

    public async Task<BffProviderReadiness> GetProviderReadinessAsync(string scheme, CancellationToken cancellationToken)
    {
        if (scheme == AuthSchemeNames.Atproto)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var readiness = await GetAtprotoReadinessAsync(cancellationToken);
            atprotoMetrics.RecordReadiness(
                readiness.IsReady,
                readiness.FailureCode,
                Stopwatch.GetElapsedTime(startedAt));
            return new(readiness.IsReady, readiness.FailureCode);
        }

        OpenIdConnectOptions options;

        try
        {
            options = optionsMonitor.Get(scheme);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not load OIDC options for scheme {Scheme}", scheme);
            return new(false, "options_unavailable");
        }

        if (scheme == AuthSchemeNames.Keycloak)
        {
            if (string.IsNullOrWhiteSpace(options.Authority) || string.IsNullOrWhiteSpace(options.ClientId))
            {
                return new(false, "configuration_incomplete");
            }

            var metadataAddress = string.IsNullOrWhiteSpace(options.MetadataAddress)
                ? $"{options.Authority.TrimEnd('/')}/.well-known/openid-configuration"
                : options.MetadataAddress;

            var ready = await HasRequiredOidcMetadataAsync(metadataAddress, "keycloak", requireGoogleIssuer: false, cancellationToken);
            return new(ready, ready ? null : "metadata_unavailable");
        }

        if (scheme == AuthSchemeNames.Google)
        {
            if (string.IsNullOrWhiteSpace(options.ClientId) || !GoogleClientIdPattern.IsMatch(options.ClientId))
            {
                return new(false, "invalid_client_id");
            }

            if (string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                return new(false, "client_secret_unavailable");
            }

            var ready = await HasRequiredOidcMetadataAsync(
                "https://accounts.google.com/.well-known/openid-configuration",
                "google",
                requireGoogleIssuer: true,
                cancellationToken);
            return new(ready, ready ? null : "metadata_unavailable");
        }

        return new(true, null);
    }

    private async Task<AtprotoOAuthReadiness> GetAtprotoReadinessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var readiness = atprotoFactory?.GetReadiness() ?? new AtprotoOAuthReadiness(false, "provider_not_configured");
        if (!readiness.IsReady) return readiness;
        if (cache.TryGetValue<(DateTimeOffset ExpiresAt, AtprotoOAuthReadiness Result)>(AtprotoReadinessCacheKey, out var cached)
            && cached.ExpiresAt > clock.GetUtcNow()) return cached.Result;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        bool entered = false;
        try
        {
            await atprotoReadinessGate.WaitAsync(deadline.Token);
            entered = true;
            if (cache.TryGetValue(AtprotoReadinessCacheKey, out cached)
                && cached.ExpiresAt > clock.GetUtcNow()) return cached.Result;
            try
            {
                readiness = await transientStore.ProbeAsync(deadline.Token)
                    ? AtprotoOAuthReadiness.Ready : new(false, "state_store_unavailable");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { readiness = new(false, "state_store_unavailable"); }
            cache.Set(AtprotoReadinessCacheKey, (clock.GetUtcNow() + AtprotoReadinessCacheTtl, readiness), AtprotoReadinessCacheTtl);
            return readiness;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timed-out waiter must not replace the result being filled by the active probe.
            return new(false, "state_store_unavailable");
        }
        finally { if (entered) atprotoReadinessGate.Release(); }
    }

    public void Dispose() => atprotoReadinessGate.Dispose();

    public bool HasMinimalProviderConfig(string scheme)
    {
        if (scheme == AuthSchemeNames.Atproto)
        {
            return atprotoFactory?.GetReadiness().IsReady == true;
        }

        try
        {
            var options = optionsMonitor.Get(scheme);
            return !string.IsNullOrWhiteSpace(options.Authority)
                && !string.IsNullOrWhiteSpace(options.ClientId);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> HasRequiredOidcMetadataAsync(
        string metadataAddress,
        string provider,
        bool requireGoogleIssuer,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            // Force IPv4 to avoid Happy Eyeballs hanging on unreachable AAAA records
            // (same pattern as the OIDC backchannel handler in DynamicAuthSchemeManager).
            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ConnectCallback = async (context, token) =>
                {
                    var socket = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.InterNetwork,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, token);
                        return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };

            // Allow self-signed / dev certs for non-production Keycloak instances.
            if (environment.IsDevelopment())
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                    IsAllowedDevelopmentCertificate(sender, errors);
            }

            using var httpClient = new HttpClient(handler, disposeHandler: false);
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            logger.LogInformation(
                "[AuthEndpoints] Checking OIDC discovery for {Provider} at {MetadataAddress}",
                provider, metadataAddress);

            using var response = await httpClient.GetAsync(metadataAddress, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "[AuthEndpoints] {Provider} discovery endpoint returned status {StatusCode} at {MetadataAddress}",
                    provider,
                    (int)response.StatusCode,
                    metadataAddress);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
            var root = document.RootElement;

            if (!TryGetNonEmptyString(root, "issuer", out var issuer)
                || !TryGetNonEmptyString(root, "authorization_endpoint", out _)
                || !TryGetNonEmptyString(root, "token_endpoint", out _))
            {
                logger.LogWarning(
                    "[AuthEndpoints] {Provider} discovery document at {MetadataAddress} is missing required fields (issuer/authorization_endpoint/token_endpoint)",
                    provider, metadataAddress);
                return false;
            }

            if (requireGoogleIssuer
                && !string.Equals(issuer, "https://accounts.google.com", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(issuer, "accounts.google.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            logger.LogInformation(
                "[AuthEndpoints] {Provider} discovery check passed (issuer: {Issuer})",
                provider, issuer);
            return true;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "[AuthEndpoints] {Provider} discovery request TIMED OUT for {MetadataAddress}",
                provider, metadataAddress);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[AuthEndpoints] {Provider} discovery check FAILED at {MetadataAddress}: {Error}",
                provider, metadataAddress, ex.Message);
            return false;
        }
    }

    private static bool TryGetNonEmptyString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw;
        return true;
    }

    private static bool IsAllowedDevelopmentCertificate(object? sender, System.Net.Security.SslPolicyErrors errors)
    {
        if (errors == System.Net.Security.SslPolicyErrors.None)
        {
            return true;
        }

        return sender is System.Net.Security.SslStream { TargetHostName: { } host }
               && IsDevelopmentTrustedHost(host);
    }

    private static bool IsDevelopmentTrustedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("100.64.0.2", StringComparison.OrdinalIgnoreCase)
            || IsTailscaleAddress(host))
        {
            return true;
        }

        var additionalHosts = Environment.GetEnvironmentVariable("BFF_DEV_TRUSTED_HOSTS");
        if (string.IsNullOrWhiteSpace(additionalHosts))
        {
            return false;
        }

        return additionalHosts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTailscaleAddress(string host)
    {
        if (!System.Net.IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        // Tailscale/CGNAT range: 100.64.0.0/10
        return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
    }
}
