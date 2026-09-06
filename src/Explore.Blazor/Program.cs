// ABOUTME: Thin Split-profile Blazor BFF composition root over reusable owning-assembly host modules.
// ABOUTME: Retains Program-based WebApplicationFactory compatibility and caller-owned shutdown state.

using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;

const BlazorHostProfile hostProfile = BlazorHostProfile.Split;
var shutdownState = new GracefulShutdownState();
using var shutdownCts = shutdownState.CancellationTokenSource;
var builder = WebApplication.CreateBuilder(args);
builder.AddBlazorHostServices(hostProfile, shutdownState);

await using var app = builder.Build();
await app.InitializeBlazorHostAsync(hostProfile);
app.UseBlazorHostMiddleware(hostProfile, shutdownState);
app.MapBlazorHostEndpoints(hostProfile);
await app.RunAsync();
