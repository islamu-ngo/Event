using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.ServiceDefaults.DataProtection;

public interface IExternalDataProtectionSource
{
    ExternalDataProtectionInput Read();
}

/// <summary>
/// Reads only the selected partition's two exact authority roots. Deployment pins are deliberately
/// not read from the ring authority or from ordinary application configuration.
/// </summary>
public sealed class ExternalDataProtectionSource(
    IConfiguration configuration, IHostEnvironment environment, ExternalDataProtectionPartition partition)
    : IExternalDataProtectionSource
{
    public ExternalDataProtectionInput Read()
    {
        try
        {
            string prefix = ExternalDataProtectionRegistration.EnvironmentPrefix(partition.Name);
            string pin = Required(Environment.GetEnvironmentVariable(prefix + "_ADMISSION_SHA256"));
            if (!long.TryParse(Environment.GetEnvironmentVariable(prefix + "_MINIMUM_GENERATION"),
                    NumberStyles.None, CultureInfo.InvariantCulture, out long floor) || floor < 1)
                throw ExternalDataProtectionRingAdmission.Invalid();
            string? selected = configuration["SecretProvider:Provider"] ?? configuration["SECRET_PROVIDER"];
            string ring;
            string admission;
            if (string.Equals(selected, "Environment", StringComparison.OrdinalIgnoreCase))
            {
                ring = Required(Environment.GetEnvironmentVariable(prefix + "_RING"));
                admission = Required(Environment.GetEnvironmentVariable(prefix + "_ADMISSION"));
            }
            else if (string.Equals(selected, "UserSecrets", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsLocal) throw ExternalDataProtectionRingAdmission.Invalid();
                var authority = new ConfigurationBuilder()
                    .AddUserSecrets("event-shared-secrets", reloadOnChange: false).Build();
                using var authorityLifetime = (IDisposable)authority;
                ring = Required(authority[prefix + "_RING"]);
                admission = Required(authority[prefix + "_ADMISSION"]);
            }
            else if (string.Equals(selected, "Infisical", StringComparison.OrdinalIgnoreCase))
            {
                (ring, admission) = ReadInfisical(prefix);
            }
            else
                throw ExternalDataProtectionRingAdmission.Invalid();
            return new(ring, admission, pin, floor);
        }
        catch
        {
            // Never attach upstream exceptions: they may include a body, URL, or secret value.
            throw new InvalidOperationException("data_protection_authority_unavailable");
        }
    }

    private bool IsLocal => environment.IsDevelopment() || environment.IsEnvironment("Testing");

    private (string Ring, string Admission) ReadInfisical(string prefix)
    {
        string Bootstrap(string name, string flatName)
        {
            string? value = Environment.GetEnvironmentVariable("SecretProvider__Infisical__" + name)
                ?? Environment.GetEnvironmentVariable(flatName);
            if (string.IsNullOrWhiteSpace(value) && IsLocal)
            {
                var local = new ConfigurationBuilder().AddUserSecrets("event-shared-secrets", reloadOnChange: false).Build();
                using var localLifetime = (IDisposable)local;
                value = local["SecretProvider:Infisical:" + name] ?? local["Infisical:" + name] ?? local[flatName];
            }
            return Required(value);
        }

        var origin = new Uri(Bootstrap("Url", "INFISICAL_URL"), UriKind.Absolute);
        if ((!origin.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                && !(IsLocal && origin.IsLoopback && origin.Scheme == "http"))
            || !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment) || origin.AbsolutePath != "/")
            throw ExternalDataProtectionRingAdmission.Invalid();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var http = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(15) };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/universal-auth/login")
        {
            Content = JsonContent.Create(new
            {
                clientId = Bootstrap("ClientId", "INFISICAL_CLIENT_ID"),
                clientSecret = Bootstrap("ClientSecret", "INFISICAL_CLIENT_SECRET")
            })
        };
        using var login = SendBounded(http, loginRequest, 16 * 1024, deadline.Token);
        string token = Required(login.RootElement.GetProperty("accessToken").GetString());
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string project = Bootstrap("ProjectId", "INFISICAL_PROJECT_ID");
        string stage = Bootstrap("Environment", "INFISICAL_ENV");
        string ReadRoot(string path, string name)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"api/v3/secrets/raw?workspaceId={Uri.EscapeDataString(project)}"
                + $"&environment={Uri.EscapeDataString(stage)}&secretPath={Uri.EscapeDataString(path)}"
                + "&expandSecretReferences=false&recursive=false");
            using var response = SendBounded(http, request, 256 * 1024, deadline.Token);
            var secrets = response.RootElement.GetProperty("secrets");
            if (secrets.ValueKind != JsonValueKind.Array || secrets.GetArrayLength() != 1)
                throw ExternalDataProtectionRingAdmission.Invalid();
            var secret = secrets[0];
            if (secret.GetProperty("secretPath").GetString() != path
                || secret.GetProperty("secretKey").GetString() != name)
                throw ExternalDataProtectionRingAdmission.Invalid();
            return Required(secret.GetProperty("secretValue").GetString());
        }
        return (ReadRoot($"/runtime/{partition.Name}/data-protection", prefix + "_RING"),
            ReadRoot($"/admission/{partition.Name}/data-protection", prefix + "_ADMISSION"));
    }

    private static JsonDocument SendBounded(HttpClient client, HttpRequestMessage request, int limit, CancellationToken token)
    {
        using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > limit
            || response.Content.Headers.ContentEncoding.Count != 0)
            throw ExternalDataProtectionRingAdmission.Invalid();
        using var stream = response.Content.ReadAsStream(token);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)bytes.Length)), token)
                .AsTask().GetAwaiter().GetResult();
            if (read == 0) break;
            bytes.Write(buffer, 0, read);
            if (bytes.Length > limit) throw ExternalDataProtectionRingAdmission.Invalid();
        }
        return JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
    }

    private static string Required(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 32 * 1024
            || value.Contains("${", StringComparison.Ordinal))
            throw ExternalDataProtectionRingAdmission.Invalid();
        return value;
    }
}
