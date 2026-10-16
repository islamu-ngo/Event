using System.Collections.Concurrent;
using System.Globalization;
using Explore.API.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Event.Api.IntegrationTests.Features.Middleware;

public sealed class CorrelationDiagnosticConfidentialityTests
{
    [Test]
    [Arguments("X-Correlation-ID", false)]
    [Arguments("X-Request-ID", false)]
    [Arguments("both", false)]
    [Arguments("X-Correlation-ID", true)]
    [Arguments("X-Request-ID", true)]
    [Arguments("both", true)]
    public async Task CallerHeadersNeverBecomeResponseOrLogCorrelation(string headerSelection, bool controlCharacters)
    {
        string correlationCanary = Guid.CreateVersion7().ToString("N");
        string requestCanary = Guid.CreateVersion7().ToString("N");
        string correlationHeader = controlCharacters
            ? correlationCanary + "\r\nInjected-Correlation: " + correlationCanary + "\t"
            : correlationCanary;
        string requestHeader = controlCharacters
            ? requestCanary + "\r\nInjected-Request: " + requestCanary + "\t"
            : requestCanary;
        var sink = new DiagnosticSink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var observations = new ConcurrentQueue<(string CorrelationId, bool ResponseStarted)>();
        using var host = await new HostBuilder()
            .UseEnvironment("Testing")
            .ConfigureWebHost(web => web.UseTestServer().Configure(app =>
            {
                app.UseCorrelationId();
                app.Run(async context =>
                {
                    string correlationId = (string)context.Items["CorrelationId"]!;
                    // Use the actual Serilog ambient context, not ILogger scopes or an explicit property.
                    logger.Information("Downstream correlation confidentiality probe");
                    await context.Response.WriteAsync(correlationId, context.RequestAborted);
                    observations.Enqueue((correlationId, context.Response.HasStarted));
                });
            }))
            .StartAsync();
        using var server = host.GetTestServer();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var returnedIds = new List<string>();

        for (int request = 0; request < 2; request++)
        {
            // Raw headers bypass HttpClient's CRLF validation so the middleware receives both canaries.
            HttpContext response = await server.SendAsync(context =>
            {
                context.Request.Method = HttpMethods.Get;
                context.Request.Path = "/correlation-probe";
                if (headerSelection is "X-Correlation-ID" or "both")
                    context.Request.Headers["X-Correlation-ID"] = correlationHeader;
                if (headerSelection is "X-Request-ID" or "both")
                    context.Request.Headers["X-Request-ID"] = requestHeader;
            }, deadline.Token);

            using var reader = new StreamReader(response.Response.Body);
            string body = await reader.ReadToEndAsync(deadline.Token);
            string returnedId = response.Response.Headers["X-Correlation-ID"].ToString();
            await Assert.That(response.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
            await Assert.That(response.Response.Headers["X-Correlation-ID"].Count).IsEqualTo(1);
            await Assert.That(Guid.TryParseExact(returnedId, "N", out Guid parsed)).IsTrue();
            await Assert.That(parsed.Version).IsEqualTo(7);
            await Assert.That(returnedId).IsEqualTo(parsed.ToString("N"));
            await Assert.That(observations.TryDequeue(out var observation)).IsTrue();
            await Assert.That(observation.ResponseStarted).IsTrue();
            await Assert.That(returnedId).IsEqualTo(observation.CorrelationId);
            await Assert.That(body).IsEqualTo(returnedId);
            await Assert.That(returnedId).IsNotEqualTo(correlationCanary);
            await Assert.That(returnedId).IsNotEqualTo(requestCanary);

            await Assert.That(sink.Events.TryDequeue(out LogEvent? exported)).IsTrue();
            await Assert.That(exported).IsNotNull();
            await Assert.That(((ScalarValue)exported!.Properties["CorrelationId"]).Value)
                .IsEqualTo(returnedId);

            using var serializedLog = new StringWriter(CultureInfo.InvariantCulture);
            new JsonFormatter().Format(exported, serializedLog);
            string diagnostics = serializedLog + exported.RenderMessage(CultureInfo.InvariantCulture)
                + body + string.Join('\n', response.Response.Headers.Select(header => header.Key + ": " + header.Value));
            await Assert.That(diagnostics).DoesNotContain(correlationCanary);
            await Assert.That(diagnostics).DoesNotContain(requestCanary);
            returnedIds.Add(returnedId);
        }

        await Assert.That(returnedIds[0]).IsNotEqualTo(returnedIds[1]);
        await Assert.That(observations.IsEmpty).IsTrue();
        await Assert.That(sink.Events.IsEmpty).IsTrue();
    }

    private sealed class DiagnosticSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
