using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Explore.Secrets.Database;
using Explore.Infrastructure.ConfigurationManifest;

namespace Event.MigrationService;

public sealed class Worker(IServiceProvider serviceProvider, IHostApplicationLifetime lifetime, IHostEnvironment environment, IConfiguration configuration, PrimaryDatabaseConnectionOptions migrationDatabaseOptions, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await MigrateAsync(stoppingToken);
        }
        catch
        {
            Environment.ExitCode = 1;
            throw;
        }
    }

    private async Task MigrateAsync(CancellationToken stoppingToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var externalIdentity = scope.ServiceProvider.GetService<ExternalIdentityDbContext>();
        if (externalIdentity is not null)
        {
            logger.LogInformation("Applying external Local Identity database migrations.");
            await externalIdentity.Database.MigrateAsync(stoppingToken);
        }

        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var startupSequence = scope.ServiceProvider
            .GetRequiredService<IConfigurationManifestPostMigrationSequence>();
        await startupSequence.RunAsync(
            cancellationToken => ExploreDatabaseMigrator.MigrateAndSeedAsync(
                db,
                environment,
                configuration,
                migrationDatabaseOptions,
                logger,
                cancellationToken),
            stoppingToken);

        lifetime.StopApplication();
    }
}
