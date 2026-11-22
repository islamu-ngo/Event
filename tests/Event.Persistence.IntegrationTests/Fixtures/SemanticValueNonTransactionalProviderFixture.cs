using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Event.Testing;
using Explore.Persistence.Database;

namespace Event.Persistence.IntegrationTests.Fixtures;

public sealed class SemanticValueNonTransactionalProviderFixture : IAsyncDisposable
{
    private const ushort DatabasePort = 3306;
    private const string DatabaseName = "semantic_value_migration";
    private const string Username = "semantic_migrator";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private readonly string _password =
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly SemaphoreSlim _startup = new(1, 1);
    private readonly Dictionary<PrimaryDatabaseProvider, IContainer> _containers = new();

    public async Task<PrimaryDatabaseConnectionOptions> CreateOptionsAsync(
        PrimaryDatabaseProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (provider is not (PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        IContainer container;
        await _startup.WaitAsync(cancellationToken);
        try
        {
            if (!_containers.TryGetValue(provider, out container!))
            {
                container = BuildContainer(provider);
                _containers.Add(provider, container);

                using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupCts.CancelAfter(StartupTimeout);
                await container.StartAsync(startupCts.Token);
            }
        }
        finally
        {
            _startup.Release();
        }

        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = provider,
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(DatabasePort),
            Database = DatabaseName,
            Username = Username,
            Password = _password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
            ServerFlavor = provider == PrimaryDatabaseProvider.MariaDb
                ? PrimaryDatabaseServerFlavor.MariaDb
                : PrimaryDatabaseServerFlavor.MySql,
            ServerVersion = provider == PrimaryDatabaseProvider.MariaDb
                ? new Version(11, 4)
                : new Version(8, 4),
        };
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IContainer container in _containers.Values)
        {
            await container.DisposeAsync();
        }

        _startup.Dispose();
    }

    private IContainer BuildContainer(PrimaryDatabaseProvider provider)
    {
        ContainerBuilder builder = new ContainerBuilder(provider == PrimaryDatabaseProvider.MariaDb
                ? "mariadb:11.4.7"
                : "mysql:8.4.6")
            .WithPortBinding(DatabasePort, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(DatabasePort))
            .ApplyBoundedResourcePolicy(
                provider == PrimaryDatabaseProvider.MariaDb
                    ? ContainerServiceRole.MariaDb
                    : ContainerServiceRole.MySql);

        return provider == PrimaryDatabaseProvider.MariaDb
            ? builder
                .WithEnvironment("MARIADB_DATABASE", DatabaseName)
                .WithEnvironment("MARIADB_USER", Username)
                .WithEnvironment("MARIADB_PASSWORD", _password)
                .WithEnvironment("MARIADB_ROOT_PASSWORD", _password)
                .Build()
            : builder
                .WithEnvironment("MYSQL_DATABASE", DatabaseName)
                .WithEnvironment("MYSQL_USER", Username)
                .WithEnvironment("MYSQL_PASSWORD", _password)
                .WithEnvironment("MYSQL_ROOT_PASSWORD", _password)
                .Build();
    }
}
