// ABOUTME: Composes the API and Blazor owning host modules into one optional web process.
// ABOUTME: Owns one Combined profile, shutdown state, and ordered startup sequence.

using Explore.API.Hosting;
using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Explore.Infrastructure.ConfigurationManifest;
using Event.Standalone.Hosting;
using Event.Standalone.Middleware;
using System.Runtime.Loader;

const BlazorHostProfile hostProfile = BlazorHostProfile.Combined;
var shutdownState = new GracefulShutdownState();
using var shutdownCts = shutdownState.CancellationTokenSource;
var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsEnvironment("Testing"))
{
    foreach (var migrationAssembly in Directory.EnumerateFiles(
                 AppContext.BaseDirectory,
                 "Explore.Persistence*.Migrations.*.dll"))
    {
        AssemblyLoadContext.Default.LoadFromAssemblyPath(migrationAssembly);
    }
}

var apiHost = builder.AddApiHostServices(
    () => shutdownState.IsShuttingDown,
    ownsDevelopmentMigrations: false);
builder.AddBlazorHostServices(hostProfile, shutdownState);
builder.Services.AddCombinedApiBridge();
builder.AddStandaloneSchedulerDashboard();

await using var app = builder.Build();
var primaryDatabase = PrimaryDatabaseConfiguration.BindRuntime(app.Configuration);
if (primaryDatabase.Provider == PrimaryDatabaseProvider.Sqlite &&
    app.Configuration.GetValue("Hosting:ReplicaCount", 1) != 1)
{
    throw new InvalidOperationException(
        "Hosting:ReplicaCount must be 1 when Database:Provider=Sqlite. Event.Standalone local SQLite storage supports exactly one application replica.");
}

await ExternalIdentityDatabaseMigrator.MigrateIfExternalAsync(
    app.Configuration,
    app.Logger,
    shutdownCts.Token);

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
    var startupSequence = scope.ServiceProvider
        .GetRequiredService<IConfigurationManifestPostMigrationSequence>();
    await startupSequence.RunAsync(
        async cancellationToken =>
        {
            if (app.Environment.IsEnvironment("Testing"))
            {
                await SqliteDatabaseInitializer.InitializeAsync(database, cancellationToken);
                return;
            }

            await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                database,
                app.Environment,
                app.Configuration,
                PrimaryDatabaseConfiguration.BindMigrator(app.Configuration),
                app.Logger,
                cancellationToken);
        },
        shutdownCts.Token);
}

if (!app.Environment.IsEnvironment("Testing") && !apiHost.IsOpenApiGeneration)
{
    await app.PrepareConfiguredAdministratorBootstrapAsync(shutdownCts.Token);
}

await app.RunApiHostStartupAsync(
    apiHost,
    shutdownCts,
    () => shutdownState.IsShuttingDown = true);
await app.InitializeBlazorHostAsync(hostProfile);
app.UseStandaloneHostMiddleware(apiHost, hostProfile, shutdownState);
app.MapStandaloneHostEndpoints(apiHost, hostProfile);
app.BindStandaloneInternalApiTransport();
app.Run();

partial class Program;
