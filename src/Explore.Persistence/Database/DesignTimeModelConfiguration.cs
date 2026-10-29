using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Database;

internal static class DesignTimeModelConfiguration
{
    internal static bool IsRequested(string[] arguments, IConfiguration bootstrap)
    {
        if (!new ConfigurationBuilder().AddCommandLine(arguments).Build()
                .GetValue<bool>("DesignTime:ModelOnly"))
            return false;
        string? environment = bootstrap["DOTNET_ENVIRONMENT"] ?? bootstrap["ASPNETCORE_ENVIRONMENT"];
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Model-only scaffolding requires Development or Testing.");
        return true;
    }

    internal static PrimaryDatabaseConnectionOptions ReadPrimary(IConfiguration bootstrap)
    {
        string? selected = bootstrap["Database:Provider"] ?? bootstrap["DATABASE_PROVIDER"];
        if (!Enum.TryParse(selected, true, out PrimaryDatabaseProvider provider) || !Enum.IsDefined(provider))
            throw new InvalidOperationException("Model-only scaffolding requires an explicit supported provider.");
        Version? version = null;
        string? declaredVersion = bootstrap["Database:ServerVersion"] ?? bootstrap["DATABASE_SERVER_VERSION"];
        if (provider is PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb
            && !Version.TryParse(declaredVersion, out version))
            throw new InvalidOperationException("Model-only scaffolding requires an explicit MySQL or MariaDB server version.");
        return new PrimaryDatabaseConnectionOptions
        {
            Provider = provider,
            Role = PrimaryDatabaseRole.Migrator,
            ServerFlavor = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                _ => null
            },
            ServerVersion = version
        };
    }
}
