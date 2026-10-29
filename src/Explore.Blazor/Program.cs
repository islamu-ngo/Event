using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;
using Explore.ServiceDefaults.Secrets;
using HostDefaults = Microsoft.Extensions.Hosting.Extensions;

const BlazorHostProfile hostProfile = BlazorHostProfile.Split;
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
        var paths = HostDefaults.ReadAssuranceManifestPaths(typeof(Program).Assembly);
        assetSelection = await builder.SelectAssuranceStaticAssetsAsync(paths.RuntimeManifestPath, paths.EndpointManifestPath);
    }
    secretAuthority = builder.Environment.IsEnvironment("Testing") ? null
        : await SecretAuthorityBootstrap.OpenAsync(builder.Configuration, builder.Environment.EnvironmentName,
            SecretConsumerRole.Bff,
            [new("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
            new SecretAuthorityLoginAttempt(), cancellationToken: shutdownCts.Token);
    if (secretAuthority is not null)
        await secretAuthority.StartAuthenticationMaintenanceAsync(shutdownCts.Token);
    await builder.PrepareBlazorHostSecretsAsync(hostProfile, secretAuthority, shutdownCts.Token);
    builder.AddBlazorHostServices(hostProfile, shutdownState);
    app = builder.Build();
    await app.InitializeBlazorHostAsync(hostProfile);
    app.UseBlazorHostMiddleware(hostProfile, shutdownState);
    app.MapBlazorHostEndpoints(hostProfile);
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
