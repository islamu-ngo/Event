namespace Explore.Blazor.Configuration;

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

public sealed class InfisicalConfigurationProvider(InfisicalConfigurationSource source)
    : ConfigurationProvider
{
    private static readonly string[] FrontendRoots =
        ["/runtime/bff/oidc/keycloak", "/runtime/bff/oidc/google", "/runtime/bff/atproto/oauth"];

    public override void Load()
    {
        try
        {
            Data = LoadSecrets();
        }
        catch (InvalidOperationException exception) when (IsBoundedReasonCode(exception.Message))
        {
            throw new InvalidOperationException(
                $"{exception.Message}: Blazor startup loading failed. "
                + "Verify the deployment-owned authority and retry.");
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "secret_authority_unavailable: Blazor startup loading failed. "
                + "Verify the deployment-owned authority and retry.");
        }
    }

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
            {
                NoDelay = true,
            };

            try
            {
                await socket.ConnectAsync(
                    addresses[0],
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

    private Dictionary<string, string?> LoadSecrets()
    {
        if (source.Paths.Count == 0 || source.Paths.Any(path => !IsFrontendPath(path)))
        {
            throw new InvalidOperationException("bff_secret_scope_invalid");
        }

        using var handler = source.MessageHandlerFactory is { } factory
            ? factory()
            : CreateIpv4Handler();
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        var baseUrl = source.Url.TrimEnd('/');
        using var loginResponse = http.PostAsJsonAsync(
                $"{baseUrl}/api/v1/auth/universal-auth/login",
                new { clientId = source.ClientId, clientSecret = source.ClientSecret })
            .GetAwaiter()
            .GetResult();

        if (!loginResponse.IsSuccessStatusCode)
        {
            throw ProviderFailure(loginResponse.StatusCode);
        }

        var login = loginResponse.Content
            .ReadFromJsonAsync<InfisicalLoginResponse>()
            .GetAwaiter()
            .GetResult();

        if (string.IsNullOrEmpty(login?.AccessToken))
        {
            throw new InvalidOperationException("secret_authority_invalid");
        }

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.AccessToken);

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in source.Paths)
        {
            var requestUrl =
                $"{baseUrl}/api/v3/secrets/raw"
                + $"?workspaceId={Uri.EscapeDataString(source.ProjectId)}"
                + $"&environment={Uri.EscapeDataString(source.Environment)}"
                + $"&secretPath={Uri.EscapeDataString(path)}"
                + "&expandSecretReferences=false&recursive=false";

            using var response = http.GetAsync(requestUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw ProviderFailure(response.StatusCode);
            }

            var payload = response.Content
                .ReadFromJsonAsync<InfisicalListSecretsResponse>()
                .GetAwaiter()
                .GetResult();

            if (payload?.Secrets is null)
                throw new InvalidOperationException("secret_authority_invalid");

            foreach (var secret in payload.Secrets)
            {
                if (string.IsNullOrEmpty(secret.SecretKey))
                {
                    throw new InvalidOperationException("secret_authority_invalid");
                }

                var secretPath = ValidateSecretPath(secret.SecretPath, path);
                if (ContainsDatabaseConfiguration(secret.SecretKey) || ContainsDatabaseConfiguration(secretPath))
                {
                    throw new InvalidOperationException("bff_database_configuration_forbidden");
                }
                if (secret.SecretValue is null || secret.SecretValue.Contains("${", StringComparison.Ordinal))
                    throw new InvalidOperationException("secret_authority_invalid");

                string configurationKey = ConvertToConfigurationKey(secret.SecretKey, secretPath);
                if (!data.TryAdd(configurationKey, secret.SecretValue))
                    throw new InvalidOperationException("secret_authority_invalid");
            }
        }

        return data;
    }

    private static bool IsFrontendPath(string path)
    {
        return FrontendRoots.Contains(path, StringComparer.Ordinal);
    }

    private static bool ContainsDatabaseConfiguration(string key) =>
        key.Split([':', '_', '/'], StringSplitOptions.RemoveEmptyEntries).Any(part =>
            part.Equals("Database", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Db", StringComparison.OrdinalIgnoreCase)
            || part.Equals("IdentityDatabase", StringComparison.OrdinalIgnoreCase)
            || part.Equals("DatabaseErasure", StringComparison.OrdinalIgnoreCase)
            || part.Equals("PrivacyErasureAuthorityDatabase", StringComparison.OrdinalIgnoreCase)
            || part.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Postgresql", StringComparison.OrdinalIgnoreCase));

    private static string ValidateSecretPath(string? secretPath, string requestedPath)
    {
        if (!string.Equals(secretPath, requestedPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("secret_authority_invalid");
        }
        return secretPath!;
    }

    private static InvalidOperationException ProviderFailure(HttpStatusCode statusCode) =>
        new(statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "secret_authority_unauthorized"
            : "secret_authority_unavailable");

    private static bool IsBoundedReasonCode(string message) => message is
        "secret_authority_unauthorized" or
        "secret_authority_unavailable" or
        "secret_authority_invalid" or
        "bff_secret_scope_invalid" or
        "bff_database_configuration_forbidden";

    private static string ConvertToConfigurationKey(string secretKey, string path) => (path, secretKey) switch
    {
        ("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT") => "Keycloak:Endpoint",
        ("/runtime/bff/oidc/keycloak", "KEYCLOAK_REALM") => "Keycloak:Realm",
        ("/runtime/bff/oidc/keycloak", "KEYCLOAK_CLIENT_ID") => "Keycloak:ClientId",
        ("/runtime/bff/oidc/keycloak", "KEYCLOAK_BLAZOR_CLIENT_SECRET") => "Keycloak:BlazorClientSecret",
        ("/runtime/bff/oidc/google", "GOOGLE_CLIENT_ID") => "Google:ClientId",
        ("/runtime/bff/oidc/google", "GOOGLE_CLIENT_SECRET") => "Google:ClientSecret",
        ("/runtime/bff/atproto/oauth", "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS") => "Atproto:OauthClientPrivateJwks",
        _ => throw new InvalidOperationException("secret_authority_invalid"),
    };

    private sealed record InfisicalLoginResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken);

    private sealed record InfisicalListSecretsResponse(
        [property: JsonPropertyName("secrets")] List<InfisicalRawSecret>? Secrets);

    private sealed record InfisicalRawSecret(
        [property: JsonPropertyName("secretKey")] string? SecretKey,
        [property: JsonPropertyName("secretValue")] string? SecretValue,
        [property: JsonPropertyName("secretPath")] string? SecretPath);
}
