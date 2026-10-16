using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Middleware;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.SupportAccess;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace Event.Api.IntegrationTests.Features.Middleware;

public sealed class SupportAccessAuditDiagnosticTests
{
    [Test]
    [Arguments("context_failure")]
    [Arguments("persistence_failure")]
    [Arguments("persisted")]
    public async Task AuditDiagnosticsPreserveResponseWithoutExportingPrivateExceptions(string scenario)
    {
        string canary = Guid.CreateVersion7().ToString("N");
        string privateMessage = $"Private support audit detail {canary}";
        var failure = new InvalidOperationException(privateMessage, new IOException($"Private inner detail {canary}"));
        var supportContext = new SupportAccessContext(
            IsActive: true,
            SessionId: Guid.CreateVersion7(),
            ActorUserId: Guid.CreateVersion7(),
            TargetTenantId: Guid.CreateVersion7(),
            Mode: SupportAccessModeEnum.Write);
        var sessionService = new AuditSessionService(
            supportContext, scenario == "context_failure" ? failure : null);
        var repository = new AuditRepository(scenario == "persistence_failure" ? failure : null);
        var sink = new DiagnosticSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var loggerFactory = LoggerFactory.Create(logging =>
            logging.AddProvider(new IsolatedSerilogLoggerProvider(logger)));
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        IMeterFactory meterFactory = services.GetRequiredService<IMeterFactory>();
        Meter meter = meterFactory.Create(BusinessMetrics.MeterName);
        var measurements = new ConcurrentQueue<AuditMeasurement>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, target) =>
            {
                // Only observe this test's meter, even when other support tests run concurrently.
                if (ReferenceEquals(instrument.Meter, meter)
                    && instrument.Name == "explore.support_access.request_audits")
                    target.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            measurements.Enqueue(new AuditMeasurement(
                value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        listener.Start();
        using var metrics = new BusinessMetrics(meterFactory);
        using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/support-audit-probe";
        context.Response.Body = responseBody;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/support-audit-probe"),
            0,
            EndpointMetadataCollection.Empty,
            "Support audit probe"));
        int nextCalls = 0;
        var middleware = new SupportAccessAuditMiddleware(async httpContext =>
        {
            nextCalls++;
            httpContext.Response.StatusCode = StatusCodes.Status202Accepted;
            httpContext.Response.Headers["X-Probe-Result"] = "accepted";
            await httpContext.Response.WriteAsync("downstream response");
        }, loggerFactory.CreateLogger<SupportAccessAuditMiddleware>());

        await middleware.InvokeAsync(context, sessionService, repository, metrics);

        await Assert.That(nextCalls).IsEqualTo(1);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status202Accepted);
        await Assert.That(context.Response.Headers["X-Probe-Result"].ToString()).IsEqualTo("accepted");
        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("downstream response");

        // Audit construction must reach the repository in the persistence-failure case too.
        if (scenario == "context_failure")
        {
            await Assert.That(repository.AttemptedEvent).IsNull();
        }
        else
        {
            SupportAccessAuditEvent? audit = repository.AttemptedEvent;
            await Assert.That(audit).IsNotNull();
            await Assert.That(audit!.SupportAccessSessionId).IsEqualTo(supportContext.SessionId!.Value);
            await Assert.That(audit.ActorUserId).IsEqualTo(supportContext.ActorUserId);
            await Assert.That(audit.TargetTenantId).IsEqualTo(supportContext.TargetTenantId!.Value);
            await Assert.That(audit.EventTypeId).IsEqualTo((int)SupportAccessAuditEventTypeEnum.CommandCommitted);
            await Assert.That(audit.Outcome).IsEqualTo("success");
            await Assert.That(audit.HttpStatusCode).IsEqualTo((int?)StatusCodes.Status202Accepted);
            await Assert.That(audit.RequestName).IsEqualTo("POST /api/support-audit-probe");
        }

