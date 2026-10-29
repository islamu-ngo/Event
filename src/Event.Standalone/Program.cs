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
using HostDefaults = Microsoft.Extensions.Hosting.Extensions;

const BlazorHostProfile hostProfile = BlazorHostProfile.Combined;
var shutdownState = new GracefulShutdownState();
using var shutdownCts = shutdownState.CancellationTokenSource;
var builder = WebApplication.CreateBuilder(args);
HostDefaults.AssuranceStaticAssetSelection? assetSelection = null;
WebApplication? app = null;
SecretAuthorityAcquisition? secretAuthority = null;
var failures = new List<Exception>();
try
{
    if (builder.Environment.IsEnvironment("Assurance")
        && (builder.Configuration["ISLAMU_TEST_ROOT"] is not null || builder.Configuration["ISLAMU_TEST_RUN_ID"] is not null))
    {
        var paths = HostDefaults.ReadAssuranceManifestPaths(typeof(StandaloneHostMarker).Assembly);
        assetSelection = await builder.SelectAssuranceStaticAssetsAsync(paths.RuntimeManifestPath, paths.EndpointManifestPath);
    }
    ExploreDatabaseMigrator.EnsureAgentBrowserAdmission(builder.Configuration, builder.Environment, isStandaloneHost: true);
    secretAuthority = builder.Environment.IsEnvironment("Testing")
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

    app = builder.Build();

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
    await app.StartAsync();
    await app.WaitForShutdownAsync();
}
catch (Exception failure) { failures.Add(failure); }
try { if (app is not null) await app.DisposeAsync(); }
catch (Exception failure) { failures.Add(failure); }
try { if (secretAuthority is not null) await secretAuthority.DisposeAsync(); }
catch (Exception failure) { failures.Add(failure); }
try { assetSelection?.Dispose(); }
catch (Exception failure) { failures.Add(failure); }
if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
if (failures.Count > 1) throw new AggregateException(failures);

partial class Program;
