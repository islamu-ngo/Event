using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Api;
using Event.Benchmarks.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
public class ApiEndpointPostgreSqlBenchmarks : IAsyncDisposable
{
    private PostgreSqlContainer? _container;
    private PostgreSqlApiBenchmarkHostFactory? _factory;
    private HttpClient? _client;
    private string _authenticatedHeader = string.Empty;

    [ParamsSource(nameof(Scenarios))]
    public ApiBenchmarkScenario Scenario { get; set; } = ApiBenchmarkScenario.Empty;

    public IEnumerable<ApiBenchmarkScenario> Scenarios => ApiBenchmarkScenario.ReadScenarios;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("explore_benchmark_api")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();

        _factory = new PostgreSqlApiBenchmarkHostFactory(_container.GetConnectionString());
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _authenticatedHeader = ApiBenchmarkAuthHandler.CreateUserHeaderValue(BenchmarkApiSeedData.BenchmarkUserId);
    }

    [Benchmark]
    public Task<ApiBenchmarkResult> Get_Endpoint_PostgreSql()
    {
        return SendAsync(Scenario);
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        _client?.Dispose();
        _client = null;

        if (_factory is not null)
        {
            await DisposeFactoryAsync(_factory);
            _factory = null;
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }

    private static async ValueTask DisposeFactoryAsync(PostgreSqlApiBenchmarkHostFactory factory)
    {
        try
        {
            await factory.DisposeAsync();
        }
        catch (ChannelClosedException exception)
        {
            Console.Error.WriteLine($"Ignoring WebApplicationFactory teardown ChannelClosedException: {exception.Message}");
        }
        catch (NullReferenceException exception)
        {
            Console.Error.WriteLine($"Ignoring WebApplicationFactory teardown NullReferenceException: {exception.Message}");
        }
        catch (ObjectDisposedException exception)
        {
            Console.Error.WriteLine($"Ignoring WebApplicationFactory teardown ObjectDisposedException: {exception.Message}");
        }
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(GlobalCleanup());
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

        var client = _client ?? throw new InvalidOperationException("Benchmark client has not been initialized.");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsByteArrayAsync();

        return new ApiBenchmarkResult(
            scenario.Name,
            (int)response.StatusCode,
            content.Length);
    }
}
