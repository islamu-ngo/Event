#:project ../../src/Explore.Persistence/Explore.Persistence.csproj
#:property RestorePackagesWithLockFile=true
#:property NuGetLockFilePath=ScaffoldPrimaryMigration.packages.lock.json
#:property PublishAot=false

using System.Diagnostics;
using System.Globalization;
using Explore.Persistence.Database;
using Microsoft.Extensions.Configuration;

bool script = args.Length == 4 && args[2] == "--script-from";
if (args.Length != 2 && !script || args[1].Length == 0 || !Enum.TryParse<PrimaryDatabaseProvider>(args[0], true, out var provider)
    || !Enum.IsDefined(provider) || !char.IsAsciiLetter(args[1][0])
    || args[1].Any(character => !char.IsAsciiLetterOrDigit(character))
    || script && (args[3].Length == 0 || args[3].Any(character => !char.IsAsciiLetterOrDigit(character))))
{
    Console.Error.WriteLine("Usage: dotnet run eng/tools/ScaffoldPrimaryMigration.cs -- <provider> <migration-name> [--script-from <previous-migration>]");
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
using var bootstrapLifetime = (IDisposable)bootstrap;
string? environment = bootstrap["DOTNET_ENVIRONMENT"] ?? bootstrap["ASPNETCORE_ENVIRONMENT"];
if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
    && !string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Offline migration scaffolding requires explicit Development or Testing.");
var selected = new PrimaryDatabaseConnectionOptions
{
    Provider = provider,
    Role = PrimaryDatabaseRole.Migrator,
    Schema = bootstrap["Database:Schema"] ?? PrimaryDatabaseConnectionOptions.DefaultSchema
};
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
Directory.CreateDirectory(Path.Combine(root, ".artifacts"));

// Model-only scaffolding passes public coordinates and never acquires database credentials.
// Build the selected product project with --no-restore first; scaffolding must not restore implicitly.
// Scaffolding and bounded SQL generation never connect to or update a database.
var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root };
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
string[] shared = ["--context", "ExploreDbContext", "--project", project, "--startup-project", project,
    "--configuration", "Release", "--no-build"];
string[][] commands;
if (script)
{
    string directory = Path.Combine(root, ".artifacts", "migrations", args[1]);
    string up = Path.Combine(directory, $"{provider}.up.sql");
    string down = Path.Combine(directory, $"{provider}.down.sql");
    if (File.Exists(up) || File.Exists(down))
        throw new InvalidOperationException("Migration SQL output already exists; preserve it before choosing another generation.");
    Directory.CreateDirectory(directory);
    commands =
    [
        ["ef", "migrations", "script", args[3], args[1], "--output", up, .. shared],
        ["ef", "migrations", "script", args[1], args[3], "--output", down, .. shared]
    ];
}
else
    commands = [["ef", "migrations", "add", args[1], "--output-dir", "Migrations", .. shared]];
foreach (string[] command in commands)
{
    start.ArgumentList.Clear();
    foreach (string argument in command) start.ArgumentList.Add(argument);
    start.ArgumentList.Add("--");
    start.ArgumentList.Add("--DesignTime:ModelOnly=true");
    using var child = Process.Start(start) ?? throw new InvalidOperationException("Unable to start native EF artifact generation.");
    await child.WaitForExitAsync();
    if (child.ExitCode != 0) return child.ExitCode;
}
return 0;
