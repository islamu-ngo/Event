// ABOUTME: Runs reusable API lifetime registration and ordered pre-start initialization.
// ABOUTME: Preserves migrations, privacy gating, setup-secret initialization, and 25-second shutdown behavior.

using Explore.API.BackgroundServices;
using Explore.API.Extensions;
using Explore.Application.Contracts.Services;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Infrastructure.Services;
using Explore.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Explore.API.Hosting;

public static class ApiHostStartupExtensions
{
    public const int GracefulShutdownSeconds = 25;

    public static async Task RunApiHostStartupAsync(
        this WebApplication app,
        ApiHostCompositionState state,
        CancellationTokenSource shutdownCts,
        Action markShuttingDown)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(shutdownCts);
        ArgumentNullException.ThrowIfNull(markShuttingDown);

        var appLifetime = app.Lifetime;
        var appLogger = app.Logger;
        appLifetime.ApplicationStopping.Register(() =>
        {
            markShuttingDown();
            appLogger.LogInformation(
                "SIGTERM received. Starting graceful shutdown. Health checks return 503. " +
                "Accepting requests for {Seconds} more seconds...",
                GracefulShutdownSeconds);
        });

        app.Services.GetRequiredService<HostProcessSignalSubscriptions>().Register((_, eventArgs) =>
        {
            appLogger.LogWarning("SIGINT received. Initiating graceful shutdown...");
            eventArgs.Cancel = true;
            shutdownCts.Cancel();

            try
            {
                appLifetime.StopApplication();
            }
            catch (ObjectDisposedException)
            {
            }
        });

        if (!app.Environment.IsEnvironment("Testing") && !state.IsOpenApiGeneration)
        {
            using var scope = app.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                if (app.Environment.IsDevelopment() && state.OwnsDevelopmentMigrations)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                    var startupSequence = scope.ServiceProvider
                        .GetRequiredService<IConfigurationManifestPostMigrationSequence>();
                    await startupSequence.RunAsync(
                        async cancellationToken =>
                        {
                            if (db.Database.IsRelational())
                            {
                                logger.LogInformation("Applying database migrations...");
                                await ExploreDatabaseMigrator.MigrateAsync(
                                    db,
                                    app.Configuration,
                                    cancellationToken);
                                await PostgresModelConstraintApplier.ApplyAsync(
                                    db,
                                    cancellationToken);
                                await PostgresTenantRowLevelSecurityModel.ApplyAsync(
                                    db,
                                    cancellationToken);
                                logger.LogInformation(
                                    "Database migrations completed successfully.");
                            }
                            else
                            {
                                logger.LogInformation(
                                    "Skipping database migrations because provider {ProviderName} is non-relational.",
                                    db.Database.ProviderName ?? "(unknown)");
                            }

                            var seedDevelopmentData =
                                !app.Configuration.GetValue<bool>(
                                    "Testing:DisableDevelopmentDataSeed");
                            await DatabaseSeeder.SeedAsync(
                                db,
                                app.Environment,
                                seedDevelopmentData,
                                app.Configuration,
                                cancellationToken);
                            logger.LogInformation("Database seeding completed.");
                        },
                        shutdownCts.Token);
                }
                else
                {
                    logger.LogInformation(
                        "This API startup path does not own migration or configuration-manifest bootstrap.");
                }

                if (state.UseQuartzScheduler)
                {
                    logger.LogInformation("Applying Quartz scheduler schema...");
                    await app.ApplyQuartzSchedulerSchemaAsync(shutdownCts.Token);
                    logger.LogInformation("Quartz scheduler schema applied successfully.");
                }
            }
            catch (Exception exception)
            {
                logger.LogCritical(exception, "Database migration failed. Application cannot start.");
                throw;
            }
        }

        if (!app.Environment.IsEnvironment("Testing") &&
            !state.IsOpenApiGeneration &&
            state.OwnsDevelopmentMigrations)
        {
            await app.PrepareConfiguredAdministratorBootstrapAsync(shutdownCts.Token);
        }

        if ((!app.Environment.IsEnvironment("Testing") ||
             app.Configuration.GetValue<bool>("Testing:EnablePrivacyErasureStartupGate")) &&
            !state.IsOpenApiGeneration)
        {
            await PrivacyErasureStartupGate.RunAsync(app.Services, shutdownCts.Token);
        }

        if (!state.IsOpenApiGeneration)
        {
            var setupSecretProvider = app.Services.GetRequiredService<ISetupSecretProvider>();
            await setupSecretProvider.InitializeAsync();
            if (setupSecretProvider.IsSetupModeActive)
            {
                if (!setupSecretProvider.IsSetupSecretRequired)
                {
                    app.Logger.LogInformation(
                        "[SetupSecret] Interactive setup-secret validation disabled by trusted managed provisioning configuration. Setup endpoints still reject anonymous setup-secret access.");
                }
                else if (setupSecretProvider.IsFromEnvironmentVariable)
                {
                    app.Logger.LogInformation("[SetupSecret] SETUP_SECRET loaded from environment variable.");
                }
                else
                {
                    if (setupSecretProvider.GeneratedSecretFilePath is not null)
                    {
                        app.Logger.LogWarning(
                            "[SetupMode] Instance is unclaimed. Retrieve the generated setup secret from the Docker host with: " +
                            "docker cp <container-name>:{SetupSecretFilePath} ./setup-secret",
                            setupSecretProvider.GeneratedSecretFilePath);
                    }
                    else
                    {
                        app.Logger.LogWarning(
                            "[SetupMode] Instance is unclaimed. Auto-generated setup secret active. " +
                            "Configure SETUP_SECRET and restart before visiting /setup.");
                    }
                }
            }
            else
            {
                app.Logger.LogInformation(
                    "[SetupSecret] Instance onboarding already completed. Setup mode inactive.");
            }
        }
    }

    public static async Task PrepareConfiguredAdministratorBootstrapAsync(
        this WebApplication app,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var provider = ActivatorUtilities.CreateInstance<
            ConfiguredAdministratorBootstrapProvider>(scope.ServiceProvider);
        var runner = ActivatorUtilities.CreateInstance<
            ConfiguredAdministratorBootstrapStartupRunner>(
                scope.ServiceProvider,
                provider);
        await runner.PrepareAsync(cancellationToken);
    }
}
