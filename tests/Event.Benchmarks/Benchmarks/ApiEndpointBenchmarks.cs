using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Api;
using Event.Benchmarks.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
public class ApiEndpointBenchmarks : IAsyncDisposable
{
    private ApiBenchmarkHostFactory _factory = null!;
    private HttpClient _client = null!;
    private string _authenticatedHeader = string.Empty;

    [ParamsSource(nameof(Scenarios))]
    public ApiBenchmarkScenario Scenario { get; set; } = ApiBenchmarkScenario.Empty;

    public IEnumerable<ApiBenchmarkScenario> Scenarios => ApiBenchmarkScenario.ReadScenarios;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _factory = new ApiBenchmarkHostFactory();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _authenticatedHeader = ApiBenchmarkAuthHandler.CreateUserHeaderValue(Guid.Parse("018f6d10-7b7b-7f20-8c61-3c3e7f1b6a11"));
    }

    [Benchmark]
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
        new("event-list-minimal", "/api/event?pageNumber=1&pageSize=20", false, true),
        new("event-list-hal", "/api/event?pageNumber=1&pageSize=20", false, false),
        new("lookup-category-types", "/api/categorytype/with-categories", false, true),
        new("system-onboarding-status", "/api/system/onboarding-status", false, true),
        new("my-events-minimal", "/api/event/my?pageNumber=1&pageSize=20", true, true),
        new("event-creation-context", "/api/event/creation-context", true, true)
    ];

    public override string ToString() => Name;
}

public readonly record struct ApiBenchmarkResult(
    string Scenario,
    int StatusCode,
    int ResponseBytes);
