using Explore.API.Hosting;
using Explore.ServiceDefaults.Secrets;

using var shutdownCts = new CancellationTokenSource();
var shutdownState = 0;
var builder = WebApplication.CreateBuilder(args);
await using var secretAuthority = builder.Environment.IsEnvironment("Testing")
    || ApiHostServiceCollectionExtensions.IsBuildTimeOpenApiGeneration ? null
    : await SecretAuthorityBootstrap.OpenAsync(builder.Configuration, builder.Environment.EnvironmentName,
        SecretConsumerRole.Api,
        [new("/runtime/api/authentication/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
        new SecretAuthorityLoginAttempt(), cancellationToken: shutdownCts.Token);
if (secretAuthority is not null)
    await secretAuthority.StartAuthenticationMaintenanceAsync(shutdownCts.Token);
await builder.PrepareApiHostSecretsAsync(secretAuthority, SecretConsumerRole.Api, shutdownCts.Token);
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
