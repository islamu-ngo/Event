using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Event.Testing;
using Explore.Persistence.Database;

namespace Event.Persistence.IntegrationTests.Fixtures;

public sealed class AdmissionAuthorityProviderFixture : IAsyncDisposable
{
    private const ushort SqlServerPort = 1433;
    private const ushort MySqlPort = 3306;
    private const string MySqlDatabase = "admission_authority";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private readonly string _password =
        "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly SemaphoreSlim _startup = new(1, 1);
    private readonly Dictionary<PrimaryDatabaseProvider, IContainer> _containers = new();

    public async Task<PrimaryDatabaseConnectionOptions> CreateOptionsAsync(
        PrimaryDatabaseProvider provider,
        PrimaryDatabaseRole role = PrimaryDatabaseRole.Runtime,
        CancellationToken cancellationToken = default)
    {
        if (provider is not (PrimaryDatabaseProvider.SqlServer
            or PrimaryDatabaseProvider.MariaDb
            or PrimaryDatabaseProvider.MySql))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        IContainer container;
        await _startup.WaitAsync(cancellationToken);
        try
        {
            if (!_containers.TryGetValue(provider, out container!))
            {
                container = provider == PrimaryDatabaseProvider.SqlServer
                    ? BuildSqlServer()
                    : BuildMySqlFamily(provider);
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
        foreach (IContainer container in _containers.Values)
        {
            await container.DisposeAsync();
        }

        _startup.Dispose();
    }

    private IContainer BuildSqlServer() =>
        new ContainerBuilder(
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
            .ApplyBoundedResourcePolicy(ContainerServiceRole.SqlServer)
            .Build();

    private IContainer BuildMySqlFamily(PrimaryDatabaseProvider provider)
    {
        ContainerBuilder builder = new ContainerBuilder(provider == PrimaryDatabaseProvider.MariaDb
                ? "mariadb:11.4.7"
                : "mysql:8.4.6")
            .WithCommand("--performance-schema=OFF", "--innodb-buffer-pool-size=64M")
            .WithPortBinding(MySqlPort, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(MySqlPort))
            .ApplyBoundedResourcePolicy(
                provider == PrimaryDatabaseProvider.MariaDb
                    ? ContainerServiceRole.MariaDb
                    : ContainerServiceRole.MySql);

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
