namespace Explore.Blazor.Configuration;

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

public sealed class InfisicalConfigurationProvider(InfisicalConfigurationSource source)
    : ConfigurationProvider
{
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
            if (source.ThrowOnFirstLoadFailure)
            {
                throw new InvalidOperationException(
                    "secret_authority_unavailable: Blazor startup loading failed. "
                    + "Verify the deployment-owned authority and retry.");
            }

            Console.Error.WriteLine(
                "[Infisical] secret_authority_unavailable; verify authority configuration and retry.");
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
        using var handler = CreateIpv4Handler();
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        var baseUrl = source.Url.TrimEnd('/');
        var loginResponse = http.PostAsJsonAsync(
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
                + "&expandSecretReferences=true&recursive=true";

            var response = http.GetAsync(requestUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw ProviderFailure(response.StatusCode);
            }

            var payload = response.Content
                .ReadFromJsonAsync<InfisicalListSecretsResponse>()
                .GetAwaiter()
                .GetResult();

            foreach (var secret in payload?.Secrets ?? [])
            {
                if (string.IsNullOrEmpty(secret.SecretKey))
                {
                    continue;
                }

                data[ConvertToConfigurationKey(secret.SecretKey, path)] = secret.SecretValue ?? string.Empty;
                data[secret.SecretKey] = secret.SecretValue ?? string.Empty;
            }
        }

        return data;
    }

    private static InvalidOperationException ProviderFailure(HttpStatusCode statusCode) =>
        new(statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? "secret_authority_unauthorized"
            : "secret_authority_unavailable");

    private static bool IsBoundedReasonCode(string message) => message is
        "secret_authority_unauthorized" or
        "secret_authority_unavailable" or
        "secret_authority_invalid";

    private static string ConvertToConfigurationKey(string secretKey, string path)
    {
        if (secretKey.Equals("AI_TOOL_PROPOSALS_ENABLED", StringComparison.OrdinalIgnoreCase))
        {
            return "AiProvider:ToolProposalsEnabled";
        }

        var sectionName = path.Trim('/');
        var section = string.IsNullOrEmpty(sectionName)
            ? string.Empty
            : ToPascalCase(sectionName) + ":";
        var sectionPrefix = section.TrimEnd(':').ToUpperInvariant();
        var key = !string.IsNullOrEmpty(sectionPrefix)
            && secretKey.StartsWith(sectionPrefix + "_", StringComparison.OrdinalIgnoreCase)
                ? secretKey[(sectionPrefix.Length + 1)..]
                : secretKey;

        return section + string.Join(
            ":",
            key.Split("__", StringSplitOptions.RemoveEmptyEntries).Select(ToPascalCase));
    }

    private static string ToPascalCase(string value) => string.Concat(
        value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

    private sealed record InfisicalLoginResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken);

    private sealed record InfisicalListSecretsResponse(
        [property: JsonPropertyName("secrets")] List<InfisicalRawSecret>? Secrets);

    private sealed record InfisicalRawSecret(
        [property: JsonPropertyName("secretKey")] string? SecretKey,
        [property: JsonPropertyName("secretValue")] string? SecretValue);
}
