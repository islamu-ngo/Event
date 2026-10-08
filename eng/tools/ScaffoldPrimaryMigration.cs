#:project ../../src/Explore.Secrets/Explore.Secrets.csproj
#:property RestorePackagesWithLockFile=false
#:property PublishAot=false

using System.Diagnostics;
using System.Globalization;
using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;

if (args.Length != 2 || args[1].Length == 0 || !Enum.TryParse<PrimaryDatabaseProvider>(args[0], true, out var provider)
    || !Enum.IsDefined(provider) || !char.IsAsciiLetter(args[1][0])
    || args[1].Any(character => !char.IsAsciiLetterOrDigit(character)))
{
    Console.Error.WriteLine("Usage: dotnet run eng/tools/ScaffoldPrimaryMigration.cs -- <provider> <migration-name>");
    return 64;
}

var root = Directory.GetCurrentDirectory();
var project = provider switch
{
    PrimaryDatabaseProvider.PostgreSql => "src/Explore.Persistence/Explore.Persistence.csproj",
    PrimaryDatabaseProvider.Sqlite => "src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj",
    PrimaryDatabaseProvider.SqlServer => "src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj",
    PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb =>
        "src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj",
    _ => throw new InvalidOperationException("The selected primary provider has no migration project.")
};
if (!File.Exists(Path.Combine(root, project)))
{
    Console.Error.WriteLine("Run the scaffolder from the repository root.");
    return 64;
}

var bootstrap = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var authority = SecretAuthorityConfiguration.Build(
    bootstrap, SecretAuthorityConfiguration.GetEnvironmentName(bootstrap), "/database");
var selected = PrimaryDatabaseConfiguration.BindMigrator(authority);
var sqlite = provider == PrimaryDatabaseProvider.Sqlite;
var mysql = provider is PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb;
var options = selected with
{
    Provider = provider,
    Host = sqlite ? null : selected.Host,
    Port = provider switch
    {
        PrimaryDatabaseProvider.Sqlite => null,
        PrimaryDatabaseProvider.SqlServer => 1433,
        PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb => 3306,
        _ => selected.Port
    },
    Database = sqlite ? Path.Combine(root, ".artifacts", "islamu-event-migrations.db") : selected.Database,
    Username = sqlite ? null : selected.Username,
    Password = sqlite ? null : selected.Password,
    TlsMode = sqlite ? PrimaryDatabaseTlsMode.Prefer : selected.TlsMode,
    TrustServerCertificate = !sqlite && selected.TrustServerCertificate,
    ServerFlavor = mysql
        ? provider == PrimaryDatabaseProvider.MySql ? PrimaryDatabaseServerFlavor.MySql : PrimaryDatabaseServerFlavor.MariaDb
        : null,
    ServerVersion = provider switch
    {
        PrimaryDatabaseProvider.MySql => new Version(8, 4),
        PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
        _ => null
    }
};
PrimaryDatabaseConfiguration.Validate(options);
Directory.CreateDirectory(Path.Combine(root, ".artifacts"));

// Credentials originate in the selected authority and exist only in this child process.
// Scaffold does not connect to or update a database; provider-specific fields select its model.
var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root };
foreach (var argument in new[]
{
    "ef", "migrations", "add", args[1], "--context", "ExploreDbContext",
    "--project", project, "--startup-project", project,
    "--configuration", "Release", "--output-dir", "Migrations"
})
{
    start.ArgumentList.Add(argument);
}
start.Environment["SecretProvider__Provider"] = "Environment";
start.Environment["SECRET_PROVIDER"] = "Environment";
foreach (var field in new KeyValuePair<string, string?>[]
{
    new("Provider", options.Provider.ToString()),
    new("Host", options.Host),
    new("Port", options.Port?.ToString(CultureInfo.InvariantCulture)),
    new("Database", options.Database),
    new("Username", options.Username),
    new("Password", options.Password),
    new("TlsMode", options.TlsMode.ToString()),
    new("TrustServerCertificate", options.TrustServerCertificate.ToString(CultureInfo.InvariantCulture)),
    new("ServerFlavor", options.ServerFlavor?.ToString()),
    new("ServerVersion", options.ServerVersion?.ToString())
})
{
    start.Environment[$"Database__{field.Key}"] = field.Value ?? string.Empty;
    start.Environment[$"Database__Migrator__{field.Key}"] = field.Value ?? string.Empty;
    start.Environment[$"DATABASE_{field.Key.ToUpperInvariant()}"] = string.Empty;
    start.Environment[$"DATABASE_MIGRATOR_{field.Key.ToUpperInvariant()}"] = string.Empty;
}
start.Environment["Database__Schema"] = options.Schema;
start.Environment["DATABASE_NAME"] = string.Empty;
using var child = Process.Start(start) ?? throw new InvalidOperationException("Unable to start native EF scaffolding.");
await child.WaitForExitAsync();
return child.ExitCode;
