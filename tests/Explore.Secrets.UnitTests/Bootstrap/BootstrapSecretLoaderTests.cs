using Explore.Secrets.Bootstrap;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Bootstrap;

// All tests in this class run sequentially because they manipulate process environment variables,
// which are global state. Running in parallel would cause cross-test contamination.
[NotInParallel]
public class BootstrapSecretLoaderTests
{
    private const string HostKey = "Postgresql:Host";
    private const string PortKey = "Postgresql:Port";
    private const string DbKey = "Postgresql:Database";
    private const string UserKey = "Postgresql:Username";
    private const string PassKey = "Postgresql:Password";

    private const string HostEnv = "POSTGRESQL_HOST";
    private const string PortEnv = "POSTGRESQL_PORT";
    private const string DbEnv = "POSTGRESQL_DATABASE";
    private const string UserEnv = "POSTGRESQL_USERNAME";
    private const string PassEnv = "POSTGRESQL_PASSWORD";

    private static readonly string[] AllEnvVars =
    [
        HostEnv, PortEnv, DbEnv, UserEnv, PassEnv,
    ];

    private static IConfiguration BuildConfig(IDictionary<string, string?> values)
    {
        values.TryAdd("SecretProvider:Provider", "Environment");
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static void ClearEnv()
    {
        foreach (var key in AllEnvVars)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    #region Config Resolution

    [Test]
    public async Task ProjectPostgresConfiguration_WithDiscreteFields_BindsMigratorRole()
    {
        ClearEnv();
        try
        {
            string password = SecretsTestValues.CreateSecret();
            Environment.SetEnvironmentVariable(HostEnv, "migration-db.example.test");
            Environment.SetEnvironmentVariable(PortEnv, "6543");
            Environment.SetEnvironmentVariable(DbEnv, "event_db");
            Environment.SetEnvironmentVariable(UserEnv, "migrator_user");
            Environment.SetEnvironmentVariable(PassEnv, password);
            var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Provider"] = "Environment",
            });

            BootstrapSecretLoader.ProjectPostgresConfiguration(builder, PrimaryDatabaseRole.Migrator);
            var options = PrimaryDatabaseConfiguration.BindMigrator(builder.Build());

            await Assert.That(options.Role).IsEqualTo(PrimaryDatabaseRole.Migrator);
            await Assert.That(options.Provider).IsEqualTo(PrimaryDatabaseProvider.PostgreSql);
            await Assert.That(options.Host).IsEqualTo("migration-db.example.test");
            await Assert.That(options.Port).IsEqualTo(6543);
            await Assert.That(options.Username).IsEqualTo("migrator_user");
            await Assert.That(options.Password).IsEqualTo(password);
        }
        finally
        {
            ClearEnv();
        }
    }

    #endregion

    #region Deterministic Authority

    // BootstrapSecretLoader resolves the Infisical authority through AddInfisical, which reads
    // its secret-zero credentials (URL, project, client id/secret, environment) ONLY from the
    // process environment (SecretProvider__Infisical__* / INFISICAL_*) and deliberately ignores
    // merged IConfiguration values so a lower-priority config source can never inject them.
    // These tests therefore drive the authority through the process environment and point every
    // endpoint at loopback (or an immediately-invalid URI), so the lane exercises the real
    // fail-closed path with no outbound network I/O and no possibility of hanging.

