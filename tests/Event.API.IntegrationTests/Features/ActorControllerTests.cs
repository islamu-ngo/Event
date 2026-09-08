using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Middleware;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Serilog;
using NSubstitute;
using Serilog.Core;
using Serilog.Events;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class ActorControllerTests
{
    private readonly ApiTestFixture _fixture;
    private const string BaseUrl = "/api/actor";

    public ActorControllerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region GET Endpoints

    [Test]
    public async Task GetAll_ShouldReturnOk_WithPaginatedResult()
    {
        // Act
        var response = await _fixture.Client.GetAsync(BaseUrl);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("items");
    }

    [Test]
    [Arguments(1, 10)]
    [Arguments(1, 20)]
    [Arguments(2, 5)]
    public async Task GetAll_WithPaginationParams_ShouldReturnPaginatedResult(int pageNumber, int pageSize)
    {
        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}?pageNumber={pageNumber}&pageSize={pageSize}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetById_WithRandomId_ShouldReturnNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/{id}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetById_WithInvalidGuidFormat_ShouldReturnNotFound()
    {
        // Act - ASP.NET Core route constraints reject non-GUID strings with 404 (no route match)
        var response = await _fixture.Client.GetAsync($"{BaseUrl}/not-a-guid");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST Endpoints

    [Test]
    public async Task GenericCreateRoute_ShouldRemainAbsent()
    {
        var response = await _fixture.Client.PostAsJsonAsync(BaseUrl, new { displayName = "Test Actor" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task Suspend_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PostAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/moderation/suspend",
            new { reasonCode = "policy-violation" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Reinstate_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PostAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/moderation/reinstate",
            new { reasonCode = "appeal-approved" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AtprotoIdentitySuspend_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PostAsJsonAsync(
            $"{BaseUrl}/atproto-identities/{Guid.NewGuid()}/moderation/suspend",
            new { reasonCode = "policy-violation" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AtprotoIdentityReinstate_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PostAsJsonAsync(
            $"{BaseUrl}/atproto-identities/{Guid.NewGuid()}/moderation/reinstate",
            new { reasonCode = "appeal-approved" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region PATCH Endpoints

    [Test]
    public async Task GenericPatchRoute_ShouldRemainAbsent()
    {
        var id = Guid.NewGuid();
        var response = await _fixture.Client.PatchAsJsonAsync(
            $"{BaseUrl}/{id}",
            new { profile = new { displayName = "Updated Actor" } });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task UpdatePut_WhenUsingOldRoute_ShouldReturnMethodNotAllowed()
    {
        var id = Guid.NewGuid();
        var response = await _fixture.Client.PutAsJsonAsync($"{BaseUrl}/{id}", new { });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    #endregion

    #region DELETE Endpoints

    [Test]
    public async Task GenericDeleteRoute_ShouldRemainAbsent()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var response = await _fixture.Client.DeleteAsync($"{BaseUrl}/{id}");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    #endregion
}

public sealed class ActorDidIngressHttpRedTests
{
    [Test]
    public async Task GetByDid_MalformedAndOversizedScalarInputs_ReturnBoundedProblemBeforeDispatch()
    {
        var dispatched = new ConcurrentQueue<GetActorByDidRequest>();
        var mediator = Substitute.For<IMediator>();
        var serilogLogs = new CapturingSerilogSink();
        // Keep both captures test-owned so parallel hosts cannot replace their logging pipeline.
        Serilog.ILogger isolatedSerilogLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(serilogLogs)
            .CreateLogger();
        using var logs = new CapturingRequestLoggerProvider(isolatedSerilogLogger);
        mediator.Send(Arg.Any<GetActorByDidRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatched.Enqueue(call.Arg<GetActorByDidRequest>()!);
                return (ActorDto)null!;
            });

        using ILoggerFactory captureLoggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var requestLogger = captureLoggerFactory.CreateLogger<RequestLoggingMiddleware>();
        var tenantContext = Substitute.For<Explore.Application.Contracts.Services.ITenantContextAccessor>();
        var requestLoggingMiddleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, requestLogger);

        await using WebApplicationFactory<Program> factory = new AuthenticatedWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMediator>();
                    services.AddSingleton(mediator);
                });
            });
        using HttpClient client = factory.CreateClient();

        string erasedTombstone = $"did:deleted:{Guid.CreateVersion7():N}";
        string[] prohibitedInputs =
        [
            "not-a-did",
            "did:plc:" + new string('a', 2041),
            erasedTombstone
        ];
        var violations = new List<string>();

        foreach (string input in prohibitedInputs)
        {
            int dispatchBaseline = dispatched.Count;
            var logContext = new DefaultHttpContext();
            Task<CapturedRequestLog> logSignal = logs.CaptureNextEntryAsync();
            Task<CapturedSerilogEvent> serilogSignal = serilogLogs.CaptureNextEntryAsync();
            logContext.Request.Method = HttpMethods.Get;
            logContext.Request.Path = $"/api/actor/by-did/{input}";
            logContext.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new TestEndpointNameMetadata(RouteNames.GetActorByDid)),
                RouteNames.GetActorByDid));
            await requestLoggingMiddleware.InvokeAsync(logContext, tenantContext);
            using HttpResponseMessage response = await client.GetAsync(
                $"/api/actor/by-did/{Uri.EscapeDataString(input)}");
            string body = await response.Content.ReadAsStringAsync();
            CapturedRequestLog[] requestLogs =
                [await logSignal.WaitAsync(TimeSpan.FromSeconds(5))];
            CapturedSerilogEvent serilogEvent =
                await serilogSignal.WaitAsync(TimeSpan.FromSeconds(5));

            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                violations.Add("invalid DID was not rejected with HTTP 400");
            }
            if (response.Content.Headers.ContentType?.MediaType != "application/problem+json")
            {
                violations.Add($"invalid DID did not return RFC 7807 ({response.Content.Headers.ContentType})");
            }
            if (body.Length > 2048)
            {
                violations.Add("invalid DID response was not bounded");
            }
            if (body.Contains(input, StringComparison.Ordinal)
                || body.Contains("did:", StringComparison.OrdinalIgnoreCase)
                || body.Contains("input length", StringComparison.OrdinalIgnoreCase)
                || body.Contains("exist", StringComparison.OrdinalIgnoreCase)
                || body.Contains("provider", StringComparison.OrdinalIgnoreCase)
                || (input.Length > 2048 && body.Contains(input.Length.ToString(), StringComparison.Ordinal)))
            {
                violations.Add("invalid DID response disclosed route input, input length, existence, or provider detail");
            }
            if (dispatched.Count != dispatchBaseline)
            {
                violations.Add("invalid DID crossed the MediatR boundary");
            }
            if (requestLogs.Length == 0
                || !requestLogs.Any(entry =>
                    entry.Fields.Any(field => field.Key == "Route"
                        && field.Value == RouteNames.GetActorByDid)
                    && entry.Fields.Any(field => field.Key == "RequestPath"
                        && field.Value == RouteNames.GetActorByDid)))
            {
                violations.Add("invalid DID request log omitted its stable route identity");
            }
            if (requestLogs.Any(entry => entry.Flattened.Length > 4096)
                || serilogEvent.Flattened.Length > 4096)
            {
                violations.Add("invalid DID request log was not bounded");
            }
            if (requestLogs.Any(entry => entry.Flattened.Contains(input, StringComparison.Ordinal)
                    || entry.Flattened.Contains("did:", StringComparison.OrdinalIgnoreCase))
                || serilogEvent.Flattened.Contains(input, StringComparison.Ordinal)
                || serilogEvent.Flattened.Contains("did:", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add("invalid DID was disclosed by a request log record, scope, or exception");
            }
            if (!serilogEvent.Properties.TryGetValue("Route", out string? serilogRoute)
                || serilogRoute != RouteNames.GetActorByDid
                || !serilogEvent.Properties.TryGetValue("RequestPath", out string? serilogRequestPath)
                || serilogRequestPath != RouteNames.GetActorByDid)
            {
                violations.Add("Serilog event omitted stable route properties");
            }

            using JsonDocument problem = JsonDocument.Parse(body);
            JsonElement root = problem.RootElement;
            if (root.GetProperty("type").GetString() != "https://tools.ietf.org/html/rfc9110#section-15.5.1"
                || root.GetProperty("title").GetString() != "Invalid route parameter"
                || root.GetProperty("code").GetString() != "validation_failed")
            {
                violations.Add("invalid DID response did not retain the generic stable error contract");
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("Malformed or oversized scalar DID route values must fail safely before application or repository work.");
    }

    [Test]
    public async Task GetByDid_ValidMixedCaseScalar_PreservesExactDispatchAndExistingResponse()
    {
        const string validDid = "did:future:CaseSensitive_Value";
        var dispatched = new ConcurrentQueue<GetActorByDidRequest>();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetActorByDidRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                dispatched.Enqueue(call.Arg<GetActorByDidRequest>()!);
                return (ActorDto)null!;
            });

        await using WebApplicationFactory<Program> factory = new AuthenticatedWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMediator>();
                    services.AddSingleton(mediator);
                });
            });
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/actor/by-did/{Uri.EscapeDataString(validDid)}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(dispatched).HasSingleItem();
        await Assert.That(dispatched.Single().Did).IsEqualTo(validDid);
    }

    private sealed class CapturingRequestLoggerProvider(Serilog.ILogger? isolatedSerilogLogger = null) : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentQueue<TaskCompletionSource<CapturedRequestLog>> _waiters = new();
        private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

        internal Task<CapturedRequestLog> CaptureNextEntryAsync()
        {
            var waiter = new TaskCompletionSource<CapturedRequestLog>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            return waiter.Task;
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CapturingLogger(
            categoryName == typeof(RequestLoggingMiddleware).FullName,
            _waiters,
            () => _scopeProvider,
            isolatedSerilogLogger);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

        public void Dispose() { }

        private sealed class CapturingLogger(
            bool isTargetCategory,
            ConcurrentQueue<TaskCompletionSource<CapturedRequestLog>> waiters,
            Func<IExternalScopeProvider> getScopeProvider,
            Serilog.ILogger? isolatedSerilogLogger) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                getScopeProvider().Push(state);

            public bool IsEnabled(LogLevel logLevel) => isTargetCategory;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!isTargetCategory)
                    return;

                var fields = state is IEnumerable<KeyValuePair<string, object?>> structuredState
                    ? structuredState.Select(pair => new KeyValuePair<string, string?>(
                        pair.Key,
                        Convert.ToString(pair.Value, CultureInfo.InvariantCulture))).ToArray()
                    : [];
                var scopes = new List<string>();
                getScopeProvider().ForEachScope(
                    static (scope, captured) => captured.Add(FormatValue(scope)),
                    scopes);
                var entry = new CapturedRequestLog(
                    formatter(state, exception),
                    fields,
                    exception?.ToString(),
                    scopes);
                if (isolatedSerilogLogger is not null)
                {
                    Serilog.ILogger contextual = isolatedSerilogLogger
                        .ForContext(Constants.SourceContextPropertyName, typeof(RequestLoggingMiddleware).FullName);
                    foreach (KeyValuePair<string, string?> field in fields)
                    {
                        contextual = contextual.ForContext(field.Key, field.Value);
                    }
                    contextual.Write(LogEventLevel.Information, "{RenderedMessage}", entry.Message);
                }

                if (waiters.TryDequeue(out TaskCompletionSource<CapturedRequestLog>? waiter))
                    waiter.TrySetResult(entry);
            }

            private static string FormatValue(object? value) =>
                value is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(";", pairs.Select(pair =>
                        $"{pair.Key}={Convert.ToString(pair.Value, CultureInfo.InvariantCulture)}"))
                    : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private sealed record TestEndpointNameMetadata(string EndpointName) : Microsoft.AspNetCore.Routing.IEndpointNameMetadata;

    private sealed class CapturingSerilogSink : ILogEventSink
    {
        private readonly ConcurrentQueue<TaskCompletionSource<CapturedSerilogEvent>> _waiters = new();

        internal Task<CapturedSerilogEvent> CaptureNextEntryAsync()
        {
            var waiter = new TaskCompletionSource<CapturedSerilogEvent>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            return waiter.Task;
        }

        public void Emit(LogEvent logEvent)
        {
            if (!logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? source)
                || FormatValue(source) != typeof(RequestLoggingMiddleware).FullName)
                return;

            var properties = logEvent.Properties.ToDictionary(
                pair => pair.Key,
                pair => FormatValue(pair.Value),
                StringComparer.Ordinal);
            var entry = new CapturedSerilogEvent(
                logEvent.RenderMessage(CultureInfo.InvariantCulture),
                logEvent.Exception?.ToString(),
                properties);
            if (_waiters.TryDequeue(out TaskCompletionSource<CapturedSerilogEvent>? waiter))
                waiter.TrySetResult(entry);
        }

        private static string? FormatValue(LogEventPropertyValue value) => value switch
        {
            ScalarValue scalar => Convert.ToString(scalar.Value, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private sealed record CapturedRequestLog(
        string Message,
        IReadOnlyList<KeyValuePair<string, string?>> Fields,
        string? Exception,
        IReadOnlyList<string> Scopes)
    {
        internal string Flattened => string.Join(
            "|",
            [Message, .. Fields.Select(pair => $"{pair.Key}={pair.Value}"), Exception ?? string.Empty, .. Scopes]);
    }

    private sealed record CapturedSerilogEvent(
        string Message,
        string? Exception,
        IReadOnlyDictionary<string, string?> Properties)
    {
        internal string Flattened => string.Join(
            "|",
            [Message, Exception ?? string.Empty, .. Properties.Select(pair => $"{pair.Key}={pair.Value}")]);
    }
}
