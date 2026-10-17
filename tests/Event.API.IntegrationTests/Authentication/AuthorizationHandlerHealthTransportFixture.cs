using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using Google.Protobuf;
using Grpc.Health.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// External PDP boundary using the repository's loopback Kestrel HTTP/2 pattern.
/// The production channel, health client, normalization and compiler remain real.
/// </summary>
internal sealed class AuthorizationHandlerHealthTransportFixture : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
    internal ConcurrentQueue<string> Requests { get; } = new();
    internal string Endpoint => app.Urls.Single();
    internal string BareEndpoint => new Uri(Endpoint).Authority;
    internal CancellationToken Token => deadline.Token;

    private AuthorizationHandlerHealthTransportFixture(WebApplication app) => this.app = app;

    internal static async Task<AuthorizationHandlerHealthTransportFixture> CreateAsync(bool serving)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listener => listener.Protocols = HttpProtocols.Http2));
        var app = builder.Build();
        var fixture = new AuthorizationHandlerHealthTransportFixture(app);
        app.Run(async context =>
        {
            fixture.Requests.Enqueue(context.Request.Path);
            if (context.Request.Path != "/grpc.health.v1.Health/Check")
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            var payload = new HealthCheckResponse
            {
                Status = serving ? HealthCheckResponse.Types.ServingStatus.Serving
                    : HealthCheckResponse.Types.ServingStatus.NotServing
            }.ToByteArray();
            var frame = new byte[5 + payload.Length];
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length);
            payload.CopyTo(frame, 5);
            context.Response.ContentType = "application/grpc";
            context.Response.DeclareTrailer("grpc-status");
            await context.Response.Body.WriteAsync(frame, context.RequestAborted);
            context.Response.AppendTrailer("grpc-status", "0");
        });
        try
        {
            await app.StartAsync(fixture.Token);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        deadline.Dispose();
    }
}
