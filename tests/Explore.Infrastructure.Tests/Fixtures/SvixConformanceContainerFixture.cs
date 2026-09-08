using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using Svix;
using TUnit.Core.Interfaces;

namespace Explore.Infrastructure.Tests.Fixtures;

public sealed class SvixConformanceContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private const ushort SvixPort = 8071;
    private const string JwtSecret = "islamu-conformance-svix-jwt-secret";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly INetwork _network = new NetworkBuilder().Build();
    private IContainer? _postgres;
    private IContainer? _redis;
    private IContainer? _svix;
    private string? _authToken;

    public string ServerUrl => _svix is null
        ? throw new InvalidOperationException("Svix container has not started.")
        : $"http://{_svix.Hostname}:{_svix.GetMappedPublicPort(SvixPort)}";

    public SvixClient CreateClient(string? authToken = null, string? serverUrl = null) =>
        new(
            authToken ?? _authToken ?? throw new InvalidOperationException("Svix auth token is unavailable."),
            new SvixOptions(
                serverUrl: serverUrl ?? ServerUrl,
                timeoutMilliseconds: 5_000,
                retryScheduleMilliseconds: []),
            NullLogger<SvixClient>.Instance);

    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        _postgres = new ContainerBuilder("postgres:13.4")
            .WithNetwork(_network)
            .WithNetworkAliases("svix-postgres")
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "postgres")
            .Build();
        _redis = new ContainerBuilder("redis:7.4-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases("svix-redis")
            .Build();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _svix = new ContainerBuilder(SvixConformanceProfileRegistry.SelfHostedImage)
            .WithNetwork(_network)
            .WithNetworkAliases("svix")
            .WithPortBinding(SvixPort, assignRandomHostPort: true)
            .WithEnvironment("WAIT_FOR", "true")
            .WithEnvironment("SVIX_DB_DSN", "postgresql://postgres:postgres@svix-postgres:5432/postgres")
            .WithEnvironment("SVIX_QUEUE_TYPE", "redis")
            .WithEnvironment("SVIX_CACHE_TYPE", "redis")
            .WithEnvironment("SVIX_REDIS_DSN", "redis://svix-redis:6379")
            .WithEnvironment("SVIX_JWT_SECRET", JwtSecret)
            .Build();
        await _svix.StartAsync();

        using var startupCts = new CancellationTokenSource(StartupTimeout);
        await WaitForHealthAsync(startupCts.Token);
        _authToken = await GenerateAuthTokenAsync(startupCts.Token);
    }

    public async Task<string> GetServerVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequireSvix().ExecAsync(
            ["/usr/local/bin/svix-server", "--version"],
            cancellationToken);
        EnsureSuccessful(result, "read Svix server version");
        return result.Stdout.Trim();
    }

    public async Task<string> RotateAuthTokenAsync(CancellationToken cancellationToken = default)
    {
        var currentToken = _authToken ??
            throw new InvalidOperationException("Svix auth token is unavailable.");
        var deadline = DateTime.UtcNow.AddSeconds(3);
        string rotatedToken;
        do
        {
            await Task.Delay(PollInterval, cancellationToken);
            rotatedToken = await GenerateAuthTokenAsync(cancellationToken);
        }
        while (string.Equals(rotatedToken, currentToken, StringComparison.Ordinal) &&
               DateTime.UtcNow < deadline);

        return string.Equals(rotatedToken, currentToken, StringComparison.Ordinal)
            ? throw new InvalidOperationException("Svix did not issue a distinct disposable auth token.")
            : rotatedToken;
    }

    public async Task ExpireIdempotencyCacheAsync(CancellationToken cancellationToken = default)
    {
        var result = await RequireRedis().ExecAsync(["redis-cli", "FLUSHDB"], cancellationToken);
        EnsureSuccessful(result, "expire Svix idempotency cache");
    }

    public async ValueTask DisposeAsync()
    {
        if (_svix is not null)
        {
            await _svix.DisposeAsync();
        }

        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        await _network.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<string> GenerateAuthTokenAsync(CancellationToken cancellationToken)
    {
        var result = await RequireSvix().ExecAsync(
            ["/usr/local/bin/svix-server", "jwt", "generate"],
            cancellationToken);
        EnsureSuccessful(result, "generate disposable Svix auth token");
        const string tokenPrefix = "Token (Bearer):";
        var output = result.Stdout.Trim();
        var token = output.StartsWith(tokenPrefix, StringComparison.Ordinal)
            ? output[tokenPrefix.Length..].Trim()
            : output;
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException("Svix returned an empty disposable auth token.")
            : token;
    }

    private async Task WaitForHealthAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = new Uri(ServerUrl) };
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync("/api/v1/health/", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        throw new TimeoutException("Pinned Svix conformance container did not become healthy.");
    }

    private IContainer RequireSvix() =>
        _svix ?? throw new InvalidOperationException("Svix container has not started.");

    private IContainer RequireRedis() =>
        _redis ?? throw new InvalidOperationException("Redis container has not started.");

    private static void EnsureSuccessful(ExecResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to {operation}. ExitCode={result.ExitCode}.");
        }
    }
}
