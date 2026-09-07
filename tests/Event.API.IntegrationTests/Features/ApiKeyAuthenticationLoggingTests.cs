using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using Explore.API.Authentication;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ApiKeyHashing = Explore.Application.Services.ApiKeyHashing;

namespace Event.Api.IntegrationTests.Features;

public sealed class ApiKeyAuthenticationLoggingTests
{
    private const string SensitiveProbeUrl = "/api/private-route-tenant-id/api-keys/private-route-secret-segment/auth-probe";
    private const string ProbeRouteName = "ApiKeyAuthenticationSensitiveProbe";
    private const string UnresolvedRouteClassification = "route-unresolved";

    [Test]
    [Arguments("configured_success", HttpStatusCode.OK, "success", "accepted", "configured", true)]
    [Arguments("configured_success", HttpStatusCode.OK, "success", "accepted", "configured", false)]
    [Arguments("configured_expired", HttpStatusCode.Unauthorized, "failed", "expired", "configured", true)]
    [Arguments("configured_expired", HttpStatusCode.Unauthorized, "failed", "expired", "configured", false)]
    [Arguments("persisted_success", HttpStatusCode.OK, "success", "accepted", "persisted", true)]
    [Arguments("persisted_success", HttpStatusCode.OK, "success", "accepted", "persisted", false)]
    [Arguments("persisted_inactive", HttpStatusCode.Unauthorized, "failed", "inactive", "persisted", true)]
    [Arguments("persisted_inactive", HttpStatusCode.Unauthorized, "failed", "inactive", "persisted", false)]
    [Arguments("persisted_hash_mismatch", HttpStatusCode.Unauthorized, "failed", "invalid_secret", "persisted", true)]
    [Arguments("persisted_hash_mismatch", HttpStatusCode.Unauthorized, "failed", "invalid_secret", "persisted", false)]
    [Arguments("persisted_expired", HttpStatusCode.Unauthorized, "failed", "expired", "persisted", true)]
    [Arguments("persisted_expired", HttpStatusCode.Unauthorized, "failed", "expired", "persisted", false)]
    [Arguments("no_match", HttpStatusCode.Unauthorized, "failed", "no_match", "unknown", true)]
    [Arguments("no_match", HttpStatusCode.Unauthorized, "failed", "no_match", "unknown", false)]
    public async Task AuthenticationOutcomeLogsContainOnlyBoundedMetadata(
        string scenario,
        HttpStatusCode expectedStatus,
        string expectedOutcome,
        string expectedReason,
        string expectedSource,
        bool endpointMetadataAvailable)
    {
        var microsoft = new CapturingLoggerProvider(typeof(ApiKeyAuthenticationHandler).FullName!);
        var serilog = new CapturingSerilogSink(typeof(ApiKeyAuthenticationHandler).FullName!);
        Serilog.ILogger isolatedLogger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(serilog)
            .CreateLogger();
        using TestScenario test = await CreateScenarioAsync(scenario, endpointMetadataAvailable, microsoft, isolatedLogger);
        Task<CapturedLog> microsoftSignal = microsoft.CaptureNextEntryAsync();
        Task<CapturedLog> serilogSignal = serilog.CaptureNextEntryAsync();

        AuthenticateResult result = await test.Handler.AuthenticateAsync();
        CapturedLog microsoftLog = await microsoftSignal.WaitAsync(TimeSpan.FromSeconds(5));
        CapturedLog serilogLog = await serilogSignal.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result.Succeeded).IsEqualTo(expectedStatus == HttpStatusCode.OK);
        foreach (CapturedLog log in new[] { microsoftLog, serilogLog })
        {
            await Assert.That(log.Properties["Outcome"]).IsEqualTo(expectedOutcome);
            await Assert.That(log.Properties["Reason"]).IsEqualTo(expectedReason);
            await Assert.That(log.Properties["CredentialSource"]).IsEqualTo(expectedSource);
            await Assert.That(log.Properties).ContainsKey("TenantPresent");
            await Assert.That(log.Flattened.Length).IsLessThanOrEqualTo(2048);
            await Assert.That(log.Properties["Route"]).IsEqualTo(
                endpointMetadataAvailable ? ProbeRouteName : UnresolvedRouteClassification);
            await Assert.That(log.Properties).DoesNotContainKey("Path");

            foreach (string sensitiveValue in test.SensitiveValues)
            {
                await Assert.That(log.Flattened).DoesNotContain(sensitiveValue);
            }
        }
    }

    private static async Task<TestScenario> CreateScenarioAsync(
        string scenario,
        bool endpointMetadataAvailable,
        CapturingLoggerProvider microsoft,
        Serilog.ILogger isolatedLogger)
    {
        Guid tenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000731");
        Guid ownerId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000732");
        string keyId = $"private-{scenario}-key-id";
        string secret = $"private-{scenario}-secret";
        string persistedCredential = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);
        var optionsValue = new ApiKeyAuthenticationOptions();
        var repository = Substitute.For<IExternalApiKeyRepository>();
        string presentedCredential;

        if (scenario.StartsWith("configured_", StringComparison.Ordinal))
        {
            presentedCredential = secret;
            optionsValue.Clients =
                [
                    new ApiKeyClientDescriptor
                    {
                        KeyId = keyId,
                        TenantId = tenantId,
                        OwnerType = "User",
                        OwnerId = ownerId.ToString("D"),
                        Scopes = ["events:read"],
                        SecretHash = ApiKeyHashing.ComputeHash(secret),
                        ExpiresAtUtc = scenario == "configured_expired"
                            ? DateTimeOffset.UtcNow.AddMinutes(-5)
                            : null
                    }
                ];
        }
        else if (scenario.StartsWith("persisted_", StringComparison.Ordinal))
        {
            presentedCredential = scenario == "persisted_hash_mismatch"
                ? ApiKeyHashing.FormatPersistedApiKey(keyId, "private-wrong-secret")
                : persistedCredential;
            var persisted = new ExternalApiKey
            {
                Id = Guid.CreateVersion7(),
                Name = "Private test key",
                KeyId = keyId,
                SecretHash = ApiKeyHashing.ComputeHash(secret),
                TenantId = tenantId,
                OwnerId = ownerId,
                OwnerType = ExternalApiKeyOwnerType.User,
                ExternalApiKeyStatusId = scenario == "persisted_inactive"
                    ? (int)ExternalApiKeyStatusEnum.Revoked
                    : (int)ExternalApiKeyStatusEnum.Active,
                ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                ExternalApiKeyCreditPeriod = null!,
                ExpiresAt = scenario == "persisted_expired"
                    ? DateTime.UtcNow.AddMinutes(-5)
                    : null,
                Scopes = "events:read"
            };
            repository.GetByKeyIdForAuthentication(keyId, Arg.Any<CancellationToken>())
                .Returns(persisted);
        }
        else
        {
            presentedCredential = "private-unmatched-api-key";
        }

        var options = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        options.Get(ApiAuthenticationSchemeNames.ApiKey).Returns(optionsValue);
        var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(microsoft);
            logging.AddProvider(new IsolatedSerilogLoggerProvider(isolatedLogger));
        });
        var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new BusinessMetrics(services.GetRequiredService<IMeterFactory>());
        var handler = new ApiKeyAuthenticationHandler(
            options,
            loggerFactory,
            UrlEncoder.Default,
            repository,
            metrics);
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = SensitiveProbeUrl;
        if (endpointMetadataAvailable)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new TestEndpointNameMetadata(ProbeRouteName)),
                ProbeRouteName));
        }
        context.Request.Headers[optionsValue.HeaderName] = presentedCredential;
        await handler.InitializeAsync(
            new AuthenticationScheme(
                ApiAuthenticationSchemeNames.ApiKey,
                ApiAuthenticationSchemeNames.ApiKey,
                typeof(ApiKeyAuthenticationHandler)),
            context);

        return new TestScenario(handler, services, metrics, loggerFactory,
            [
                keyId,
                secret,
                presentedCredential,
                tenantId.ToString("D"),
                ownerId.ToString("D"),
                ApiKeyHashing.ComputeHash(secret),
                SensitiveProbeUrl,
                "private-route-tenant-id",
                "private-route-secret-segment"
            ]);
    }

    private sealed class CapturingLoggerProvider(string targetCategory) : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

        private readonly ConcurrentQueue<TaskCompletionSource<CapturedLog>> _waiters = new();

        internal Task<CapturedLog> CaptureNextEntryAsync()
        {
            var waiter = new TaskCompletionSource<CapturedLog>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            return waiter.Task;
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName == targetCategory, _waiters, () => _scopeProvider);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
            _scopeProvider = scopeProvider;


        public void Dispose() { }

        private sealed class CapturingLogger(
            bool enabled,
            ConcurrentQueue<TaskCompletionSource<CapturedLog>> waiters,
            Func<IExternalScopeProvider> getScopeProvider)
            : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
                getScopeProvider().Push(state);
            public bool IsEnabled(LogLevel logLevel) => enabled;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!enabled || !waiters.TryDequeue(out TaskCompletionSource<CapturedLog>? waiter))
                    return;

                var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.ToDictionary(
                        pair => pair.Key,
                        pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                        StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                var scopes = new List<string>();
                getScopeProvider().ForEachScope(
                    static (scope, captured) => captured.Add(
                        Convert.ToString(scope, CultureInfo.InvariantCulture) ?? string.Empty),
                    scopes);
                waiter.TrySetResult(new CapturedLog(
                    formatter(state, exception), properties, exception?.ToString(), scopes));
            }
        }
    }

    private sealed class CapturingSerilogSink(string targetCategory) : ILogEventSink
    {
        private readonly ConcurrentQueue<TaskCompletionSource<CapturedLog>> _waiters = new();

        internal Task<CapturedLog> CaptureNextEntryAsync()
        {
            var waiter = new TaskCompletionSource<CapturedLog>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            return waiter.Task;
        }

        public void Emit(LogEvent logEvent)
        {
            if (!logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? source)
                || Format(source) != targetCategory
                || !_waiters.TryDequeue(out TaskCompletionSource<CapturedLog>? waiter))
                return;

            var properties = logEvent.Properties.ToDictionary(
                pair => pair.Key,
                pair => Format(pair.Value) ?? string.Empty,
                StringComparer.Ordinal);
            waiter.TrySetResult(new CapturedLog(
                logEvent.RenderMessage(CultureInfo.InvariantCulture), properties, logEvent.Exception?.ToString(), []));
        }

        private static string? Format(LogEventPropertyValue value) => value switch
        {
            ScalarValue scalar => Convert.ToString(scalar.Value, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private sealed record TestScenario(
        ApiKeyAuthenticationHandler Handler,
        ServiceProvider Services,
        BusinessMetrics Metrics,
        ILoggerFactory LoggerFactory,
        IReadOnlyList<string> SensitiveValues) : IDisposable
    {
        public void Dispose()
        {
            Metrics.Dispose();
            LoggerFactory.Dispose();
            Services.Dispose();
        }
    }

    private sealed record TestEndpointNameMetadata(string EndpointName) : Microsoft.AspNetCore.Routing.IEndpointNameMetadata;

    private sealed record CapturedLog(
        string Message,
        IReadOnlyDictionary<string, string> Properties,
        string? Exception,
        IReadOnlyList<string> Scopes)
    {
        internal string Flattened => string.Join('|',
            [Message, .. Properties.Select(pair => $"{pair.Key}={pair.Value}"), Exception ?? string.Empty, .. Scopes]);
    }
}