        bool persisted = scenario == "persisted";
        await Assert.That(repository.PersistedEvents.Count).IsEqualTo(persisted ? 1 : 0);
        await Assert.That(measurements.Count).IsEqualTo(1);
        await Assert.That(measurements.TryDequeue(out AuditMeasurement? measurement)).IsTrue();
        await Assert.That(measurement).IsNotNull();
        await Assert.That(measurement!.Count).IsEqualTo(1L);
        await Assert.That(measurement.Tags.Count).IsEqualTo(4);
        await Assert.That(measurement.Tags["event_type"])
            .IsEqualTo(scenario == "context_failure" ? "unknown" : "command_committed");
        await Assert.That(measurement.Tags["outcome"])
            .IsEqualTo(scenario == "context_failure" ? "unknown" : "succeeded");
        await Assert.That(measurement.Tags["persistence_outcome"]).IsEqualTo(persisted ? "persisted" : "failed");
        await Assert.That(measurement.Tags["failure_category"])
            .IsEqualTo(persisted ? "none" : "support_access_audit_persistence_failed");
        await Assert.That(string.Join('|', measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}")))
            .DoesNotContain(canary);

        if (persisted)
        {
            await Assert.That(sink.Events.IsEmpty).IsTrue();
        }
        else
        {
            await Assert.That(sink.Events.Count).IsEqualTo(1);
            await Assert.That(sink.Events.TryDequeue(out LogEvent? exported)).IsTrue();
            await Assert.That(exported).IsNotNull();
            await Assert.That(exported!.Level).IsEqualTo(LogEventLevel.Warning);
            await Assert.That(exported.Exception).IsNull();
            await Assert.That(((ScalarValue)exported.Properties["SourceContext"]).Value)
                .IsEqualTo(typeof(SupportAccessAuditMiddleware).FullName);
            await Assert.That(((ScalarValue)exported.Properties["RequestName"]).Value)
                .IsEqualTo("POST /api/support-audit-probe");
            await Assert.That(((ScalarValue)exported.Properties["StatusCode"]).Value)
                .IsEqualTo(StatusCodes.Status202Accepted);
            using var serialized = new StringWriter(CultureInfo.InvariantCulture);
            new JsonFormatter().Format(exported, serialized);
            string diagnostics = serialized.ToString() + exported.RenderMessage(CultureInfo.InvariantCulture);
            await Assert.That(diagnostics).DoesNotContain(canary);
            await Assert.That(diagnostics).DoesNotContain(privateMessage);
            await Assert.That(diagnostics).DoesNotContain(nameof(InvalidOperationException));
            await Assert.That(diagnostics).DoesNotContain(nameof(IOException));
        }
    }

    private sealed class AuditSessionService(ISupportAccessContext context, Exception? failure)
        : ISupportAccessSessionService
    {
        public Task<ISupportAccessContext> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            failure is null
                ? Task.FromResult(context)
                : Task.FromException<ISupportAccessContext>(failure);

        public Task<ISupportAccessContext> ValidateForwardedSessionAsync(
            Guid sessionId,
            Guid actorUserId,
            Guid? resolvedTenantId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AuditRepository(Exception? failure) : ISupportAccessAuditEventRepository
    {
        public SupportAccessAuditEvent? AttemptedEvent { get; private set; }
        public List<SupportAccessAuditEvent> PersistedEvents { get; } = [];

        public Task<SupportAccessAuditEvent> CreateAsync(
            SupportAccessAuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            AttemptedEvent = auditEvent;
            if (failure is not null)
                return Task.FromException<SupportAccessAuditEvent>(failure);

            PersistedEvents.Add(auditEvent);
            return Task.FromResult(auditEvent);
        }

        public Task<IReadOnlyList<SupportAccessAuditEvent>> ListForSessionAsync(
            Guid sessionId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SupportAccessAuditEvent>>(PersistedEvents
                .Where(audit => audit.SupportAccessSessionId == sessionId).Take(limit).ToArray());

        public Task<IReadOnlyList<SupportAccessAuditEvent>> ListForTargetTenantAsync(
            Guid targetTenantId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SupportAccessAuditEvent>>(PersistedEvents
                .Where(audit => audit.TargetTenantId == targetTenantId).Take(limit).ToArray());
    }

    private sealed class DiagnosticSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private sealed record AuditMeasurement(long Count, IReadOnlyDictionary<string, object?> Tags);
}
