using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Identity;

public sealed class ExternalIdentityDbContextFactory
    : IDesignTimeDbContextFactory<ExternalIdentityDbContext>
{
    public ExternalIdentityDbContext CreateDbContext(string[] args)
    {
        IConfiguration bootstrap = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        var configurationBuilder = new ConfigurationBuilder().AddConfiguration(
            SecretAuthorityConfiguration.Build(
                bootstrap,
                SecretAuthorityConfiguration.GetEnvironmentName(bootstrap),
                "/database/identity"));

        return CreateDbContext(configurationBuilder);
    }

    public ExternalIdentityDbContext CreateDbContext(
        IConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        IConfiguration configuration = configurationBuilder.Build();
        var optionsBuilder = new DbContextOptionsBuilder<ExternalIdentityDbContext>();
        IdentityDatabaseProviderComposition.Configure(
            optionsBuilder,
            configuration,
            PrimaryDatabaseRole.Migrator);

        return new ExternalIdentityDbContext(optionsBuilder.Options);
    }
}
