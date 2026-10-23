using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using Event.Benchmarks.Api;
using Event.Benchmarks.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Event.Benchmarks.Benchmarks;

[Config(typeof(ExploreBenchmarkConfig))]
[BenchmarkCategory("PostgreSql18", "TestServer", "OutputCacheDisabled", "AllowAllAuthorization", "WorkersDisabled", "CurrentModelSchema", "NoMigrations", "PurposeIsolationNotMeasured")]
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
        var login = BenchmarkDatabaseConfiguration.ReadPostgreSqlLogin();
        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("explore_benchmark_api")
            .WithUsername(login.Username)
            .WithPassword(login.Password)
            .Build();
        await _container.StartAsync();
        var seedStarted = DateTimeOffset.UtcNow;
        _factory = new PostgreSqlApiBenchmarkHostFactory(_container.GetConnectionString());
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _authenticatedHeader = ApiBenchmarkAuthHandler.CreateUserHeaderValue(BenchmarkApiSeedData.BenchmarkUserId);
        Console.WriteLine($"benchmark_dataset: profile=PostgreSql events=24 seed_clock_start={seedStarted:O} seed_clock_end={DateTimeOffset.UtcNow:O}");
        await ApiBenchmarkScenario.WarmUpAndValidateAsync(_client, _authenticatedHeader);
    }

    [Benchmark(Description = "GET: PostgreSQL 18 / TestServer / output cache disabled")]
    public Task<ApiBenchmarkResult> Get_Endpoint_PostgreSql() => SendAsync(Scenario);

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
        try { await factory.DisposeAsync(); }
        catch (ChannelClosedException) { Console.Error.WriteLine("benchmark_factory_teardown_channel_closed"); }
        catch (NullReferenceException) { Console.Error.WriteLine("benchmark_factory_teardown_null_reference"); }
        catch (ObjectDisposedException) { Console.Error.WriteLine("benchmark_factory_teardown_disposed"); }
    }

    public ValueTask DisposeAsync() => new(GlobalCleanup());

    private async Task<ApiBenchmarkResult> SendAsync(ApiBenchmarkScenario scenario)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, scenario.Path);
        if (scenario.UseMinimalResponse)
            request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        if (scenario.RequiresAuthentication)
            request.Headers.TryAddWithoutValidation(ApiBenchmarkAuthHandler.AuthHeaderName, _authenticatedHeader);
        var client = _client ?? throw new InvalidOperationException("Benchmark client has not been initialized.");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync();
        return new ApiBenchmarkResult(scenario.Name, (int)response.StatusCode, content.Length);
    }
}
