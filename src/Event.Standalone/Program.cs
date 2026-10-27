using Explore.API.Hosting;
using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.ServiceDefaults.Secrets;
using Explore.Application.Features.ConfigurationManifest.Application;
using Event.Standalone.Hosting;
using Event.Standalone.Middleware;
using System.Runtime.Loader;

const BlazorHostProfile hostProfile = BlazorHostProfile.Combined;
var shutdownState = new GracefulShutdownState();
using var shutdownCts = shutdownState.CancellationTokenSource;
var builder = WebApplication.CreateBuilder(args);
ExploreDatabaseMigrator.EnsureAgentBrowserAdmission(builder.Configuration, builder.Environment, isStandaloneHost: true);
await using var secretAuthority = builder.Environment.IsEnvironment("Testing")
    || ApiHostServiceCollectionExtensions.IsBuildTimeOpenApiGeneration ? null
    : await SecretAuthorityBootstrap.OpenAsync(builder.Configuration, builder.Environment.EnvironmentName,
        SecretConsumerRole.Combined,
        [new("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
        new SecretAuthorityLoginAttempt(), cancellationToken: shutdownCts.Token);
if (secretAuthority is not null)
    await secretAuthority.StartAuthenticationMaintenanceAsync(shutdownCts.Token);
if (!builder.Environment.IsEnvironment("Testing"))
{
    foreach (var migrationAssembly in Directory.EnumerateFiles(
                 AppContext.BaseDirectory,
                 "Explore.Persistence*.Migrations.*.dll"))
    {
        AssemblyLoadContext.Default.LoadFromAssemblyPath(migrationAssembly);
    }
}

await builder.PrepareApiHostSecretsAsync(secretAuthority, SecretConsumerRole.Combined, shutdownCts.Token);
var apiHost = builder.AddApiHostServices(
    () => shutdownState.IsShuttingDown,
    ownsDevelopmentMigrations: false,
    secretConsumer: SecretConsumerRole.Combined,
    ownsConfigurationManifestStartup: false);
await builder.PrepareBlazorHostSecretsAsync(hostProfile, secretAuthority, shutdownCts.Token);
builder.AddBlazorHostServices(hostProfile, shutdownState);
builder.Services.AddCombinedApiDataProtection();
builder.Services.AddCombinedApiBridge();
builder.AddStandaloneSchedulerDashboard();

builder.Host.ConfigureContainer<IServiceCollection>((context, _) =>
{
    var primaryDatabase = PrimaryDatabaseConfiguration.BindRuntime(context.Configuration);
    if (primaryDatabase.Provider == PrimaryDatabaseProvider.Sqlite &&
        context.Configuration.GetValue("Hosting:ReplicaCount", 1) != 1)
    {
        throw new InvalidOperationException(
            "Hosting:ReplicaCount must be 1 when Database:Provider=Sqlite. Event.Standalone local SQLite storage supports exactly one application replica.");
    }
});

await using var app = builder.Build();

await ExternalIdentityDatabaseMigrator.MigrateIfExternalAsync(
    app.Configuration,
    app.Logger,
    shutdownCts.Token);

await using (var scope = app.Services.CreateAsyncScope())
{
    var startupSequence = scope.ServiceProvider
        .GetRequiredService<IConfigurationManifestPostMigrationSequence>();
    await startupSequence.RunAsync(
        async cancellationToken =>
        {
            if (app.Environment.IsEnvironment("Testing"))
            {
                var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                await SqliteDatabaseInitializer.InitializeAsync(database, cancellationToken);
                return;
            }

            await ExploreDatabaseMigrator.MigrateAndSeedAsync(
                app.Environment,
                app.Configuration,
                PrimaryDatabaseConfiguration.BindMigrator(app.Configuration),
                app.Logger,
                cancellationToken: cancellationToken);
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
