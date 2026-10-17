using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.DTOs.EventReporting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class ReportDiagnosticConfidentialityTests
{
    [Test]
    public async Task NativeReportSubmissionRefusalDoesNotExportReportOrEventIdentifiers()
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var canary = $"{Guid.NewGuid():N}\r\nprivate-report";
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var logger = new Collector<EventReportsController>();
        var controller = ActivatorUtilities.CreateInstance<EventReportsController>(scope.ServiceProvider, logger);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        var result = await controller.Submit(new SubmitEventReportDto
        {
            EventId = eventId,
            ReasonCode = "spam",
            ReporterText = canary,
            ReporterLocale = "en",
            ReportCaseUpdatesConsent = true,
            ReportFollowUpContactConsent = false
        });

        await Assert.That(result.Result is ObjectResult { StatusCode: >= 400 }).IsTrue();
        await AssertCollector(logger.Exports, eventId, reportId, canary);
    }

    [Test]
    public async Task NativeWorkflowRefusalRetainsActionWithoutDiagnosticIdentifiers()
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var canary = $"{Guid.NewGuid():N}\r\nprivate-workflow";
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var logger = new Collector<ModerationReportController>();
        var controller = ActivatorUtilities.CreateInstance<ModerationReportController>(scope.ServiceProvider, logger);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        var result = await controller.Triage(eventId, reportId, new TriageModerationReportRequestDto
        {
            CaseId = Guid.NewGuid(),
            ExpectedCaseConcurrencyStamp = Guid.NewGuid(),
            QueueCode = canary
        });

        await Assert.That(result.Result is ObjectResult { StatusCode: >= 400 }).IsTrue();
        await AssertCollector(logger.Exports, eventId, reportId, canary);
        await Assert.That(logger.Exports.Single().Contains("triage", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task NativeProviderCallbackRefusalDoesNotExportPrivateIdentifiers()
    {
        var eventId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var canary = $"{Guid.NewGuid():N}\r\nprivate-provider";
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var logger = new Collector<ModerationIntegrationController>();
        var controller = ActivatorUtilities.CreateInstance<ModerationIntegrationController>(scope.ServiceProvider, logger);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        var result = await controller.RecordOspreyCallback(new OspreySignalCallbackRequestDto
        {
            TenantId = Guid.NewGuid(),
            EventId = eventId,
            ReportId = reportId,
            ProviderSignalId = canary,
            CorrelationId = canary
        });

        await Assert.That(result.Result is ObjectResult { StatusCode: >= 400 }).IsTrue();
        await AssertCollector(logger.Exports, eventId, reportId, canary);
        await Assert.That(logger.Exports.Single().Contains("osprey", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertCollector(List<string> exports, Guid eventId, Guid reportId, string canary)
    {
        await Assert.That(exports.Count).IsEqualTo(1);
        var output = exports.Single();
        foreach (var value in new[] { eventId.ToString(), reportId.ToString(), canary[..32] })
            await Assert.That(output.Contains(value, StringComparison.Ordinal)).IsFalse();
        await Assert.That(output.Contains("failed", StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains("\"Exception\":null", StringComparison.Ordinal)).IsTrue();
    }

    private sealed class Collector<T> : ILogger<T>
    {
        public List<string> Exports { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Exports.Add(JsonSerializer.Serialize(new
            {
                Message = formatter(state, exception),
                State = state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToArray() : [],
                Exception = exception?.ToString()
            }));
    }

}
