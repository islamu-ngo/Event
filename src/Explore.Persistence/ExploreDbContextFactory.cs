using Explore.Persistence.Database;
using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence;

/// <summary>
/// Design-time factory for <see cref="ExploreDbContext"/> used by the EF Core tooling
/// (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>, etc.).
/// </summary>
/// <remarks>
/// Explicit structured <c>Database:Migrator</c> settings take priority.
/// </remarks>
public class ExploreDbContextFactory : IDesignTimeDbContextFactory<ExploreDbContext>
{
    public ExploreDbContext CreateDbContext(string[] args)
    {
        IConfiguration bootstrap = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        var configurationBuilder = new ConfigurationBuilder().AddConfiguration(
            SecretAuthorityConfiguration.Build(
                bootstrap,
                SecretAuthorityConfiguration.GetEnvironmentName(bootstrap),
                "/database",
                "/database/erasure"));

        return CreateDbContext(configurationBuilder);
    }

    public ExploreDbContext CreateDbContext(IConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        var configuration = configurationBuilder.Build();

        var databaseOptions = PrimaryDatabaseConfiguration.BindMigrator(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<ExploreDbContext>();
        var database = PrimaryDatabaseProviderComposition.ConfigureApplication(
            optionsBuilder,
            databaseOptions);

        Console.WriteLine($"[DesignTime] Database bootstrap source: {database.SafeSummary}");

        return new ExploreDbContext(optionsBuilder.Options);
    }
}
