using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Middleware;
using Explore.API.Telemetry;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class JwtDiagnosticConfidentialityTests
{
    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task RegisteredCallbacksExcludePrivateContentAndKeepGenericChallenge(
        bool authenticateFailurePresent, bool endpointAvailable)
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string content = $"{canary}@example.test\r\nforged-event-{canary}";
        string privatePath = $"/api/private/{canary}";
        string privateQuery = $"?provider={canary}";
        var sink = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak);
        using var hosted = factory.WithWebHostBuilder(builder => ConfigureCapture(builder, sink));
        using var client = hosted.CreateClient();
        using var scope = hosted.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = privatePath;
        context.Request.QueryString = new QueryString(privateQuery);
        context.Items["CorrelationId"] = Guid.CreateVersion7().ToString("N");
        if (endpointAvailable)
        {
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/private/{subject}"),
                0,
                new EndpointMetadataCollection(new EndpointNameMetadata("jwt-diagnostic-probe")),
                "JWT diagnostic probe"));
        }

        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var failure = new AuthenticationFailedContext(context, scheme, options)
        {
            Exception = new InvalidOperationException(content, new Exception(content))
        };
        Task<LogEvent> failureSignal = sink.AuthenticationFailed;
        Task<LogEvent> challengeSignal = sink.AuthenticationChallenge;
        await options.Events.AuthenticationFailed(failure);
        LogEvent failureEvent = await failureSignal.WaitAsync(TimeSpan.FromSeconds(10));

        var challenge = new JwtBearerChallengeContext(context, scheme, options, new AuthenticationProperties())
        {
            AuthenticateFailure = authenticateFailurePresent ? failure.Exception : null,
            Error = canary,
            ErrorDescription = content,
            ErrorUri = $"https://private.example.test/{canary}?provider={canary}"
        };
        await options.Events.Challenge(challenge);
        LogEvent challengeEvent = await challengeSignal.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(options.IncludeErrorDetails).IsFalse();
        await Assert.That(challenge.Error).IsEqualTo(authenticateFailurePresent ? "invalid_token" : null);
        await Assert.That(challenge.ErrorDescription).IsNull();
        await Assert.That(challenge.ErrorUri).IsNull();
        string expectedRoute = endpointAvailable ? "jwt-diagnostic-probe" : "route-unresolved";
        await AssertDiagnostic(failureEvent, "authentication_failed", expectedRoute);
        await AssertDiagnostic(challengeEvent, "authentication_challenge", expectedRoute);
        string emitted = sink.Render();
        await Assert.That(emitted).DoesNotContain(canary);
        await Assert.That(emitted).DoesNotContain("private.example.test");
        await Assert.That(emitted).DoesNotContain("forged-event-");
        await Assert.That(emitted).DoesNotContain(privatePath);
        await Assert.That(emitted).DoesNotContain(privateQuery);
    }

    [Test]
    public async Task MalformedBearerHttpRejectionKeeps401WithoutPrivateTokenOrQuery()
    {
        string privateToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string privateQuery = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var sink = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: AuthenticationProviderKind.Keycloak);
        using var hosted = factory.WithWebHostBuilder(builder => ConfigureCapture(builder, sink));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        Task<LogEvent> failureSignal = sink.AuthenticationFailed;
        Task<LogEvent> challengeSignal = sink.AuthenticationChallenge;
        Task requestSignal = sink.RequestCompleted;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/user?token={privateQuery}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", privateToken);
        using var response = await client.SendAsync(request);
        await Task.WhenAll(failureSignal, challengeSignal, requestSignal).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        AuthenticationHeaderValue bearer = response.Headers.WwwAuthenticate.Single(
            header => header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase));
        await Assert.That(bearer.Parameter).IsEqualTo("error=\"invalid_token\"");
        await Assert.That((await failureSignal).Exception).IsNull();
        await Assert.That((await challengeSignal).Exception).IsNull();
        string emitted = string.Join('\n', sink.Render(),
            await response.Content.ReadAsStringAsync(), response.Headers.ToString());
        await Assert.That(emitted).DoesNotContain(privateToken);
        await Assert.That(emitted).DoesNotContain(privateQuery);
        await Assert.That(emitted).DoesNotContain("?token=");
        await Assert.That(emitted).DoesNotContain("error_description");
        await Assert.That(emitted).DoesNotContain("error_uri");
    }

    private static void ConfigureCapture(IWebHostBuilder builder, DiagnosticSink sink)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"Serilog:MinimumLevel:Override:{ApiDiagnosticLogEnricher.JwtCategory}"] = "Debug"
            }));
        builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(sink));
    }

    private static async Task AssertDiagnostic(LogEvent logEvent, string code, string route)
    {
        await Assert.That(Scalar(logEvent, "SourceContext")).IsEqualTo(ApiDiagnosticLogEnricher.JwtCategory);
        await Assert.That(Scalar(logEvent, "Code")).IsEqualTo(code);
        await Assert.That(Scalar(logEvent, "Method")).IsEqualTo("GET");
        await Assert.That(Scalar(logEvent, "Route")).IsEqualTo(route);
        await Assert.That(logEvent.Exception).IsNull();
    }

    private static string? Scalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var property) && property is ScalarValue { Value: string value }
            ? value
            : null;

    private sealed class DiagnosticSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();
        private readonly TaskCompletionSource<LogEvent> _failure =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<LogEvent> _challenge =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _request =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LogEvent> AuthenticationFailed => _failure.Task;
        public Task<LogEvent> AuthenticationChallenge => _challenge.Task;
        public Task RequestCompleted => _request.Task;

        public void Emit(LogEvent logEvent)
        {
            _events.Enqueue(logEvent);
            string? category = Scalar(logEvent, "SourceContext");
            if (category == ApiDiagnosticLogEnricher.JwtCategory)
            {
                switch (Scalar(logEvent, "Code"))
                {
                    case "authentication_failed":
                        _failure.TrySetResult(logEvent);
                        break;
                    case "authentication_challenge":
                        _challenge.TrySetResult(logEvent);
                        break;
                }
            }
            if (category == typeof(RequestLoggingMiddleware).FullName)
                _request.TrySetResult();
        }

        public string Render() => string.Join('\n', _events.ToArray().Select(logEvent =>
            string.Join('\n', logEvent.RenderMessage(), logEvent.Exception?.ToString() ?? string.Empty,
                string.Join('\n', logEvent.Properties.Select(property => $"{property.Key}={property.Value}")))));
    }
}
