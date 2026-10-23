using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Api;
using Event.Benchmarks.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
[BenchmarkCategory("EFInMemory", "TestServer", "OutputCacheEnabled", "AllowAllAuthorization", "WorkersDisabled")]
public class ApiEndpointBenchmarks : IAsyncDisposable
{
    private ApiBenchmarkHostFactory _factory = null!;
    private HttpClient _client = null!;
    private string _authenticatedHeader = string.Empty;

    [ParamsSource(nameof(Scenarios))]
    public ApiBenchmarkScenario Scenario { get; set; } = ApiBenchmarkScenario.Empty;

    public IEnumerable<ApiBenchmarkScenario> Scenarios => ApiBenchmarkScenario.ReadScenarios;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        var seedStarted = DateTimeOffset.UtcNow;
        _factory = new ApiBenchmarkHostFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _authenticatedHeader = ApiBenchmarkAuthHandler.CreateUserHeaderValue(Guid.Parse("018f6d10-7b7b-7f20-8c61-3c3e7f1b6a11"));
        Console.WriteLine($"benchmark_dataset: profile=InMemory events=24 seed_clock_start={seedStarted:O} seed_clock_end={DateTimeOffset.UtcNow:O}");
        await ApiBenchmarkScenario.WarmUpAndValidateAsync(_client, _authenticatedHeader);
    }

    [Benchmark(Description = "GET: EF InMemory / TestServer / output cache enabled")]
    public Task<ApiBenchmarkResult> Get_Endpoint()
    {
        return SendAsync(Scenario);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            _client.Dispose();
        }

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<ApiBenchmarkResult> SendAsync(ApiBenchmarkScenario scenario)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, scenario.Path);

        if (scenario.UseMinimalResponse)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        }

        if (scenario.RequiresAuthentication)
        {
            request.Headers.TryAddWithoutValidation(ApiBenchmarkAuthHandler.AuthHeaderName, _authenticatedHeader);
        }

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsByteArrayAsync();

        return new ApiBenchmarkResult(
            scenario.Name,
            (int)response.StatusCode,
            content.Length);
    }
}

public sealed record ApiBenchmarkScenario(
    string Name,
    string Path,
    bool RequiresAuthentication,
    bool UseMinimalResponse)
{
    public static ApiBenchmarkScenario Empty { get; } = new("unset", "/", false, true);

    public static IReadOnlyList<ApiBenchmarkScenario> ReadScenarios { get; } =
    [
        new("event-list-minimal", "/api/event?pageSize=20", false, true),
        new("event-list-hal", "/api/event?pageSize=20", false, false),
        new("lookup-category-types", "/api/categorytype/with-categories", false, true),
        new("system-onboarding-status", "/api/system/onboarding-status", false, true),
        new("my-events-minimal", "/api/event/my?pageNumber=1&pageSize=20", true, true),
        new("event-creation-context", "/api/event/creation-context", true, true)
    ];

    public override string ToString() => Name;

    internal static async Task WarmUpAndValidateAsync(HttpClient client, string authenticatedHeader)
    {
        foreach (var scenario in ReadScenarios)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, scenario.Path);
            if (scenario.UseMinimalResponse)
                request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            if (scenario.RequiresAuthentication)
                request.Headers.TryAddWithoutValidation(ApiBenchmarkAuthHandler.AuthHeaderName, authenticatedHeader);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            var root = body.RootElement;
            bool valid = scenario.Name switch
            {
                "event-list-minimal" or "event-list-hal" => root.GetProperty("snapshotCount").GetInt32() == 24
                    && ValidEventPage(root, discovery: true),
                "my-events-minimal" => root.GetProperty("totalCount").GetInt32() == 24
                    && ValidEventPage(root, discovery: false),
                "lookup-category-types" => root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                    && root.EnumerateArray().All(row => !string.IsNullOrWhiteSpace(row.GetProperty("fullName").GetString())
                        && row.GetProperty("categories").ValueKind == JsonValueKind.Array),
                "system-onboarding-status" => root.GetProperty("requiresOnboarding").ValueKind is JsonValueKind.True or JsonValueKind.False
                    && root.GetProperty("deploymentMode").GetString() == "SingleTenant",
                "event-creation-context" => root.GetProperty("canCreate").ValueKind is JsonValueKind.True or JsonValueKind.False
                    && root.GetProperty("publisherOptions").ValueKind == JsonValueKind.Array,
                _ => false
            };
            if (!valid)
                throw new InvalidOperationException("benchmark_public_response_invariant_failed");
        }
    }

    private static bool ValidEventPage(JsonElement root, bool discovery)
    {
        var items = root.GetProperty("_embedded").GetProperty("items");
        if (items.GetArrayLength() != 20)
            return false;
        var ids = new HashSet<Guid>();
        foreach (var item in items.EnumerateArray())
        {
            var row = discovery ? item.GetProperty("event") : item;
            Guid id = row.GetProperty("id").GetGuid();
            if (id == Guid.Empty || !ids.Add(id) ||
                row.GetProperty("slug").GetString()?.StartsWith("benchmark-api-event-", StringComparison.Ordinal) != true)
                return false;
        }
        return true;
    }
}

public readonly record struct ApiBenchmarkResult(
    string Scenario,
    int StatusCode,
    int ResponseBytes);
