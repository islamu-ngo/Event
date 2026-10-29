using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;
using HostDefaults = Microsoft.Extensions.Hosting.Extensions;

const BlazorHostProfile hostProfile = BlazorHostProfile.Split;
var shutdownState = new GracefulShutdownState();
using var shutdownCts = shutdownState.CancellationTokenSource;
var builder = WebApplication.CreateBuilder(args);
HostDefaults.AssuranceStaticAssetSelection? assetSelection = null;
WebApplication? app = null;
var failures = new List<Exception>();
try
{
    if (builder.Environment.IsEnvironment("Assurance")
        && (builder.Configuration["ISLAMU_TEST_ROOT"] is not null || builder.Configuration["ISLAMU_TEST_RUN_ID"] is not null))
    {
        var paths = HostDefaults.ReadAssuranceManifestPaths(typeof(Program).Assembly);
        assetSelection = await builder.SelectAssuranceStaticAssetsAsync(paths.RuntimeManifestPath, paths.EndpointManifestPath);
    }
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
try { assetSelection?.Dispose(); }
catch (Exception failure) { failures.Add(failure); }
if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
if (failures.Count > 1) throw new AggregateException(failures);
