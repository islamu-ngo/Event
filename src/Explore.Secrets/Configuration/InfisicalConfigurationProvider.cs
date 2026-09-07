namespace Explore.Secrets.Configuration;

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Configuration provider that loads secrets from Infisical via direct REST API calls.
/// Uses HttpClient instead of Infisical.Sdk (whose Rust FFI hangs against self-hosted instances).
/// Secrets are loaded during startup and optionally reloaded periodically.
/// </summary>
public sealed class InfisicalConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly InfisicalConfigurationSource _source;
    private string? _accessToken;
    private Timer? _reloadTimer;
    private bool _disposed;

    public InfisicalConfigurationProvider(InfisicalConfigurationSource source)
    {
        _source = source;
    }

    /// <inheritdoc />
    public override void Load()
    {
        try
        {
            LoadViaRestApi();

            if (_source.ReloadOnChange && _reloadTimer is null)
            {
                _reloadTimer = new Timer(
                    _ => ReloadViaRestApi(),
                    null,
                    _source.ReloadInterval,
                    _source.ReloadInterval);
            }
        }
        catch (InvalidOperationException exception) when (IsBoundedReasonCode(exception.Message))
        {
            throw new InvalidOperationException(
                $"{exception.Message}: Infisical startup loading failed. "
                + "Verify the deployment-owned authority and retry.");
        }
        catch (Exception)
        {
            if (_source.ThrowOnFirstLoadFailure)
            {
                throw new InvalidOperationException(
                    "secret_authority_unavailable: Infisical startup loading failed. "
                    + "Verify the deployment-owned authority and retry.");
            }

            Console.Error.WriteLine(
                "[Infisical] secret_authority_unavailable; verify authority configuration and retry.");
        }
    }

    /// <summary>
    /// Loads secrets using direct REST API calls instead of the Infisical.Sdk package.
    /// The SDK (3.0.4) wraps a native Rust FFI binary whose LoginAsync hangs for 100+ seconds
    /// against self-hosted Infisical instances. The REST endpoints respond in &lt;500ms.
    /// IPv4 is forced because many self-hosted deployments publish AAAA records that are
    /// unreachable, causing .NET's Happy Eyeballs to block until timeout.
    /// </summary>
    private void LoadViaRestApi()
    {
        using var handler = CreateIpv4Handler();
        using var http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        var effectiveUrl = _source.Url.TrimEnd('/');

        // Authenticate if we don't have a token yet
        if (string.IsNullOrEmpty(_accessToken))
        {
            var loginResp = http.PostAsJsonAsync(
                $"{effectiveUrl}/api/v1/auth/universal-auth/login",
                new { clientId = _source.ClientId, clientSecret = _source.ClientSecret })
                .GetAwaiter().GetResult();

            if (!loginResp.IsSuccessStatusCode)
            {
                throw ProviderFailure(loginResp.StatusCode);
            }

            var loginJson = loginResp.Content
                .ReadFromJsonAsync<InfisicalLoginResponse>()
                .GetAwaiter().GetResult();

            _accessToken = loginJson?.AccessToken;
            if (string.IsNullOrEmpty(_accessToken))
            {
                throw new InvalidOperationException("secret_authority_invalid");
            }
        }

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

        var newData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _source.Paths)
        {
            var listUrl =
                $"{effectiveUrl}/api/v3/secrets/raw"
                + $"?workspaceId={Uri.EscapeDataString(_source.ProjectId)}"
                + $"&environment={Uri.EscapeDataString(_source.Environment)}"
                + $"&secretPath={Uri.EscapeDataString(path)}"
                + "&expandSecretReferences=true&recursive=true";

            var listResp = http.GetAsync(listUrl).GetAwaiter().GetResult();

            if (!listResp.IsSuccessStatusCode)
            {
                throw ProviderFailure(listResp.StatusCode);
            }

            var listJson = listResp.Content
                .ReadFromJsonAsync<InfisicalListSecretsResponse>()
                .GetAwaiter().GetResult();

            if (listJson?.Secrets is null || listJson.Secrets.Count == 0)
            {
                continue;
            }

            foreach (var secret in listJson.Secrets)
            {
                if (string.IsNullOrEmpty(secret.SecretKey)) continue;

                var secretValue = secret.SecretValue;
                if (string.IsNullOrWhiteSpace(secretValue)) continue;

                // Convert to .NET configuration key format
                var configKey = ConvertToConfigurationKey(secret.SecretKey, path);
                newData[configKey] = secretValue;

                // Also store with original key for direct access
                newData[secret.SecretKey] = secretValue;

                if (configKey.Equals("Database:Name", StringComparison.OrdinalIgnoreCase))
                {
                    newData["Database:Database"] = secretValue;
                }
                else if (configKey.StartsWith("Database:Erasure:", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = configKey["Database:Erasure:".Length..];
                    newData[$"PrivacyErasureAuthorityDatabase:{suffix}"] = secretValue;
                    newData[$"DatabaseErasure:{suffix}"] = secretValue;
                }

            }
        }

        Data = newData;
    }

    private void ReloadViaRestApi()
    {
        try
        {
            // Clear token to force re-authentication on reload
            _accessToken = null;
            LoadViaRestApi();
            OnReload();
        }
        catch (Exception)
        {
            Console.Error.WriteLine(
                "[Infisical] secret_authority_reload_failed; keeping the last-known-good configuration.");
        }
    }

    private static InvalidOperationException ProviderFailure(HttpStatusCode statusCode) =>
        new(statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "secret_authority_unauthorized"
            : "secret_authority_unavailable");

    private static bool IsBoundedReasonCode(string message) => message is
        "secret_authority_unauthorized" or
        "secret_authority_unavailable" or
        "secret_authority_invalid";

    /// <summary>
    /// Creates an HttpHandler that forces IPv4 connections.
    /// Self-hosted Infisical often publishes AAAA records that are unreachable;
    /// .NET's Happy Eyeballs prefers IPv6 and blocks until timeout.
    /// </summary>
    private static SocketsHttpHandler CreateIpv4Handler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = static async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(
                context.DnsEndPoint.Host,
                AddressFamily.InterNetwork,
                cancellationToken).ConfigureAwait(false);

            if (addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp)
            { NoDelay = true };

            try
            {
                await socket.ConnectAsync(
                    addresses,
                    context.DnsEndPoint.Port,
                    cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    private sealed record InfisicalLoginResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken);

    private sealed record InfisicalListSecretsResponse(
        [property: JsonPropertyName("secrets")] List<InfisicalRawSecret>? Secrets);

    private sealed record InfisicalRawSecret(
        [property: JsonPropertyName("secretKey")] string? SecretKey,
        [property: JsonPropertyName("secretValue")] string? SecretValue);

    /// <summary>
    /// Converts an Infisical secret key to .NET configuration format.
    /// </summary>
    private static string ConvertToConfigurationKey(string secretKey, string path)
    {
        var normalizedPath = path.Trim('/');

        // 1. Privacy Erasure Authority Database (/database/erasure) -> Database:Erasure:*
        if (normalizedPath.Equals("database/erasure", StringComparison.OrdinalIgnoreCase))
        {
            return secretKey.ToUpperInvariant() switch
            {
                "DATABASE_HOST" or "HOST" => "Database:Erasure:Host",
                "DATABASE_PORT" or "PORT" => "Database:Erasure:Port",
                "DATABASE_NAME" or "DATABASE" or "NAME" => "Database:Erasure:Database",
                "DATABASE_PROVIDER" or "PROVIDER" => "Database:Erasure:Provider",
                "DATABASE_RUNTIME_USERNAME" or "RUNTIME_USERNAME" => "Database:Erasure:Runtime:Username",
                "DATABASE_RUNTIME_PASSWORD" or "RUNTIME_PASSWORD" => "Database:Erasure:Runtime:Password",
                "DATABASE_MIGRATOR_USERNAME" or "MIGRATOR_USERNAME" => "Database:Erasure:Migrator:Username",
                "DATABASE_MIGRATOR_PASSWORD" or "MIGRATOR_PASSWORD" => "Database:Erasure:Migrator:Password",
                "DATABASE_TLS_MODE" or "TLS_MODE" => "Database:Erasure:TlsMode",
                "DATABASE_TRUST_SERVER_CERTIFICATE" or "TRUST_SERVER_CERTIFICATE" => "Database:Erasure:TrustServerCertificate",
                _ => $"Database:Erasure:{ToPascalCase(secretKey)}"
            };
        }

        // 2. Primary Database (/database)
        if (normalizedPath.Equals("database", StringComparison.OrdinalIgnoreCase))
        {
            return secretKey.ToUpperInvariant() switch
            {
                "DATABASE_PROVIDER" or "PROVIDER" => "Database:Provider",
                "DATABASE_HOST" or "HOST" => "Database:Host",
                "DATABASE_PORT" or "PORT" => "Database:Port",
                "DATABASE_NAME" or "DATABASE" or "NAME" => "Database:Name",
                "DATABASE_SCHEMA" or "SCHEMA" => "Database:Schema",
                "DATABASE_RUNTIME_USERNAME" or "RUNTIME_USERNAME" => "Database:Runtime:Username",
                "DATABASE_RUNTIME_PASSWORD" or "RUNTIME_PASSWORD" => "Database:Runtime:Password",
                "DATABASE_MIGRATOR_USERNAME" or "MIGRATOR_USERNAME" => "Database:Migrator:Username",
                "DATABASE_MIGRATOR_PASSWORD" or "MIGRATOR_PASSWORD" => "Database:Migrator:Password",
                "DATABASE_TLS_MODE" or "TLS_MODE" => "Database:TlsMode",
                "DATABASE_TRUST_SERVER_CERTIFICATE" or "TRUST_SERVER_CERTIFICATE" => "Database:TrustServerCertificate",
                "DATABASE_SERVER_FLAVOR" or "SERVER_FLAVOR" => "Database:ServerFlavor",
                "DATABASE_SERVER_VERSION" or "SERVER_VERSION" => "Database:ServerVersion",
                "ERASURE_TOPOLOGY" or "PRIVACY_ERASURE_AUTHORITY_TOPOLOGY" or "TOPOLOGY" => "PrivacyErasure:Authority:Topology",
                _ => $"Database:{ToPascalCase(secretKey)}"
            };
        }

        // 3. Special mappings for common patterns
        if (secretKey.Equals("AI_TOOL_PROPOSALS_ENABLED", StringComparison.OrdinalIgnoreCase))
        {
            return "AiProvider:ToolProposalsEnabled";
        }

        // 4. Default path to section conversion
        var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var section = pathSegments.Length == 0 ? string.Empty : string.Join(":", pathSegments.Select(ToPascalCase)) + ":";

        // Remove section prefix from key if present
        var keyWithoutSection = secretKey;
        var sectionUpper = section.TrimEnd(':').Replace(":", "_", StringComparison.Ordinal).ToUpperInvariant();
        if (!string.IsNullOrEmpty(sectionUpper) &&
            secretKey.StartsWith(sectionUpper + "_", StringComparison.OrdinalIgnoreCase))
        {
            keyWithoutSection = secretKey[(sectionUpper.Length + 1)..];
        }

        // Handle double underscore as subsection separator
        var parts = keyWithoutSection.Split("__", StringSplitOptions.RemoveEmptyEntries);
        var configParts = parts.Select(ToPascalCase);
        var configKey = string.Join(":", configParts);

        return section + configKey;
    }

    /// <summary>
    /// Converts SCREAMING_SNAKE_CASE to PascalCase.
    /// </summary>
    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var parts = input.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var pascalParts = parts.Select(part =>
        {
            if (part.Length == 0) return string.Empty;
            return char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
        });

        return string.Join("", pascalParts);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _reloadTimer?.Dispose();
        _disposed = true;
    }
}
