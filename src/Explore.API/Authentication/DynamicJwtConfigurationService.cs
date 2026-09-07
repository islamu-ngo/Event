using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Explore.API.Authentication;

public sealed class DynamicJwtConfigurationService : IJwtAuthorityRefreshNotifier, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DynamicJwtConfigurationService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    private volatile State _state;

    public void Dispose() => _reloadGate.Dispose();

    public DynamicJwtConfigurationService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DynamicJwtConfigurationService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;

        _state = BuildFromEnvironment();
    }

    public IConfigurationManager<OpenIdConnectConfiguration>? ConfigurationManager => _state.Manager;

    public string? Authority => _state.Authority;

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _reloadGate.WaitAsync(ct);
        try
        {
            var next = await BuildFromDatabaseAsync(ct) ?? BuildFromEnvironment();

            // Eagerly prefetch OIDC/JWKS metadata on the new ConfigurationManager before
            // swapping it in. Without this, ReloadAsync creates a brand-new manager with no
            // cached signing keys, and the old state (which DID have keys) is discarded.
            // The first request then triggers a lazy fetch — if Keycloak is slow at that
            // instant, ALL authenticated requests fail with IDX10500 until the fetch succeeds.
            // By validating upfront, we only swap when we know the new manager is functional.
            if (next.Manager is not null)
            {
                try
                {
                    var config = await next.Manager.GetConfigurationAsync(ct).ConfigureAwait(false);
                    var keyCount = config?.SigningKeys?.Count ?? 0;

                    _state = next;
                    _logger.LogInformation(
                        "[JWT] Dynamic JWT configuration reloaded. Authority={Authority}, Source={Source}, SigningKeys={KeyCount}",
                        next.Authority ?? "<none>",
                        next.Source,
                        keyCount);
                }
                catch (Exception ex)
                {
                    // New manager couldn't fetch keys. Keep the old state which still has
                    // cached signing keys from the last successful fetch.
                    _logger.LogWarning(ex,
                        "[JWT] New ConfigurationManager failed to prefetch OIDC/JWKS metadata. " +
                        "Retaining existing state (Source={Source}) with cached keys.",
                        _state.Source);
                }
            }
            else
            {
                // No manager (e.g. pre-onboarding or authority not yet configured).
                _state = next;
                _logger.LogInformation(
                    "[JWT] Dynamic JWT configuration reloaded. Authority={Authority}, Source={Source}",
                    next.Authority ?? "<none>",
                    next.Source);
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task<State?> BuildFromDatabaseAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var configService = scope.ServiceProvider.GetService<IAuthProviderConfigurationService>();
            if (configService is null)
            {
                return null;
            }

            var dto = await configService.ReadConfigurationAsync();
            if (dto.PrimaryProviderId != (int)AuthenticationProviderKind.Keycloak
                || string.IsNullOrWhiteSpace(dto.KeycloakAuthority)
                || string.IsNullOrWhiteSpace(dto.KeycloakClientId))
            {
                return null;
            }

            var authority = dto.KeycloakAuthority.TrimEnd('/');
            return BuildState(authority, source: "Database");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[JWT] Failed to read auth provider configuration from DB; falling back to environment.");
            return null;
        }
    }

    private State BuildFromEnvironment()
    {
        var authority = _configuration["Keycloak:Authority"]?.TrimEnd('/');
        var metadataAddress = _configuration["Keycloak:MetadataAddress"];

        if (string.IsNullOrWhiteSpace(authority) && string.IsNullOrWhiteSpace(metadataAddress))
        {
            return State.Empty;
        }

        return BuildState(authority, metadataAddress, source: "Environment");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "SocketsHttpHandler and HttpClient instances are owned by ConfigurationManager for dynamic JWKS refresh.")]
    private State BuildState(string? authority, string? metadataAddress = null, string source = "")
    {
        var resolvedMetadata = !string.IsNullOrWhiteSpace(metadataAddress)
            ? metadataAddress
            : $"{authority}/.well-known/openid-configuration";

        var retriever = new OpenIdConnectConfigurationRetriever();

        // Bounded SocketsHttpHandler replaces HttpDocumentRetriever's default HttpClient (100s timeout).
        // IPv4 forced via ConnectCallback: self-hosted providers often publish AAAA records but the
        // host's IPv6 stack may be broken, causing .NET's Happy Eyeballs to hang on IPv6 before
        // falling back. BFF's DynamicAuthSchemeManager.CreateIpv4Handler uses the same pattern.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        if (_environment.IsDevelopment())
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                IsAllowedDevelopmentCertificate(sender, errors);
        }

        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var documentRetriever = new HttpDocumentRetriever(httpClient)
        {
            RequireHttps = !_environment.IsDevelopment()
                && !string.Equals(
                    _configuration["Keycloak:RequireHttpsMetadata"],
                    "false",
                    StringComparison.OrdinalIgnoreCase)
        };

        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            resolvedMetadata,
            retriever,
            documentRetriever);

        return new State(manager, authority, source);
    }

    private bool IsAllowedDevelopmentCertificate(object? sender, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (!_environment.IsDevelopment())
        {
            return false;
        }

        return sender is SslStream { TargetHostName: { } host }
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
        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        // Tailscale/CGNAT range: 100.64.0.0/10
        return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
    }

    private sealed record State(
        IConfigurationManager<OpenIdConnectConfiguration>? Manager,
        string? Authority,
        string Source)
    {
        public static readonly State Empty = new(null, null, "None");
    }
}
