using System.Collections.Concurrent;
using System.Diagnostics;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Middleware;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceRequestLoggingTests
{
    [Test]
    [Arguments("api/eventresource/{id:guid}/content")]
    [Arguments("api/eventresource/{id:guid}/management")]
    [Arguments("api/event/{id:guid}/resources")]
    [Arguments("api/storageobject/{id:guid}/content")]
    [Arguments("api/storageobject/upload-sessions/{id:guid}/content")]
    public async Task ResourceAndStorageLogsOmitCallerSelectedIdentifiers(string route)
    {
        Guid identifier = Guid.CreateVersion7();
        var logger = new Capture();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/" + route.Replace("{id:guid}", identifier.ToString("D"), StringComparison.Ordinal);
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse(route), 0, EndpointMetadataCollection.Empty, "resource boundary"));
        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);
        await middleware.InvokeAsync(context, Substitute.For<ITenantContextAccessor>());
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        await Assert.That(logger.Entries.Single()).DoesNotContain(identifier.ToString("D"));
        await Assert.That(logger.Entries.Single()).Contains("/" + route);
    }

    [Test]
    [Arguments("api/eventresource/{id:guid}/content", "api/eventresource/{0}/content")]
    [Arguments("api/storageobject/{id:guid}/content", "api/storageobject/{0}/content")]
    public async Task FrameworkTraceExporterAndRequestLoggerRedactSensitiveRoutes(
        string routeTemplate, string requestTemplate)
    {
        Guid identifier = Guid.CreateVersion7();
        string title = $"protected-title-{Guid.CreateVersion7():N}";
        string credential = $"credential-{Guid.CreateVersion7():N}";
        var diagnostics = new HttpDiagnosticsCapture(routeTemplate);
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(logCapture: diagnostics);
        var exporter = new SensitiveRouteActivityExporter(routeTemplate);
        using var hosted = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
                    ["Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information"
                }));
            builder.ConfigureTestServices(services => services.AddOpenTelemetry().WithTracing(tracing =>
                tracing.AddProcessor(new SimpleActivityExportProcessor(exporter))));
        });
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        string path = "/" + string.Format(System.Globalization.CultureInfo.InvariantCulture,
            requestTemplate, identifier) + $"?title={title}&token={credential}";

        using var response = await client.GetAsync(path);
        try
        {
            await diagnostics.Observed.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException($"Missing request log for {routeTemplate}; status {(int)response.StatusCode}. "
                + string.Join('\n', diagnostics.Values.Where(value =>
                    value.StartsWith("Explore.API.Middleware.RequestLoggingMiddleware|", StringComparison.Ordinal)
                    || value.StartsWith("Microsoft.AspNetCore.Hosting.Diagnostics|", StringComparison.Ordinal)).Take(12)), exception);
        }
        await exporter.Observed.WaitAsync(TimeSpan.FromSeconds(10));

        string emitted = string.Join('\n', diagnostics.Values.Concat(exporter.Values));
        await Assert.That(emitted).Contains("/" + routeTemplate);
        await Assert.That(emitted).DoesNotContain(identifier.ToString("D"));
        await Assert.That(emitted).DoesNotContain(title);
        await Assert.That(emitted).DoesNotContain(credential);
    }

    [Test]
    public async Task UnmatchedResourcePathDoesNotExportCallerSelectedSegments()
    {
        Guid identifier = Guid.CreateVersion7();
        string privateSegment = $"private-token-{Guid.CreateVersion7():N}";
        string credential = $"credential-{Guid.CreateVersion7():N}";
        var diagnostics = new HttpDiagnosticsCapture(string.Empty);
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(logCapture: diagnostics);
        var exporter = new SensitiveRouteActivityExporter(null);
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddOpenTelemetry().WithTracing(tracing =>
                tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)))));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        var logObserved = diagnostics.Observed;
        var spanObserved = exporter.Observed;

        using var response = await client.GetAsync(
            $"/api/eventresource/{identifier}/content/{privateSegment}?token={credential}");
        await Task.WhenAll(logObserved, spanObserved).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That((int)response.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        string emitted = string.Join('\n', diagnostics.Values.Concat(exporter.Values));
        await Assert.That(emitted).DoesNotContain(identifier.ToString("D"));
        await Assert.That(emitted).DoesNotContain(privateSegment);
        await Assert.That(emitted).DoesNotContain(credential);
    }

    [Test]
    public async Task UnmatchedResourceActivityClearsLegacyHttpUrl()
    {
        Guid identifier = Guid.CreateVersion7();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/api/eventresource/{identifier}/content/private-token";
        string rawUrl = $"https://localhost{context.Request.Path}?token=private-token";
        using var activity = new Activity("unmatched resource").Start();
        activity.SetTag("url.path", context.Request.Path.Value);
        activity.SetTag("http.url", rawUrl);
        var logger = new Capture();
        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context, Substitute.For<ITenantContextAccessor>());

        await Assert.That(activity.GetTagItem("url.path")).IsEqualTo("route-unresolved");
        await Assert.That(activity.GetTagItem("http.url")).IsNull();
        await Assert.That(logger.Entries.Single()).DoesNotContain(identifier.ToString("D"));
    }

    private sealed class Capture : ILogger<RequestLoggingMiddleware>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
    }

    private sealed class HttpDiagnosticsCapture(string routeTemplate) : ILoggerProvider
    {
        private readonly string _routeTemplate = routeTemplate;
        private readonly ConcurrentQueue<string> _values = new();
        private readonly TaskCompletionSource _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<string> Values => _values.ToArray();
        public Task Observed => _observed.Task;
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }

        private sealed class Logger(HttpDiagnosticsCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                string value = $"{category}|{formatter(state, exception)}";
                owner._values.Enqueue(value);
                if (category == typeof(RequestLoggingMiddleware).FullName
                    && value.Contains(owner._routeTemplate, StringComparison.OrdinalIgnoreCase))
                    owner._observed.TrySetResult();
            }
        }
    }

    private sealed class SensitiveRouteActivityExporter(string? routeTemplate) : BaseExporter<Activity>
    {
        private readonly ConcurrentQueue<string> _values = new();
        private readonly TaskCompletionSource _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<string> Values => _values.ToArray();
        public Task Observed => _observed.Task;

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (Activity activity in batch)
            {
                _values.Enqueue(activity.Source.Name);
                _values.Enqueue(activity.DisplayName);
                foreach (var tag in activity.TagObjects)
                    _values.Enqueue($"{tag.Key}={tag.Value}");
                foreach (var baggage in activity.Baggage)
                    _values.Enqueue($"baggage:{baggage.Key}={baggage.Value}");
                foreach (ActivityEvent activityEvent in activity.Events)
                {
                    _values.Enqueue(activityEvent.Name);
                    foreach (var tag in activityEvent.Tags)
                        _values.Enqueue($"event:{tag.Key}={tag.Value}");
                }
                if (activity.Source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                    && (routeTemplate is null || activity.TagObjects.Any(tag => string.Equals(tag.Value?.ToString(),
                        "/" + routeTemplate, StringComparison.OrdinalIgnoreCase))))
                    _observed.TrySetResult();
            }
            return ExportResult.Success;
        }
    }
}
