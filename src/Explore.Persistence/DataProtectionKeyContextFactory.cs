using Explore.Persistence.Database;
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
        if (DesignTimeModelConfiguration.IsRequested(args, bootstrap))
        {
            var modelBuilder = new DbContextOptionsBuilder<DataProtectionKeyContext>();
            PrimaryDatabaseProviderComposition.ConfigureDataProtectionModel(
                modelBuilder, DesignTimeModelConfiguration.ReadPrimary(bootstrap));
            return new DataProtectionKeyContext(modelBuilder.Options);
        }
        return MigrationDatabaseAcquisition.CreateDesignTimeContext(
            bootstrap, ["/database"],
            configuration => CreateDbContext(new ConfigurationBuilder().AddConfiguration(configuration)));
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
