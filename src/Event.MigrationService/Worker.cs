using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Database;

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
            await ExternalIdentityDatabaseMigrator.MigrateAsync(
                externalIdentity, configuration, stoppingToken);
        }

        await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                environment,
                configuration,
                migrationDatabaseOptions,
                logger,
                cancellationToken: stoppingToken);

        lifetime.StopApplication();
    }
}
