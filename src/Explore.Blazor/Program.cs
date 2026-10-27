using Explore.Blazor.Extensions;
using Explore.Blazor.Hosting;
using Explore.ServiceDefaults.Secrets;

const BlazorHostProfile hostProfile = BlazorHostProfile.Split;
var shutdownState = new GracefulShutdownState();
using var shutdownCts = shutdownState.CancellationTokenSource;
var builder = WebApplication.CreateBuilder(args);
await using var secretAuthority = builder.Environment.IsEnvironment("Testing") ? null
    : await SecretAuthorityBootstrap.OpenAsync(builder.Configuration, builder.Environment.EnvironmentName,
        SecretConsumerRole.Bff,
        [new("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
        new SecretAuthorityLoginAttempt(), cancellationToken: shutdownCts.Token);
if (secretAuthority is not null)
    await secretAuthority.StartAuthenticationMaintenanceAsync(shutdownCts.Token);
await builder.PrepareBlazorHostSecretsAsync(hostProfile, secretAuthority, shutdownCts.Token);
builder.AddBlazorHostServices(hostProfile, shutdownState);

await using var app = builder.Build();
await app.InitializeBlazorHostAsync(hostProfile);
app.UseBlazorHostMiddleware(hostProfile, shutdownState);
app.MapBlazorHostEndpoints(hostProfile);
await app.RunAsync();
