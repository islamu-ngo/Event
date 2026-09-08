using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Manages a Cerbos PDP container with the project's policy directory mounted.
/// Provides the gRPC and HTTP endpoints for authorization checks.
/// </summary>
public sealed class CerbosContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>Pinned Cerbos version for reproducible test results.</summary>
    private const string CerbosImage = "ghcr.io/cerbos/cerbos:0.53.0";

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    private IContainer _container = null!;

    /// <summary>
    /// The Cerbos gRPC endpoint (e.g., <c>http://localhost:{port}</c>).
    /// Use this for Cerbos SDK client connections.
    /// </summary>
    public string GrpcEndpoint { get; private set; } = string.Empty;

    /// <summary>
    /// The Cerbos HTTP endpoint for health checks and REST API access.
    /// </summary>
    public string HttpEndpoint { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var policiesPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "cerbos", "policies");

        if (!Directory.Exists(policiesPath))
        {
            throw new DirectoryNotFoundException(
                $"Cerbos policies directory not found at '{policiesPath}'. " +
                "Ensure cerbos/policies/** is included as Content with CopyToOutputDirectory=PreserveNewest.");
        }

        _container = new ContainerBuilder()
            .WithImage(CerbosImage)
            .WithPortBinding(3592, true) // HTTP
            .WithPortBinding(3593, true) // gRPC
            .WithResourceMapping(policiesPath, "/policies")
            .WithCommand("server", "--config=/config/.cerbos.yaml")
            .WithResourceMapping(CreateMinimalConfig(), "/config/.cerbos.yaml")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request =>
                    request
                        .ForPath("/_cerbos/health")
                        .ForPort(3592)
                        .ForStatusCode(System.Net.HttpStatusCode.OK),
                    wait => wait.WithTimeout(StartupTimeout)))
            .Build();

        using var startupCts = new CancellationTokenSource(StartupTimeout);
        await _container.StartAsync(startupCts.Token);

        var host = _container.Hostname;
        var httpPort = _container.GetMappedPublicPort(3592);
        var grpcPort = _container.GetMappedPublicPort(3593);

        HttpEndpoint = $"http://{host}:{httpPort}";
        GrpcEndpoint = $"http://{host}:{grpcPort}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a minimal Cerbos configuration that reads policies from the disk driver only.
    /// No PostgreSQL overlay needed for integration tests — we validate the policy files directly.
    /// </summary>
    private static byte[] CreateMinimalConfig()
    {
        const string config = """
            server:
              httpListenAddr: ":3592"
              grpcListenAddr: ":3593"
            storage:
              driver: "disk"
              disk:
                directory: /policies
                watchForChanges: false
            schema:
              enforcement: warn
            engine:
              lenientScopeSearch: true
            """;

        return System.Text.Encoding.UTF8.GetBytes(config);
    }
}
