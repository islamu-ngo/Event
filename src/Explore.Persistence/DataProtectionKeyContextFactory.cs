using Explore.Persistence.Database;
using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence;

public sealed class DataProtectionKeyContextFactory : IDesignTimeDbContextFactory<DataProtectionKeyContext>
{
    public DataProtectionKeyContext CreateDbContext(string[] args)
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

    public DataProtectionKeyContext CreateDbContext(IConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        var configuration = configurationBuilder.Build();

        var databaseOptions = PrimaryDatabaseConfiguration.BindMigrator(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<DataProtectionKeyContext>();
        var database = PrimaryDatabaseProviderComposition.ConfigureDataProtection(
            optionsBuilder,
            databaseOptions);

        Console.WriteLine($"[DesignTime] Data Protection database bootstrap source: {database.SafeSummary}");

        return new DataProtectionKeyContext(optionsBuilder.Options);
    }
}
