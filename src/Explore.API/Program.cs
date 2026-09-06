// ABOUTME: Thin API executable composition root that delegates to reusable owning-assembly host modules.
// ABOUTME: Retains Program-based WebApplicationFactory compatibility and caller-owned shutdown state.

using Explore.API.Hosting;

using var shutdownCts = new CancellationTokenSource();
var shutdownState = 0;
var builder = WebApplication.CreateBuilder(args);
var apiHost = builder.AddApiHostServices(() => Volatile.Read(ref shutdownState) != 0);
await using var app = builder.Build();

await app.RunApiHostStartupAsync(
    apiHost,
    shutdownCts,
    () => Volatile.Write(ref shutdownState, 1));
app.UseApiHostMiddleware(apiHost);
app.MapApiHostEndpoints(apiHost);
app.Run();

partial class Program;
