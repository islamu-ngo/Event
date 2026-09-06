// ABOUTME: Hosts real SQL Server, MariaDB, and MySQL engines for admission authority lock contracts.
// ABOUTME: Uses runtime-generated credentials and production provider composition without repository secrets.

using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Explore.Secrets.Database;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Fixtures;

public sealed class AdmissionAuthorityProviderFixture
    : IAsyncInitializer, IAsyncDisposable
{
    private const ushort SqlServerPort = 1433;
    private const ushort MySqlPort = 3306;
    private const string MySqlDatabase = "admission_authority";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private readonly string _password =
        "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private IContainer? _sqlServer;
    private IContainer? _mariaDb;
    private IContainer? _mySql;

    public async Task InitializeAsync()
    {
        _sqlServer = BuildSqlServer();
        _mariaDb = BuildMySqlFamily(PrimaryDatabaseProvider.MariaDb);
        _mySql = BuildMySqlFamily(PrimaryDatabaseProvider.MySql);

        using var startupCts = new CancellationTokenSource(StartupTimeout);
        await Task.WhenAll(
            _sqlServer.StartAsync(startupCts.Token),
            _mariaDb.StartAsync(startupCts.Token),
            _mySql.StartAsync(startupCts.Token));
    }

    public PrimaryDatabaseConnectionOptions CreateOptions(
        PrimaryDatabaseProvider provider,
        PrimaryDatabaseRole role = PrimaryDatabaseRole.Runtime)
    {
        IContainer container = provider switch
        {
            PrimaryDatabaseProvider.SqlServer => _sqlServer
                ?? throw new InvalidOperationException("SQL Server is not started."),
            PrimaryDatabaseProvider.MariaDb => _mariaDb
                ?? throw new InvalidOperationException("MariaDB is not started."),
            PrimaryDatabaseProvider.MySql => _mySql
                ?? throw new InvalidOperationException("MySQL is not started."),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        return new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = provider,
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(
                provider == PrimaryDatabaseProvider.SqlServer
                    ? SqlServerPort
                    : MySqlPort),
            Database = provider == PrimaryDatabaseProvider.SqlServer
                ? "master"
                : MySqlDatabase,
            Username = provider == PrimaryDatabaseProvider.SqlServer
                ? "sa"
                : "admission_user",
            Password = _password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
            ServerFlavor = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                _ => null,
            },
            ServerVersion = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                PrimaryDatabaseProvider.MySql => new Version(8, 4),
                _ => null,
            },
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_sqlServer is not null)
        {
            await _sqlServer.DisposeAsync();
        }
        if (_mariaDb is not null)
        {
            await _mariaDb.DisposeAsync();
        }
        if (_mySql is not null)
        {
            await _mySql.DisposeAsync();
        }
    }

    private IContainer BuildSqlServer() =>
        new ContainerBuilder()
            .WithImage(
                "mcr.microsoft.com/mssql/server@sha256:" +
                "ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89")
            .WithPortBinding(SqlServerPort, assignRandomHostPort: true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "1024")
            .WithEnvironment("MSSQL_SA_PASSWORD", _password)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(SqlServerPort))
            .Build();

    private IContainer BuildMySqlFamily(PrimaryDatabaseProvider provider)
    {
        ContainerBuilder builder = new ContainerBuilder()
            .WithImage(provider == PrimaryDatabaseProvider.MariaDb
                ? "mariadb:11.4.7"
                : "mysql:8.4.6")
            .WithCommand("--performance-schema=OFF", "--innodb-buffer-pool-size=64M")
            .WithPortBinding(MySqlPort, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(MySqlPort));

        return provider == PrimaryDatabaseProvider.MariaDb
            ? builder
                .WithEnvironment("MARIADB_DATABASE", MySqlDatabase)
                .WithEnvironment("MARIADB_USER", "admission_user")
                .WithEnvironment("MARIADB_PASSWORD", _password)
                .WithEnvironment("MARIADB_ROOT_PASSWORD", _password)
                .Build()
            : builder
                .WithEnvironment("MYSQL_DATABASE", MySqlDatabase)
                .WithEnvironment("MYSQL_USER", "admission_user")
                .WithEnvironment("MYSQL_PASSWORD", _password)
                .WithEnvironment("MYSQL_ROOT_PASSWORD", _password)
                .Build();
    }
}
