using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;

namespace Explore.Secrets.Database;

public static partial class PrimaryDatabaseConfiguration
{
    private const string SectionName = "Database";
    private const string RuntimeSectionName = "Runtime";
    private const string MigratorSectionName = "Migrator";
    private const string PrivacyErasureAuthorityDatabaseFileName = "privacy_erasure_authority.db";
    private const string SchemaEnvironmentAlias = "DATABASE_SCHEMA";
    private const string PrefixEnvironmentAlias = "DATABASE_PREFIX";
    private const string RuntimePrefixEnvironmentAlias = "DATABASE_RUNTIME_PREFIX";
    private const string MigratorPrefixEnvironmentAlias = "DATABASE_MIGRATOR_PREFIX";
    private const string PrefixStructuredAlias = "Database:Prefix";
    private const string RuntimePrefixStructuredAlias = "Database:Runtime:Prefix";
    private const string MigratorPrefixStructuredAlias = "Database:Migrator:Prefix";

    public static PrimaryDatabaseConnectionOptions BindRuntime(IConfiguration configuration)
        => Bind(configuration, PrimaryDatabaseRole.Runtime);

    public static PrimaryDatabaseConnectionOptions BindMigrator(IConfiguration configuration)
        => Bind(configuration, PrimaryDatabaseRole.Migrator);

    public static PrimaryDatabaseConnectionOptions Bind(IConfiguration configuration, PrimaryDatabaseRole role)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        RejectUnsupportedPrefixAlias(configuration);

