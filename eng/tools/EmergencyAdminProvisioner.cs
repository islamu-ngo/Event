#:project ../../src/Explore.Persistence/Explore.Persistence.csproj
#:project ../../src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj
#:project ../../src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj
#:project ../../src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj
#:property RestorePackagesWithLockFile=false
#:property PublishAot=false

using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Operations;
using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

const int Success = 0;
const int InvalidUsage = 64;
const int InvalidData = 65;
const int InternalFailure = 70;
const int Cancelled = 130;

if (!TryParseArguments(
        args,
        out string? rawDid,
        out bool apply,
        out bool reassign,
        out bool showHelp))
{
    PrintUsage();
    return InvalidUsage;
}

if (showHelp)
{
    PrintUsage();
    return Success;
}

if (!apply || !AtprotoDid.TryParse(rawDid, out AtprotoDid did))
{
    PrintUsage();
    return InvalidUsage;
}

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    IConfiguration bootstrap = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build();
    IConfiguration configuration = new ConfigurationBuilder()
        .AddConfiguration(SecretAuthorityConfiguration.Build(
            bootstrap,
            SecretAuthorityConfiguration.GetEnvironmentName(bootstrap),
            "/database"))
        .Build();
    PrimaryDatabaseConnectionOptions migratorOptions =
        PrimaryDatabaseConfiguration.BindMigrator(configuration);

    var migrationOptionsBuilder =
        new DbContextOptionsBuilder<ExploreDbContext>();
    PrimaryDatabaseProviderComposition.ConfigureApplication(
        migrationOptionsBuilder,
        migratorOptions);
    await using (var migrationContext =
                 new ExploreDbContext(migrationOptionsBuilder.Options))
    {
        IReadOnlyList<string> pending = (await migrationContext.Database
                .GetPendingMigrationsAsync(cancellation.Token))
            .ToArray();
        if (pending.Count != 0)
        {
            Console.WriteLine(
                "instance-administrator-recovery: database-not-current");
            return InvalidData;
        }
    }

    PrimaryDatabaseConnectionOptions recoveryOptions =
        migratorOptions with { Role = PrimaryDatabaseRole.Runtime };
    var recoveryOptionsBuilder =
        new DbContextOptionsBuilder<ExploreDbContext>();
    PrimaryDatabaseProviderComposition.ConfigureApplication(
        recoveryOptionsBuilder,
        recoveryOptions);
    await using var recoveryContext =
        new ExploreDbContext(recoveryOptionsBuilder.Options);
    var operation =
        new EmergencyAdminProvisioningOperation(recoveryContext);
    EmergencyAdminProvisioningOutcome outcome =
        await operation.GrantAsync(
            did,
            cancellation.Token,
            reassign);

    return outcome switch
    {
        EmergencyAdminProvisioningOutcome.Granted =>
            WriteOutcome("granted", Success),
        EmergencyAdminProvisioningOutcome.Reassigned =>
            WriteOutcome("reassigned", Success),
        EmergencyAdminProvisioningOutcome.AlreadyPresent =>
            WriteOutcome("already-present", Success),
        EmergencyAdminProvisioningOutcome.TargetNotFound =>
            WriteOutcome("target-not-found", InvalidData),
        EmergencyAdminProvisioningOutcome.InvalidRoleAuthority =>
            WriteOutcome("role-authority-invalid", InvalidData),
        _ => WriteOutcome("failed", InternalFailure),
    };
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("instance-administrator-recovery: cancelled");
    return Cancelled;
}
catch
{
    Console.WriteLine("instance-administrator-recovery: failed");
    return InternalFailure;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static int WriteOutcome(string outcome, int exitCode)
{
    Console.WriteLine($"instance-administrator-recovery: {outcome}");
    return exitCode;
}

static bool TryParseArguments(
    string[] arguments,
    out string? did,
    out bool apply,
    out bool reassign,
    out bool showHelp)
{
    did = null;
    apply = false;
    reassign = false;
    showHelp = false;

    for (int index = 0; index < arguments.Length; index++)
    {
        string argument = arguments[index];
        switch (argument)
        {
            case "--help":
                if (showHelp)
                {
                    return false;
                }

                showHelp = true;
                break;
            case "--apply":
                if (apply)
                {
                    return false;
                }

                apply = true;
                break;
            case "--reassign":
                if (reassign)
                {
                    return false;
                }

                reassign = true;
                break;
            case "--grant-did":
                if (did is not null
                    || index + 1 >= arguments.Length
                    || arguments[index + 1].StartsWith('-'))
                {
                    return false;
                }

                did = arguments[++index];
                break;
            default:
                return false;
        }
    }

    return showHelp
        ? arguments.Length == 1
        : did is not null;
}

static void PrintUsage()
{
    Console.WriteLine(
        "Usage: dotnet run --file eng/tools/EmergencyAdminProvisioner.cs -- --grant-did <did> --apply [--reassign]");
    Console.WriteLine(
        "Grants instance administration to an existing exact AT Protocol binding; --reassign revokes other platform administrators.");
}