    [Test]
    public async Task LoadPostgresConnectionString_InfisicalSelectedWithoutCredentials_DoesNotFallBack()
    {
        var previousBootstrap = CaptureInfisicalEnvironment();
        ClearInfisicalEnvironment();
        try
        {
            var configuration = InfisicalConfiguration();

            await AssertInfisicalFailureDoesNotFallBack(configuration);
        }
        finally
        {
            RestoreInfisicalEnvironment(previousBootstrap);
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_InfisicalSelectedWithInvalidUrl_DoesNotFallBack()
    {
        string coordinateCanary = $"invalid-{Guid.CreateVersion7():N}";
        var previousBootstrap = CaptureInfisicalEnvironment();
        ClearInfisicalEnvironment();
        try
        {
            SetInfisicalBootstrap(coordinateCanary);
            var configuration = InfisicalConfiguration();

            await AssertInfisicalFailureDoesNotFallBack(configuration, coordinateCanary);
        }
        finally
        {
            RestoreInfisicalEnvironment(previousBootstrap);
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_InfisicalSelectedButUnavailable_DoesNotFallBack()
    {
        string coordinateCanary = $"path-{Guid.CreateVersion7():N}";
        string unavailableUrl = GetUnusedLoopbackUrl(coordinateCanary);
        var previousBootstrap = CaptureInfisicalEnvironment();
        ClearInfisicalEnvironment();
        try
        {
            SetInfisicalBootstrap(unavailableUrl);
            var configuration = InfisicalConfiguration();

            await AssertInfisicalFailureDoesNotFallBack(configuration, coordinateCanary);
        }
        finally
        {
            RestoreInfisicalEnvironment(previousBootstrap);
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_InfisicalSelectedButUnauthorized_DoesNotFallBackOrLeakProviderBody()
    {
        string providerBodyCanary = SecretsTestValues.CreateSecret();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        Task response = RespondUnauthorizedAsync(listener, providerBodyCanary);
        var previousBootstrap = CaptureInfisicalEnvironment();
        ClearInfisicalEnvironment();
        try
        {
            SetInfisicalBootstrap($"http://127.0.0.1:{endpoint.Port}");
            var configuration = InfisicalConfiguration();

            await AssertInfisicalFailureDoesNotFallBack(configuration, providerBodyCanary);
            await response;
        }
        finally
        {
            RestoreInfisicalEnvironment(previousBootstrap);
        }
    }

    #endregion

    #region Environment Variable Resolution

    [Test]
    public async Task LoadPostgresConnectionString_WithEnvironmentVariables_ComposesCorrectConnectionString()
    {
        ClearEnv();
        try
        {
            string password = SecretsTestValues.CreateSecret();
            Environment.SetEnvironmentVariable(HostEnv, "env-host");
            Environment.SetEnvironmentVariable(PortEnv, "7777");
            Environment.SetEnvironmentVariable(DbEnv, "env_db");
            Environment.SetEnvironmentVariable(UserEnv, "env_user");
            Environment.SetEnvironmentVariable(PassEnv, password);

            var config = BuildConfig(new Dictionary<string, string?>());

            var credentials = BootstrapSecretLoader.LoadPostgresConnectionString(config);

            var parsed = new NpgsqlConnectionStringBuilder(credentials.ConnectionString);
            await Assert.That(parsed.Host).IsEqualTo("env-host");
            await Assert.That(parsed.Port).IsEqualTo(7777);
            await Assert.That(parsed.Database).IsEqualTo("env_db");
            await Assert.That(parsed.Username).IsEqualTo("env_user");
            await Assert.That(parsed.Password).IsEqualTo(password);

            await Assert.That(credentials.Source).Contains("Env");
        }
        finally
        {
            ClearEnv();
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_EnvWinsOverConfig()
    {
        ClearEnv();
        try
        {
            string password = SecretsTestValues.CreateSecret();
            Environment.SetEnvironmentVariable(HostEnv, "env-host");
            Environment.SetEnvironmentVariable(PortEnv, "6543");
            Environment.SetEnvironmentVariable(DbEnv, "env_db");
            Environment.SetEnvironmentVariable(UserEnv, "env_user");
            Environment.SetEnvironmentVariable(PassEnv, password);

            var config = BuildConfig(new Dictionary<string, string?>
            {
                [HostKey] = "config-host",
                [PortKey] = "5432",
                [DbKey] = "config_db",
                [UserKey] = "config_user",
                [PassKey] = SecretsTestValues.CreateSecret(),
            });

            var credentials = BootstrapSecretLoader.LoadPostgresConnectionString(config);

            var parsed = new NpgsqlConnectionStringBuilder(credentials.ConnectionString);
            await Assert.That(parsed.Host).IsEqualTo("env-host");
            await Assert.That(parsed.Database).IsEqualTo("env_db");
            await Assert.That(parsed.Username).IsEqualTo("env_user");
            await Assert.That(parsed.Password).IsEqualTo(password);
        }
        finally
        {
            ClearEnv();
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_EnvironmentModeDoesNotMixConfigurationValues()
    {
        ClearEnv();
        try
        {
            Environment.SetEnvironmentVariable(HostEnv, "env-host");

            var config = BuildConfig(new Dictionary<string, string?>
            {
                [PortKey] = "5432",
                [DbKey] = "db",
                [UserKey] = "u",
                [PassKey] = SecretsTestValues.CreateSecret(),
            });

            var act = () => BootstrapSecretLoader.LoadPostgresConnectionString(config);

            var exception = (await Assert.That(act).Throws<InvalidOperationException>())!;
            await Assert.That(exception.Message).Contains("database");
            await Assert.That(exception.Message).Contains("username");
            await Assert.That(exception.Message).Contains("password");
        }
        finally
        {
            ClearEnv();
        }
    }

    #endregion

    #region Missing Field Errors

    [Test]
    public async Task LoadPostgresConnectionString_MissingHost_ThrowsInvalidOperationException()
    {
        ClearEnv();
        try
        {
            Environment.SetEnvironmentVariable(DbEnv, "db");
            Environment.SetEnvironmentVariable(UserEnv, "user");
            Environment.SetEnvironmentVariable(PassEnv, SecretsTestValues.CreateSecret());
            var config = BuildConfig(new Dictionary<string, string?>());

            var act = () => BootstrapSecretLoader.LoadPostgresConnectionString(config);

            await Assert.That(act).Throws<InvalidOperationException>()
                .WithMessageContaining("host");
        }
        finally
        {
            ClearEnv();
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_MissingDatabase_ThrowsInvalidOperationException()
    {
        ClearEnv();
        try
        {
            Environment.SetEnvironmentVariable(HostEnv, "localhost");
            Environment.SetEnvironmentVariable(UserEnv, "user");
            Environment.SetEnvironmentVariable(PassEnv, SecretsTestValues.CreateSecret());
            var config = BuildConfig(new Dictionary<string, string?>());

            var act = () => BootstrapSecretLoader.LoadPostgresConnectionString(config);

            await Assert.That(act).Throws<InvalidOperationException>()
                .WithMessageContaining("database");
        }
        finally
        {
            ClearEnv();
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_MissingUsername_ThrowsInvalidOperationException()
    {
        ClearEnv();
        try
        {
            Environment.SetEnvironmentVariable(HostEnv, "localhost");
            Environment.SetEnvironmentVariable(DbEnv, "db");
            Environment.SetEnvironmentVariable(PassEnv, SecretsTestValues.CreateSecret());
            var config = BuildConfig(new Dictionary<string, string?>());

            var act = () => BootstrapSecretLoader.LoadPostgresConnectionString(config);

            await Assert.That(act).Throws<InvalidOperationException>()
                .WithMessageContaining("username");
        }
        finally
        {
            ClearEnv();
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_MissingPassword_ThrowsInvalidOperationException()
    {
        ClearEnv();
        try
        {
            Environment.SetEnvironmentVariable(HostEnv, "localhost");
            Environment.SetEnvironmentVariable(DbEnv, "db");
            Environment.SetEnvironmentVariable(UserEnv, "user");
            var config = BuildConfig(new Dictionary<string, string?>());

            var act = () => BootstrapSecretLoader.LoadPostgresConnectionString(config);

            await Assert.That(act).Throws<InvalidOperationException>()
                .WithMessageContaining("password");
        }
        finally
        {
            ClearEnv();
        }
    }

    [Test]
    public async Task LoadPostgresConnectionString_AllMissing_ErrorListsEveryMissingField()
    {
        ClearEnv();

        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => BootstrapSecretLoader.LoadPostgresConnectionString(config);

        var ex = (await Assert.That(act).Throws<InvalidOperationException>())!;
        await Assert.That(ex.Message).Contains("host");
        await Assert.That(ex.Message).Contains("database");
        await Assert.That(ex.Message).Contains("username");
        await Assert.That(ex.Message).Contains("password");
    }

    #endregion

    private static readonly string[] InfisicalBootstrapKeys =
    [
        "SecretProvider__Infisical__Url",
        "SecretProvider__Infisical__ProjectId",
        "SecretProvider__Infisical__ClientId",
        "SecretProvider__Infisical__ClientSecret",
        "SecretProvider__Infisical__Environment",
        "INFISICAL_URL",
        "INFISICAL_PROJECT_ID",
        "INFISICAL_CLIENT_ID",
        "INFISICAL_CLIENT_SECRET",
        "INFISICAL_ENV",
    ];

    private static IConfiguration InfisicalConfiguration() =>
        BuildConfig(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Infisical",
        });

    private static void SetInfisicalBootstrap(string url)
    {
        Environment.SetEnvironmentVariable("SecretProvider__Infisical__Url", url);
        Environment.SetEnvironmentVariable(
            "SecretProvider__Infisical__ProjectId", $"project-{Guid.CreateVersion7():N}");
        Environment.SetEnvironmentVariable(
            "SecretProvider__Infisical__ClientId", $"client-{Guid.CreateVersion7():N}");
        Environment.SetEnvironmentVariable(
            "SecretProvider__Infisical__ClientSecret", SecretsTestValues.CreateSecret());
        Environment.SetEnvironmentVariable("SecretProvider__Infisical__Environment", "test");
    }

    private static Dictionary<string, string?> CaptureInfisicalEnvironment() =>
        InfisicalBootstrapKeys.ToDictionary(
            key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);

    private static void ClearInfisicalEnvironment()
    {
        foreach (var key in InfisicalBootstrapKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private static void RestoreInfisicalEnvironment(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var pair in values)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private static async Task AssertInfisicalFailureDoesNotFallBack(
        IConfiguration configuration,
        params string[] outputCanaries)
    {
        ClearEnv();
        string fallbackPassword = SecretsTestValues.CreateSecret();
        var originalError = Console.Error;
        using var error = new StringWriter();
        Exception? failure = null;

        try
        {
            Environment.SetEnvironmentVariable(HostEnv, "lower-authority-host");
            Environment.SetEnvironmentVariable(DbEnv, "lower-authority-database");
            Environment.SetEnvironmentVariable(UserEnv, "lower-authority-user");
            Environment.SetEnvironmentVariable(PassEnv, fallbackPassword);
            Console.SetError(error);

            try
            {
                BootstrapSecretLoader.LoadPostgresConnectionString(configuration);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }
        finally
        {
            Console.SetError(originalError);
            ClearEnv();
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(error.ToString()).DoesNotContain(fallbackPassword);
        foreach (string canary in outputCanaries)
        {
            await Assert.That(error.ToString()).DoesNotContain(canary);
            await Assert.That(failure!.Message).DoesNotContain(canary);
        }
    }

    private static string GetUnusedLoopbackUrl(string path)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return $"http://127.0.0.1:{port}/{path}";
    }

    private static async Task RespondUnauthorizedAsync(TcpListener listener, string responseBody)
    {
        // Bound the accept so a future production regression that never connects fails this test
        // in seconds instead of hanging the whole [NotInParallel] class indefinitely.
        using var acceptTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using TcpClient client = await listener.AcceptTcpClientAsync(acceptTimeout.Token);
        await using NetworkStream stream = client.GetStream();
        byte[] body = Encoding.UTF8.GetBytes(responseBody);
        byte[] response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 401 Unauthorized\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
        await stream.WriteAsync(body);
    }
}