        var root = configuration.GetSection(SectionName);
        var roleSection = root.GetSection(role == PrimaryDatabaseRole.Runtime ? RuntimeSectionName : MigratorSectionName);
        var providerValue = ReadRequired(configuration, root, roleSection, "Provider", role);
        if (!TryParseNamedEnum(providerValue, out PrimaryDatabaseProvider provider))
        {
            throw new InvalidOperationException(
                $"Database:{nameof(PrimaryDatabaseConnectionOptions.Provider)} must be one of PostgreSql, Sqlite, SqlServer, MariaDb, or MySql.");
        }

        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = provider,
            Host = ReadOptional(configuration, root, roleSection, "Host", role),
            Port = ReadOptionalInt(configuration, root, roleSection, "Port", role),
            Database = ReadDatabaseName(configuration, root, roleSection),
            Schema = ReadSchema(configuration, root),
            Username = ReadOptional(configuration, root, roleSection, "Username", role),
            Password = ReadOptional(configuration, root, roleSection, "Password", role),
            TlsMode = ReadOptionalEnum(
                configuration,
                root,
                roleSection,
                "TlsMode",
                provider == PrimaryDatabaseProvider.Sqlite
                    ? PrimaryDatabaseTlsMode.Prefer
                    : PrimaryDatabaseTlsMode.Required,
                role),
            TrustServerCertificate = ReadOptionalBool(configuration, root, roleSection, "TrustServerCertificate", role) ?? false,
            ServerFlavor = ReadOptionalNullableEnum<PrimaryDatabaseServerFlavor>(configuration, root, roleSection, "ServerFlavor", role)
                ?? (provider switch
                {
                    PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                    PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                    _ => null
                }),
            ServerVersion = ReadOptionalVersion(configuration, root, roleSection, "ServerVersion", role)
                ?? (provider switch
                {
                    PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                    PrimaryDatabaseProvider.MySql => new Version(8, 4),
                    _ => null
                }),
        };

        Validate(options);
        return options;
    }

    public static void Validate(PrimaryDatabaseConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var hasHost = !string.IsNullOrWhiteSpace(options.Host);
        var hasDatabase = !string.IsNullOrWhiteSpace(options.Database);
        var hasUsername = !string.IsNullOrWhiteSpace(options.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(options.Password);
        var hasFlavor = options.ServerFlavor is not null;
        var hasVersion = options.ServerVersion is not null;

        if (string.IsNullOrWhiteSpace(options.Schema) || !PortableSchemaName().IsMatch(options.Schema))
        {
            errors.Add("Database:Schema must start with a letter or underscore, contain only ASCII letters, digits, or underscores, and be at most 63 characters.");
        }

        switch (options.Provider)
        {
            case PrimaryDatabaseProvider.PostgreSql:
                RequireServerFields(errors, options, hasHost, hasDatabase, hasUsername, hasPassword);
                ValidateServerTls(errors, options);
                ValidateNoFlavorOrVersion(errors, options, hasFlavor, hasVersion);
                if (ContainsPathSemantics(options.Database))
                {
                    errors.Add("PostgreSql database names must not use file-path semantics.");
                }
                break;

            case PrimaryDatabaseProvider.SqlServer:
                RequireServerFields(errors, options, hasHost, hasDatabase, hasUsername, hasPassword);
                ValidateServerTls(errors, options);
                ValidateNoFlavorOrVersion(errors, options, hasFlavor, hasVersion);
                if (ContainsPathSemantics(options.Database))
                {
                    errors.Add("SqlServer database names must not use file-path semantics.");
                }
                break;

            case PrimaryDatabaseProvider.MariaDb:
                RequireServerFields(errors, options, hasHost, hasDatabase, hasUsername, hasPassword);
                ValidateServerTls(errors, options);
                ValidateFlavorAndVersion(errors, options, PrimaryDatabaseServerFlavor.MariaDb, hasFlavor, hasVersion);
                if (ContainsPathSemantics(options.Database))
                {
                    errors.Add("MariaDb database names must not use file-path semantics.");
                }
                break;

            case PrimaryDatabaseProvider.MySql:
                RequireServerFields(errors, options, hasHost, hasDatabase, hasUsername, hasPassword);
                ValidateServerTls(errors, options);
                ValidateFlavorAndVersion(errors, options, PrimaryDatabaseServerFlavor.MySql, hasFlavor, hasVersion);
                if (ContainsPathSemantics(options.Database))
                {
                    errors.Add("MySql database names must not use file-path semantics.");
                }
                break;

            case PrimaryDatabaseProvider.Sqlite:
                if (!hasDatabase)
                {
                    errors.Add("Sqlite requires a persisted local file path in Database.");
                }
                else if (IsNonPersistedSqliteDatabase(options.Database!))
                {
                    errors.Add("Sqlite requires a persisted local file path and forbids in-memory databases.");
                }
                else if (!IsLocalSqliteFilePath(options.Database!))
                {
                    errors.Add("Sqlite requires a persisted local file path and forbids URI and network paths.");
                }
                else if (IsReservedSqliteAuthorityDatabase(options.Database!))
                {
                    errors.Add($"Sqlite Database must not use the reserved authority file '{PrivacyErasureAuthorityDatabaseFileName}'.");
                }
                if (hasHost) errors.Add("Sqlite forbids Host.");
                if (options.Port.HasValue) errors.Add("Sqlite forbids Port.");
                if (hasUsername) errors.Add("Sqlite forbids Username.");
                if (hasPassword) errors.Add("Sqlite forbids Password.");
                if (hasFlavor) errors.Add("Sqlite forbids ServerFlavor.");
                if (hasVersion) errors.Add("Sqlite forbids ServerVersion.");
                if (options.TrustServerCertificate) errors.Add("Sqlite forbids TrustServerCertificate.");
                if (options.TlsMode != PrimaryDatabaseTlsMode.Prefer)
                {
                    errors.Add("Sqlite requires the default TLS mode and ignores transport TLS settings.");
                }
                break;

            default:
                errors.Add($"Unsupported database provider '{options.Provider}'.");
                break;
        }

        if (errors.Count > 0)
        {
            throw new OptionsValidationException(
                SectionName,
                typeof(PrimaryDatabaseConnectionOptions),
                errors);
        }
    }

    public static PrimaryDatabaseConnectionResult BuildConnectionString(PrimaryDatabaseConnectionOptions options)
    {
        Validate(options);

        return options.Provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => BuildPostgreSql(options),
            PrimaryDatabaseProvider.Sqlite => BuildSqlite(options),
            PrimaryDatabaseProvider.SqlServer => BuildSqlServer(options),
            PrimaryDatabaseProvider.MariaDb => BuildMySql(options),
            PrimaryDatabaseProvider.MySql => BuildMySql(options),
            _ => throw new OptionsValidationException(SectionName, typeof(PrimaryDatabaseConnectionOptions), [$"Unsupported database provider '{options.Provider}'."]),
        };
    }

    public static PrimaryDatabaseConnectionResult ResolveRuntimeConnectionString(IConfiguration configuration)
        => ResolveConnectionString(configuration, PrimaryDatabaseRole.Runtime);

    public static PrimaryDatabaseConnectionResult ResolveMigratorConnectionString(IConfiguration configuration)
        => ResolveConnectionString(configuration, PrimaryDatabaseRole.Migrator);

    public static PrimaryDatabaseConnectionResult ResolveConnectionString(IConfiguration configuration, PrimaryDatabaseRole role)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return BuildConnectionString(Bind(configuration, role));
    }

    private static PrimaryDatabaseConnectionResult BuildPostgreSql(PrimaryDatabaseConnectionOptions options)
    {
        var port = options.Port ?? 5432;
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host!,
            Port = port,
            Database = options.Database!,
            Username = options.Username!,
            Password = options.Password!,
            SearchPath = options.Role == PrimaryDatabaseRole.Migrator
                ? $"{options.Schema}, public"
                : options.Schema,
            SslMode = MapNpgsqlSslMode(options.TlsMode, options.TrustServerCertificate),
        };

        return new PrimaryDatabaseConnectionResult(
            options.Role,
            options.Provider,
            builder.ConnectionString,
            Redact(builder.ConnectionString),
            Describe(options, port));
    }

    private static PrimaryDatabaseConnectionResult BuildSqlite(PrimaryDatabaseConnectionOptions options)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = options.Database!,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
        };

        return new PrimaryDatabaseConnectionResult(
            options.Role,
            options.Provider,
            builder.ConnectionString,
            Redact(builder.ConnectionString),
            Describe(options, null));
    }

    private static PrimaryDatabaseConnectionResult BuildSqlServer(PrimaryDatabaseConnectionOptions options)
    {
        var port = options.Port ?? 1433;
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = options.Host is null ? throw new InvalidOperationException("SqlServer Host is required.") : FormatSqlServerDataSource(options.Host, port),
            InitialCatalog = options.Database!,
            UserID = options.Username!,
            Password = options.Password!,
            Encrypt = options.TlsMode != PrimaryDatabaseTlsMode.Disabled,
            TrustServerCertificate = options.TrustServerCertificate,
            ConnectTimeout = 30,
        };

        return new PrimaryDatabaseConnectionResult(
            options.Role,
            options.Provider,
            builder.ConnectionString,
            Redact(builder.ConnectionString),
            Describe(options, port));
    }

    private static PrimaryDatabaseConnectionResult BuildMySql(PrimaryDatabaseConnectionOptions options)
    {
        var port = options.Port ?? 3306;
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Host!,
            Port = (uint)port,
            Database = options.Database!,
            UserID = options.Username!,
            Password = options.Password!,
            ConnectionTimeout = 30,
            SslMode = MapMySqlSslMode(options.TlsMode, options.TrustServerCertificate),
        };

        return new PrimaryDatabaseConnectionResult(
            options.Role,
            options.Provider,
            builder.ConnectionString,
            Redact(builder.ConnectionString),
            Describe(options, port));
    }

    private static void RequireServerFields(List<string> errors, PrimaryDatabaseConnectionOptions options, bool hasHost, bool hasDatabase, bool hasUsername, bool hasPassword)
    {
        if (!hasHost) errors.Add($"{options.Provider} requires Host.");
        if (!hasDatabase) errors.Add($"{options.Provider} requires Database.");
        if (!hasUsername) errors.Add($"{options.Provider} requires Username.");
        if (!hasPassword) errors.Add($"{options.Provider} requires Password.");
    }

    private static void ValidateServerTls(List<string> errors, PrimaryDatabaseConnectionOptions options)
    {
        if (options.TrustServerCertificate
            && options.TlsMode != PrimaryDatabaseTlsMode.Required)
        {
            errors.Add($"{options.Provider} can bypass certificate validation only when TLS is required.");
        }
    }

    private static void ValidateNoFlavorOrVersion(List<string> errors, PrimaryDatabaseConnectionOptions options, bool hasFlavor, bool hasVersion)
    {
        if (hasFlavor) errors.Add($"{options.Provider} forbids ServerFlavor.");
        if (hasVersion) errors.Add($"{options.Provider} forbids ServerVersion.");
    }

    private static void ValidateFlavorAndVersion(List<string> errors, PrimaryDatabaseConnectionOptions options, PrimaryDatabaseServerFlavor expectedFlavor, bool hasFlavor, bool hasVersion)
    {
        if (options.ServerFlavor is not null && options.ServerFlavor != expectedFlavor)
        {
            errors.Add($"{options.Provider} requires ServerFlavor={expectedFlavor}.");
        }

        if (options.ServerVersion is { Major: < 1 } or null)
        {
            errors.Add($"{options.Provider} requires a bounded positive ServerVersion.");
        }
    }

    private static bool ContainsPathSemantics(string? database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            return false;
        }

        return Path.IsPathRooted(database)
            || database.Contains(Path.DirectorySeparatorChar)
            || database.Contains(Path.AltDirectorySeparatorChar)
            || database.Contains("://", StringComparison.Ordinal);
    }

    private static bool IsNonPersistedSqliteDatabase(string database)
    {
        var normalized = database.Trim();
        if (string.Equals(normalized, ":memory:", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "file::memory:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var queryIndex = normalized.IndexOf('?');
        if (queryIndex < 0)
        {
            return string.Equals(normalized[5..], ":memory:", StringComparison.OrdinalIgnoreCase);
        }

        var path = normalized[5..queryIndex];
        if (string.Equals(path, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalized[(queryIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static parameter => string.Equals(
                parameter,
                "mode=memory",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLocalSqliteFilePath(string database)
    {
        var normalized = database.Trim();
        return !normalized.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("//", StringComparison.Ordinal)
            && !normalized.StartsWith("\\\\", StringComparison.Ordinal)
            && !normalized.Contains("://", StringComparison.Ordinal);
    }

    private static bool IsReservedSqliteAuthorityDatabase(string database)
    {
        var normalized = database.Trim().Replace('\\', '/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        return string.Equals(fileName, PrivacyErasureAuthorityDatabaseFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSqlServerDataSource(string host, int port)
        => port == 1433 ? host : $"{host},{port}";

    private static SslMode MapNpgsqlSslMode(
        PrimaryDatabaseTlsMode tlsMode,
        bool trustServerCertificate) => tlsMode switch
        {
            PrimaryDatabaseTlsMode.Disabled => SslMode.Disable,
            PrimaryDatabaseTlsMode.Required when trustServerCertificate => SslMode.Require,
            PrimaryDatabaseTlsMode.Required => SslMode.VerifyFull,
            _ => SslMode.Prefer,
        };

    private static MySqlSslMode MapMySqlSslMode(PrimaryDatabaseTlsMode tlsMode, bool trustServerCertificate) => tlsMode switch
    {
        PrimaryDatabaseTlsMode.Disabled => MySqlSslMode.None,
        PrimaryDatabaseTlsMode.Required when trustServerCertificate => MySqlSslMode.Required,
        PrimaryDatabaseTlsMode.Required => MySqlSslMode.VerifyFull,
        PrimaryDatabaseTlsMode.Prefer when trustServerCertificate => MySqlSslMode.Preferred,
        _ => MySqlSslMode.VerifyFull,
    };

    private static string Describe(PrimaryDatabaseConnectionOptions options, int? port)
    {
        var safeHost = string.IsNullOrWhiteSpace(options.Host) ? "<none>" : options.Host.Trim();
        var safeDatabase = string.IsNullOrWhiteSpace(options.Database) ? "<none>" : options.Database.Trim();
        return port is null
            ? $"{options.Role}:{options.Provider} host={safeHost} database={safeDatabase} tls={options.TlsMode} trust={options.TrustServerCertificate}"
            : $"{options.Role}:{options.Provider} host={safeHost} port={port} database={safeDatabase} tls={options.TlsMode} trust={options.TrustServerCertificate}";
    }

    private static string Redact(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return PasswordRegex().Replace(connectionString, "$1=***");
    }

    private static string ReadRequired(IConfiguration configuration, IConfiguration root, IConfiguration role, string key, PrimaryDatabaseRole? databaseRole = null)
        => ReadOptional(configuration, root, role, key, databaseRole) ?? throw new InvalidOperationException($"Missing structured database configuration section '{SectionName}' or required setting Database:{key}.");

    private static string? ReadOptional(IConfiguration configuration, IConfiguration root, IConfiguration role, string key, PrimaryDatabaseRole? databaseRole = null)
    {
        var roleValue = role[key];
        if (!string.IsNullOrWhiteSpace(roleValue))
        {
            return roleValue.Trim();
        }

        var rootValue = root[key];
        if (!string.IsNullOrWhiteSpace(rootValue))
        {
            return rootValue.Trim();
        }

        if (databaseRole is not null)
        {
            var roleFlatKey = $"DATABASE_{databaseRole.Value.ToString().ToUpperInvariant()}_{key.ToUpperInvariant()}";
            var roleFlatValue = configuration[roleFlatKey];
            if (!string.IsNullOrWhiteSpace(roleFlatValue))
            {
                return roleFlatValue.Trim();
            }

            var roleDotNetKey = $"Database:{databaseRole.Value}:{key}";
            var roleDotNetValue = configuration[roleDotNetKey];
            if (!string.IsNullOrWhiteSpace(roleDotNetValue))
            {
                return roleDotNetValue.Trim();
            }
        }

        var genericFlatKey = $"DATABASE_{key.ToUpperInvariant()}";
        var genericFlatValue = configuration[genericFlatKey];
        if (!string.IsNullOrWhiteSpace(genericFlatValue))
        {
            return genericFlatValue.Trim();
        }

        var directDotNetKey = $"Database:{key}";
        var directDotNetValue = configuration[directDotNetKey];
        if (!string.IsNullOrWhiteSpace(directDotNetValue))
        {
            return directDotNetValue.Trim();
        }

        return null;
    }

    private static string? ReadDatabaseName(IConfiguration configuration, IConfiguration root, IConfiguration role)
    {
        var roleValue = role["Name"] ?? role["Database"];
        if (!string.IsNullOrWhiteSpace(roleValue))
        {
            return roleValue.Trim();
        }

        var rootValue = root["Name"] ?? root["Database"];
        if (!string.IsNullOrWhiteSpace(rootValue))
        {
            return rootValue.Trim();
        }

        var alias = configuration["DATABASE_NAME"]
            ?? configuration["DATABASE_DATABASE"]
            ?? configuration["Database:Name"]
            ?? configuration["Database:Database"];
        return string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
    }

    private static string ReadSchema(IConfiguration configuration, IConfiguration root)
    {
        var structured = root[nameof(PrimaryDatabaseConnectionOptions.Schema)];
        if (!string.IsNullOrWhiteSpace(structured))
        {
            return structured.Trim();
        }

        var alias = configuration[SchemaEnvironmentAlias];
        return string.IsNullOrWhiteSpace(alias)
            ? PrimaryDatabaseConnectionOptions.DefaultSchema
            : alias.Trim();
    }

    private static void RejectUnsupportedPrefixAlias(IConfiguration configuration)
    {
        var unsupportedPrefix = configuration[PrefixEnvironmentAlias];
        var unsupportedStructuredPrefix = configuration[PrefixStructuredAlias];
        var unsupportedRuntimePrefix = configuration[RuntimePrefixEnvironmentAlias];
        var unsupportedMigratorPrefix = configuration[MigratorPrefixEnvironmentAlias];
        var unsupportedRuntimeStructuredPrefix = configuration[RuntimePrefixStructuredAlias];
        var unsupportedMigratorStructuredPrefix = configuration[MigratorPrefixStructuredAlias];

        if (!string.IsNullOrWhiteSpace(unsupportedPrefix) ||
            !string.IsNullOrWhiteSpace(unsupportedStructuredPrefix) ||
            !string.IsNullOrWhiteSpace(unsupportedRuntimePrefix) ||
            !string.IsNullOrWhiteSpace(unsupportedMigratorPrefix) ||
            !string.IsNullOrWhiteSpace(unsupportedRuntimeStructuredPrefix) ||
            !string.IsNullOrWhiteSpace(unsupportedMigratorStructuredPrefix))
        {
            throw new InvalidOperationException(
                "Prefix overrides are not supported (DATABASE_PREFIX, DATABASE_RUNTIME_PREFIX, "
                + "DATABASE_MIGRATOR_PREFIX, Database:Prefix, Database:Runtime:Prefix, and Database:Migrator:Prefix are rejected); "
                + "use Database:Schema (or DATABASE_SCHEMA) only. Schema-less providers always use the fixed ie_ prefix.");
        }
    }

    private static int? ReadOptionalInt(IConfiguration configuration, IConfiguration root, IConfiguration role, string key, PrimaryDatabaseRole? databaseRole = null)
    {
        var value = ReadOptional(configuration, root, role, key, databaseRole);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new InvalidOperationException($"Database:{key} must be a valid integer.");
    }

    private static bool? ReadOptionalBool(IConfiguration configuration, IConfiguration root, IConfiguration role, string key, PrimaryDatabaseRole? databaseRole = null)
    {
        var value = ReadOptional(configuration, root, role, key, databaseRole);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var parsed) ? parsed : throw new InvalidOperationException($"Database:{key} must be true or false.");
    }

    private static TEnum ReadOptionalEnum<TEnum>(IConfiguration configuration, IConfiguration root, IConfiguration role, string key, TEnum defaultValue, PrimaryDatabaseRole? databaseRole = null)
        where TEnum : struct, Enum
    {
        var value = ReadOptional(configuration, root, role, key, databaseRole);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return TryParseNamedEnum(value, out TEnum parsed)
            ? parsed
            : throw new InvalidOperationException($"Database:{key} is invalid.");
    }

    private static TEnum? ReadOptionalNullableEnum<TEnum>(IConfiguration configuration, IConfiguration root, IConfiguration role, string key, PrimaryDatabaseRole? databaseRole = null)
        where TEnum : struct, Enum
    {
        var value = ReadOptional(configuration, root, role, key, databaseRole);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TryParseNamedEnum(value, out TEnum parsed)
            ? parsed
            : throw new InvalidOperationException($"Database:{key} is invalid.");
    }

    private static Version? ReadOptionalVersion(IConfiguration configuration, IConfiguration root, IConfiguration role, string key, PrimaryDatabaseRole? databaseRole = null)
    {
        var value = ReadOptional(configuration, root, role, key, databaseRole);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Version.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Database:{key} must be a valid version string.");
    }

    private static bool TryParseNamedEnum<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        return !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && Enum.TryParse(value, ignoreCase: true, out parsed)
            && Enum.IsDefined(parsed);
    }

    [GeneratedRegex("(?i)(password|pwd|secret)=([^;]*)")]
    private static partial Regex PasswordRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex PortableSchemaName();
}
