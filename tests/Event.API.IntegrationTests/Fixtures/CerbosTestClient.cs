using System.Net.Http.Json;
using System.Text.Json;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Reusable HTTP client for Cerbos PDP check resource requests.
/// Used by CerbosPolicyContractTests, CerbosPolicyCompilationTests,
/// and AuthorizationPipelineIntegrationTests to avoid duplicate payload code.
/// </summary>
public sealed class CerbosTestClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public CerbosTestClient(string cerbosHttpEndpoint)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(cerbosHttpEndpoint) };
    }

    /// <summary>
    /// Sends a check resource request with the given principal and a single resource.
    /// </summary>
    public async Task<Dictionary<string, string>> CheckResourceAsync(
        string principalId,
        string[] principalRoles,
        object principalAttrs,
        string resourceKind,
        string resourceId,
        object resourceAttrs,
        string[] actions,
        bool includeMeta = false)
    {
        var payload = new
        {
            requestId = Guid.NewGuid().ToString(),
            includeMeta,
            principal = new { id = principalId, roles = principalRoles, attr = principalAttrs },
            resources = new[]
            {
                new
                {
                    resource = new { kind = resourceKind, id = resourceId, attr = resourceAttrs },
                    actions
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync("/api/check/resources", payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        response.EnsureSuccessStatusCode();

        return await ParseActionsFromResponseAsync(response);
    }

    /// <summary>
    /// Sends a health check request to the Cerbos container.
    /// </summary>
    public async Task<HttpResponseMessage> HealthAsync()
    {
        return await _httpClient.GetAsync("/_cerbos/health");
    }

    /// <summary>
    /// Sends a GET request to the Cerbos OpenAPI schema endpoint.
    /// </summary>
    public async Task<HttpResponseMessage> GetSchemaAsync()
    {
        return await _httpClient.GetAsync("/schema/swagger.json");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static async Task<Dictionary<string, string>> ParseActionsFromResponseAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        var firstResult = results.EnumerateArray().First();
        var actions = firstResult.GetProperty("actions");

        var map = new Dictionary<string, string>();
        foreach (var prop in actions.EnumerateObject())
        {
            map[prop.Name] = prop.Value.GetString() ?? "UNKNOWN";
        }

        return map;
    }
}
