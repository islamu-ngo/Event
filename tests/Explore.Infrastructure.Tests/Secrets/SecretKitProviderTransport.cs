using SecretKit.Core;
using SecretKit.Infisical;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Tests.Secrets;

internal sealed class SecretKitProviderTransport : ISecretReader
{
    internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public SecretAuthority Authority { get; } = new();

    public ValueTask<SecretOutcome<SecretRead>> ReadAsync(
        SecretReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = (InfisicalSecretAddress)reference.Address;
        if (!Values.TryGetValue(address.Key, out string? value))
            return ValueTask.FromResult(SecretOutcome<SecretRead>.Failure(SecretStatus.Absent));
        return ValueTask.FromResult(SecretOutcome<SecretRead>.Success(new SecretRead(
            SecretMaterial.FromText(value),
            new SecretObservation(reference, "1", DateTimeOffset.UtcNow, SecretValidity.Valid, null))));
    }
}

internal sealed class SecretKitHttpAuthority : IAsyncDisposable
{
    private readonly WebApplication _server;
    private int _logins;
    private int _reads;
    private int _renewals;
    private readonly string _token = Guid.NewGuid().ToString("N");
    private SecretKitHttpAuthority(WebApplication server) => _server = server;
    internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    internal int Logins => Volatile.Read(ref _logins);
    internal int Reads => Volatile.Read(ref _reads);
    internal int Renewals => Volatile.Read(ref _renewals);
    internal double MaximumTtl { get; set; } = 3600;
    internal string? BlockedKey { get; set; }
    internal string? ReturnedProject { get; set; }
    internal int ReadStatusCode { get; set; } = StatusCodes.Status200OK;
    internal string? ProviderMessage { get; set; }
    internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ReadReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Uri Origin => new(_server.Urls.Single());

    internal static async Task<SecretKitHttpAuthority> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication server = builder.Build();
        var authority = new SecretKitHttpAuthority(server);
        server.MapPost("/api/v1/auth/universal-auth/login", async context =>
        {
            Interlocked.Increment(ref authority._logins);
            await context.Response.WriteAsJsonAsync(new
            {
                accessToken = authority._token,
                accessTokenMaxTTL = authority.MaximumTtl,
                expiresIn = 600,
                tokenType = "Bearer"
            }, context.RequestAborted);
        });
        server.MapPost("/api/v1/auth/token/renew-self", async context =>
        {
            Interlocked.Increment(ref authority._renewals);
            await context.Response.WriteAsJsonAsync(new
            {
                accessToken = authority._token,
                accessTokenMaxTTL = authority.MaximumTtl,
                expiresIn = 600,
                tokenType = "Bearer"
            }, context.RequestAborted);
        });
        server.MapGet("/api/v4/secrets/{key}", async context =>
        {
            Interlocked.Increment(ref authority._reads);
            string key = (string)context.Request.RouteValues["key"]!;
            if (string.Equals(key, authority.BlockedKey, StringComparison.Ordinal))
            {
                authority.ReadEntered.TrySetResult();
                await authority.ReadReleased.Task.WaitAsync(context.RequestAborted);
            }
            if (authority.ReadStatusCode != StatusCodes.Status200OK)
            {
                context.Response.StatusCode = authority.ReadStatusCode;
                await context.Response.WriteAsJsonAsync(new { message = authority.ProviderMessage },
                    context.RequestAborted);
                return;
            }
            if (!authority.Values.TryGetValue(key, out string? material))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await context.Response.WriteAsJsonAsync(new
            {
                secret = new
                {
                    id = Guid.NewGuid().ToString(),
                    workspace = authority.ReturnedProject ?? context.Request.Query["projectId"].ToString(),
                    environment = context.Request.Query["environment"].ToString(),
                    secretPath = context.Request.Query["secretPath"].ToString(),
                    secretKey = key,
                    secretValue = material,
                    secretValueHidden = false,
                    type = "shared",
                    version = 1
                }
            }, context.RequestAborted);
        });
        try
        {
            await server.StartAsync();
            return authority;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
        await _server.DisposeAsync();
    }
}
