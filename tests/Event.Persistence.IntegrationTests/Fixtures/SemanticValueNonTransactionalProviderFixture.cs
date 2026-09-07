using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Explore.Secrets.Database;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Fixtures;

public sealed class SemanticValueNonTransactionalProviderFixture
    : IAsyncInitializer, IAsyncDisposable
{
    private const ushort DatabasePort = 3306;
    private const string DatabaseName = "semantic_value_migration";
    private const string Username = "semantic_migrator";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private readonly string _password =
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private IContainer? _mariaDb;
    private IContainer? _mySql;

    public async Task InitializeAsync()
    {
        _mariaDb = BuildContainer(PrimaryDatabaseProvider.MariaDb);
        _mySql = BuildContainer(PrimaryDatabaseProvider.MySql);

        using var startupCts = new CancellationTokenSource(StartupTimeout);
        await Task.WhenAll(
            _mariaDb.StartAsync(startupCts.Token),
            _mySql.StartAsync(startupCts.Token));
    }

    public PrimaryDatabaseConnectionOptions CreateOptions(
        PrimaryDatabaseProvider provider)
    {
        IContainer container = provider switch
        {
            PrimaryDatabaseProvider.MariaDb => _mariaDb
                ?? throw new InvalidOperationException("MariaDB is not started."),
            PrimaryDatabaseProvider.MySql => _mySql
                ?? throw new InvalidOperationException("MySQL is not started."),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

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

    public async ValueTask DisposeAsync()
    {
        if (_mariaDb is not null)
        {
            await _mariaDb.DisposeAsync();
        }

        if (_mySql is not null)
        {
            await _mySql.DisposeAsync();
        }
    }

    private IContainer BuildContainer(PrimaryDatabaseProvider provider)
    {
        ContainerBuilder builder = new ContainerBuilder()
            .WithImage(provider == PrimaryDatabaseProvider.MariaDb
                ? "mariadb:11.4.7"
                : "mysql:8.4.6")
            .WithPortBinding(DatabasePort, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(DatabasePort));

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
