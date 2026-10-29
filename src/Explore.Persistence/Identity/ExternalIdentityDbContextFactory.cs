using Explore.Persistence.Database;
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
        if (DesignTimeModelConfiguration.IsRequested(args, bootstrap))
        {
            string? selected = bootstrap["IdentityDatabase:Provider"] ?? bootstrap["IDENTITY_DATABASE_PROVIDER"];
            if (!Enum.TryParse(selected, true, out PrimaryDatabaseProvider provider) || !Enum.IsDefined(provider))
                throw new InvalidOperationException("Model-only scaffolding requires an explicit supported Identity provider.");
            string? schema = bootstrap["IdentityDatabase:Schema"] ?? bootstrap["IDENTITY_DATABASE_SCHEMA"];
            if (string.IsNullOrEmpty(schema) || schema.Length > 63
                || !(char.IsAsciiLetter(schema[0]) || schema[0] == '_')
                || schema.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                throw new InvalidOperationException("Model-only scaffolding requires an explicit valid Identity schema.");
            Version? version = null;
            if (provider is PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb
                && !Version.TryParse(bootstrap["IdentityDatabase:ServerVersion"]
                    ?? bootstrap["IDENTITY_DATABASE_SERVER_VERSION"], out version))
                throw new InvalidOperationException("Model-only Identity scaffolding requires an explicit MySQL or MariaDB server version.");
            var modelBuilder = new DbContextOptionsBuilder<ExternalIdentityDbContext>();
            IdentityDatabaseProviderComposition.ConfigureModel(modelBuilder, provider, schema, version);
            return new ExternalIdentityDbContext(modelBuilder.Options);
        }
        return MigrationDatabaseAcquisition.CreateDesignTimeContext(
            bootstrap, ["/database/identity"],
            configuration => CreateDbContext(new ConfigurationBuilder().AddConfiguration(configuration)));
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
