using System.Globalization;
using System.Text.Json;
using Explore.Persistence.Database;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace ISLAMU.Moderation.Measurement;

internal sealed class MeasurementConfiguration : IAsyncDisposable
{
    private readonly IConfigurationRoot authority;
    private readonly SecretAuthorityAcquisition owner;

    private MeasurementConfiguration(
        IConfigurationRoot authority, string environment, SecretAuthorityAcquisition owner)
    {
        this.authority = authority;
        this.owner = owner;
        EnvironmentName = environment;
        Primary = PrimaryDatabaseConfiguration.BindRuntime(authority);
        // A separate approved test database is mandatory. Tests cannot inherit the primary
        // authority database, and this projection has no vault/user-secrets fallback.
        var section = authority.GetSection("Moderation:MeasurementTestDatabase");
        var test = new ConfigurationBuilder().AddInMemoryCollection(section.AsEnumerable(true)
            .Where(pair => pair.Value is not null)
            .Select(pair => new KeyValuePair<string, string?>("Database:" + pair.Key, pair.Value))).Build();
        using ((IDisposable)test)
            Test = PrimaryDatabaseConfiguration.BindRuntime(test);
        if (Primary.Provider == PrimaryDatabaseProvider.Sqlite && Test.Provider == PrimaryDatabaseProvider.Sqlite)
        {
            Artifacts.RegularPath(Primary.Database!);
            Artifacts.RegularPath(Test.Database!);
            Require.That(Path.GetFullPath(Primary.Database!) != Path.GetFullPath(Test.Database!),
                "test-database-not-isolated");
        }
        else
        {
            // Different DNS spellings/default ports do not prove different databases.
            // Require distinct database names even when endpoints/providers differ.
            Require.That(!string.Equals(Primary.Database, Test.Database, StringComparison.OrdinalIgnoreCase),
                "test-database-not-isolated");
        }
        PublicDigest = Artifacts.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "moderation-configuration-v1",
            environment,
            authority = owner.Provider.ToString(),
            primary = PublicCoordinates(Primary),
            test = PublicCoordinates(Test),
            childEnvironmentContract = "structured-primary-test-database-v1"
        }));
    }

    internal string EnvironmentName { get; }
    internal PrimaryDatabaseConnectionOptions Primary { get; }
    internal PrimaryDatabaseConnectionOptions Test { get; }
    internal string PublicDigest { get; }
    internal string PrimaryPublicDigest => PrimaryDatabasePublicIdentity.Digest(Primary);
    internal string TestPublicDigest => PrimaryDatabasePublicIdentity.Digest(Test);

    internal static async Task<MeasurementConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        var bootstrap = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var bootstrapLifetime = (IDisposable)bootstrap;
        var environment = bootstrap["DOTNET_ENVIRONMENT"] ?? bootstrap["ASPNETCORE_ENVIRONMENT"] ?? "Production";
        // Unlike general bootstrap, the measurement host never infers Infisical from the
        // presence of a credential. The operator must select one authority explicitly.
        Require.That(!string.IsNullOrWhiteSpace(bootstrap["SecretProvider:Provider"] ?? bootstrap["SECRET_PROVIDER"]),
            "secret-authority-selection-required");
        var owner = await SecretAuthorityBootstrap.OpenAsync(
            bootstrap, environment, SecretConsumerRole.ModerationMeasurement,
            [new("/database", "DATABASE_PROVIDER", "Database:Provider"),
             new("/moderation", "MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM", "signer")],
            new SecretAuthorityLoginAttempt(), cancellationToken: cancellationToken);
        IConfigurationRoot? authority = null;
        try
        {
            await owner.StartAuthenticationMaintenanceAsync(cancellationToken);
            var prepared = await owner.PrepareConfigurationGroupAsync(
                ["/database", "/moderation"], cancellationToken);
            if (!prepared.IsSuccess)
                throw new MeasurementFailure("selected-authority-unavailable");
            authority = new ConfigurationBuilder().AddInMemoryCollection(prepared.Value).Build();
            cancellationToken.ThrowIfCancellationRequested();
            return new(authority, environment, owner);
        }
        catch
        {
            (authority as IDisposable)?.Dispose();
            await owner.DisposeAsync();
            throw;
        }
    }

    internal string SigningKey()
    {
        var alias = authority["MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM"];
        var canonical = authority["Moderation:MeasurementSigningPrivateKeyPem"];
        Require.That(alias is null || canonical is null || alias == canonical, "signer-configuration-conflict");
        return alias ?? canonical ?? throw new MeasurementFailure("signer-unavailable");
    }

    internal Dictionary<string, string> ChildEnvironment(string directory, Guid runId)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_ENVIRONMENT"] = "Testing",
            ["ASPNETCORE_ENVIRONMENT"] = "Testing",
            ["SECRET_PROVIDER"] = "Environment",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_EnableDiagnostics"] = "0",
            ["DOTNET_ROLL_FORWARD"] = "Disable",
            ["HOME"] = directory,
            ["TMPDIR"] = directory,
            ["LANG"] = "C.UTF-8",
            ["LC_ALL"] = "C.UTF-8",
            ["ISLAMU_MODERATION_MEASUREMENT_JOURNAL"] = "1",
            ["ISLAMU_MODERATION_MEASUREMENT_RUN_ID"] = runId.ToString("N"),
            ["ISLAMU_MODERATION_MEASUREMENT_RUN_DIRECTORY"] = directory,
            ["Database__Provider"] = Test.Provider.ToString(),
            ["Database__Database"] = Test.Database!,
            ["Database__Schema"] = Test.Schema,
            ["Database__TlsMode"] = Test.TlsMode.ToString(),
            ["Database__TrustServerCertificate"] = Test.TrustServerCertificate.ToString(CultureInfo.InvariantCulture)
        };
        void Add(string name, string? value)
        {
            if (value is not null) result.Add("Database__" + name, value);
        }
        Add("Host", Test.Host);
        Add("Port", Test.Port?.ToString(CultureInfo.InvariantCulture));
        Add("Username", Test.Username);
        Add("Password", Test.Password);
        Add("ServerFlavor", Test.ServerFlavor?.ToString());
        Add("ServerVersion", Test.ServerVersion?.ToString());
        return result;
    }

    // Neither credential text nor a hash of credential text is a public configuration identity.
    private static object PublicCoordinates(PrimaryDatabaseConnectionOptions options) => new
    {
        provider = options.Provider.ToString(), role = options.Role.ToString(),
        options.Host, options.Port, options.Database, options.Schema,
        tlsMode = options.TlsMode.ToString(), options.TrustServerCertificate,
        serverFlavor = options.ServerFlavor?.ToString(), serverVersion = options.ServerVersion?.ToString()
    };

    public async ValueTask DisposeAsync()
    {
        ((IDisposable)authority).Dispose();
        await owner.DisposeAsync();
    }
}
