using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Explore.Secrets.Database;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>Starts only the engine selected by a migration-safety test slice.</summary>
public sealed class MySqlMigrationGuardFixture : IAsyncDisposable
{
    private const ushort DatabasePort = 3306;
    private const string DatabaseName = "varchar_narrowing";
    private const string Username = "narrowing_migrator";
    private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly SemaphoreSlim _startup = new(1, 1);
    private readonly Dictionary<PrimaryDatabaseProvider, IContainer> _containers = [];

    public async Task<PrimaryDatabaseConnectionOptions> CreateOptionsAsync(
        PrimaryDatabaseProvider provider)
    {
        if (provider is not (PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb))
            throw new ArgumentOutOfRangeException(nameof(provider));

        await _startup.WaitAsync();
        try
        {
            if (!_containers.TryGetValue(provider, out IContainer? container))
            {
                bool mariaDb = provider == PrimaryDatabaseProvider.MariaDb;
                string prefix = mariaDb ? "MARIADB" : "MYSQL";
                container = new ContainerBuilder(mariaDb ? "mariadb:11.4.7" : "mysql:8.4.6")
                    .WithPortBinding(DatabasePort, assignRandomHostPort: true)
                    .WithEnvironment(prefix + "_DATABASE", DatabaseName)
                    .WithEnvironment(prefix + "_USER", Username)
                    .WithEnvironment(prefix + "_PASSWORD", _password)
                    .WithEnvironment(prefix + "_ROOT_PASSWORD", _password)
                    .WithWaitStrategy(Wait.ForUnixContainer()
                        .UntilInternalTcpPortIsAvailable(DatabasePort))
                    .Build();
                _containers.Add(provider, container);
                using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await container.StartAsync(startup.Token);
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
                    : new Version(8, 4)
            };
        }
        finally
        {
            _startup.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IContainer container in _containers.Values)
            await container.DisposeAsync();
        _startup.Dispose();
    }
}
