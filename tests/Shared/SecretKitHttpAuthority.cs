using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Event.Testing.Secrets;

internal sealed class SecretKitHttpAuthority : IAsyncDisposable
{
    private readonly WebApplication _server;
    private int _logins;
    private int _reads;
    private int _renewals;
    private int _inventories;
    private readonly string _token = Guid.NewGuid().ToString("N");
    private SecretKitHttpAuthority(WebApplication server) => _server = server;
    internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    internal int Logins => Volatile.Read(ref _logins);
    internal int Reads => Volatile.Read(ref _reads);
    internal int Renewals => Volatile.Read(ref _renewals);
    internal int Inventories => Volatile.Read(ref _inventories);
    internal string[]? AdvertisedKeys { get; set; }
    internal Dictionary<string, string[]>? InventoryKeysByFolder { get; set; }
    internal Dictionary<string, int> InventoryStatusCodes { get; } = new(StringComparer.Ordinal);
    internal double MaximumTtl { get; set; } = 3600;
    internal string? BlockedKey { get; set; }
    internal string? ReturnedProject { get; set; }
    internal string ResourceId { get; set; } = Guid.NewGuid().ToString("N");
    internal int Version { get; set; } = 1;
    internal bool OmitRevisionEvidence { get; set; }
    internal bool SecretValueHidden { get; set; }
    internal int ReadStatusCode { get; set; } = StatusCodes.Status200OK;
    internal int RenewalStatusCode { get; set; } = StatusCodes.Status200OK;
    internal TaskCompletionSource RenewalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource RenewalReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool BlockRenewal { get; set; }
    internal string? ProviderMessage { get; set; }
    internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ReadReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<int, Task>? BeforeReadResponse { get; set; }
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
        server.MapPost("/api/v1/auth/token/renew", async context =>
        {
            Interlocked.Increment(ref authority._renewals);
            using var body = await System.Text.Json.JsonDocument.ParseAsync(
                context.Request.Body, cancellationToken: context.RequestAborted);
            if (!body.RootElement.TryGetProperty("accessToken", out var token)
                || token.GetString() != authority._token)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            authority.RenewalEntered.TrySetResult();
            if (authority.BlockRenewal)
                await authority.RenewalReleased.Task.WaitAsync(context.RequestAborted);
            if (authority.RenewalStatusCode != StatusCodes.Status200OK)
            {
                context.Response.StatusCode = authority.RenewalStatusCode;
                return;
            }
            await context.Response.WriteAsJsonAsync(new
            {
                accessToken = authority._token,
                accessTokenMaxTTL = authority.MaximumTtl,
                expiresIn = 600,
                tokenType = "Bearer"
            }, context.RequestAborted);
        });
        server.MapGet("/api/v4/secrets", async context =>
        {
            Interlocked.Increment(ref authority._inventories);
            string folder = context.Request.Query["secretPath"].ToString();
            if (authority.InventoryStatusCodes.TryGetValue(folder, out int statusCode))
            {
                context.Response.StatusCode = statusCode;
                return;
            }
            string[] keys = authority.InventoryKeysByFolder is { } folders
                ? folders.GetValueOrDefault(folder, [])
                : authority.AdvertisedKeys ?? authority.Values.Keys.ToArray();
            await context.Response.WriteAsJsonAsync(new
            {
                secrets = keys.Select(key => new
                {
                    id = authority.OmitRevisionEvidence ? null : authority.ResourceId,
                    workspace = authority.ReturnedProject ?? context.Request.Query["projectId"].ToString(),
                    environment = context.Request.Query["environment"].ToString(),
                    secretPath = context.Request.Query["secretPath"].ToString(),
                    secretKey = key,
                    secretValueHidden = true,
                    type = "shared",
                    version = authority.OmitRevisionEvidence ? (int?)null : authority.Version
                }).ToArray()
            }, context.RequestAborted);
        });
        server.MapGet("/api/v4/secrets/{key}", async context =>
        {
            int readNumber = Interlocked.Increment(ref authority._reads);
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
            var response = new
            {
                secret = new
                {
                    id = authority.OmitRevisionEvidence ? null : authority.ResourceId,
                    workspace = authority.ReturnedProject ?? context.Request.Query["projectId"].ToString(),
                    environment = context.Request.Query["environment"].ToString(),
                    secretPath = context.Request.Query["secretPath"].ToString(),
                    secretKey = key,
                    secretValue = material,
                    secretValueHidden = authority.SecretValueHidden,
                    type = "shared",
                    version = authority.OmitRevisionEvidence ? (int?)null : authority.Version
                }
            };
            if (authority.BeforeReadResponse is { } beforeResponse)
                await beforeResponse(readNumber).WaitAsync(context.RequestAborted);
            await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
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
