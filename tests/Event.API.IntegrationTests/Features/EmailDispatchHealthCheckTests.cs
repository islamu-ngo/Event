// ABOUTME: Unit-style tests for the API EmailDispatchHealthCheck.
// ABOUTME: Verifies Basic Dispatch Mode health reports enabled and intentionally disabled states safely.

using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Configuration;
using Explore.API.HealthChecks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Infrastructure;
using Explore.ServiceDefaults.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[Category(TestCategories.Email)]
public sealed class EmailDispatchHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsyncWhenDispatchEnabledReturnsHealthyWithSafeData()
    {
        var settings = new EmailDispatchProcessorSettings
        {
            Enabled = true,
            Mode = EmailDispatchProcessorMode.Quartz,
            PollingIntervalSeconds = 7,
            BatchSize = 12,
            MaxAttemptCount = 4,
            ProcessingLeaseTimeoutSeconds = 30,
            HealthDueDispatchWarningThreshold = 10,
            HealthStaleProcessingWarningThreshold = 2,
            HealthDeadLetterWarningThreshold = 2,
            HealthOldestPendingWarningSeconds = 3600,
            HealthTenantBacklogWarningThreshold = 5,
            HealthTenantSampleLimit = 3,
            ConsumerId = "test-consumer"
        };
        var schedulerOptions = new QuartzSchedulerSettings
        {
            Enabled = true,
            StatusEndpointEnabled = false
        };
        var setup = CreateHealthCheck(
            settings,
            schedulerOptions,
            dueDispatchCount: 1,
            retryScheduledCount: 1,
            oldestDueCreatedAt: DateTime.UtcNow.AddMinutes(-5),
            dueDispatchByTenant: new Dictionary<Guid, int> { [Guid.CreateVersion7()] = 1 });
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("Quartz");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("mode").And.Value.IsEqualTo(nameof(EmailDispatchProcessorMode.Quartz));
        await Assert.That(result.Data).ContainsKey("pollingIntervalSeconds").And.Value.IsEqualTo(7);
        await Assert.That(result.Data).ContainsKey("batchSize").And.Value.IsEqualTo(12);
        await Assert.That(result.Data).ContainsKey("maxAttemptCount").And.Value.IsEqualTo(4);
        await Assert.That(result.Data).ContainsKey("processingLeaseTimeoutSeconds").And.Value.IsEqualTo(30);
        await Assert.That(result.Data).ContainsKey("dueDispatchWarningThreshold").And.Value.IsEqualTo(10);
        await Assert.That(result.Data).ContainsKey("staleProcessingWarningThreshold").And.Value.IsEqualTo(2);
        await Assert.That(result.Data).ContainsKey("unknownWarningThreshold").And.Value.IsEqualTo(1);
        await Assert.That(result.Data).ContainsKey("deadLetterWarningThreshold").And.Value.IsEqualTo(2);
        await Assert.That(result.Data).ContainsKey("dueDispatchCount").And.Value.IsEqualTo(1);
        await Assert.That(result.Data).ContainsKey("retryScheduledCount").And.Value.IsEqualTo(1);
        await Assert.That(result.Data).ContainsKey("staleProcessingCount").And.Value.IsEqualTo(0);
        await Assert.That(result.Data).ContainsKey("deadLetteredCount").And.Value.IsEqualTo(0);
        await Assert.That(result.Data).ContainsKey("unknownCount").And.Value.IsEqualTo(0);
        await Assert.That(result.Data).ContainsKey("parkedCount").And.Value.IsEqualTo(0);
        await Assert.That(result.Data).ContainsKey("optionalReminderDeferralActive").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("processingStartedBefore").And.Value.IsTypeOf<DateTime>();
        await Assert.That(result.Data).ContainsKey("oldestActivePendingAgeSeconds");
        await Assert.That(result.Data).ContainsKey("tenantBacklogSample");
        await Assert.That(result.Data).ContainsKey("consumerId").And.Value.IsEqualTo("test-consumer");
        await Assert.That(result.Data).ContainsKey("schedulerEnabled").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("schedulerStatusEndpointEnabled").And.Value.IsEqualTo(false);
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("body", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("recipient", StringComparison.OrdinalIgnoreCase));
        await Assert.That(result.Data.Keys).DoesNotContain(key => key.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task CheckHealthAsyncWhenDispatchDisabledReturnsHealthy()
    {
        var settings = new EmailDispatchProcessorSettings
        {
            Enabled = false,
            ConsumerId = "disabled-consumer"
        };
        var setup = CreateHealthCheck(settings);
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("intentionally disabled");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(false);
        await Assert.That(result.Data).ContainsKey("consumerId").And.Value.IsEqualTo("disabled-consumer");
        await setup.Repository.DidNotReceiveWithAnyArgs().CountDueDispatchAsync(default, default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountRetryScheduledAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountStaleProcessingAsync(default, default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountDeadLetteredAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountUnknownAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountParkedAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().IsOptionalReminderDeferralActiveAsync(default);
    }

    [Test]
    public async Task CheckHealthAsyncWhenSchedulerModeDisabledReturnsHealthy()
    {
        var settings = new EmailDispatchProcessorSettings
        {
            Enabled = true,
            Mode = EmailDispatchProcessorMode.Disabled,
            ConsumerId = "disabled-mode"
        };
        var schedulerOptions = new QuartzSchedulerSettings
        {
            Enabled = true
        };
        var setup = CreateHealthCheck(settings, schedulerOptions);
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("Disabled");
        await Assert.That(result.Data).ContainsKey("enabled").And.Value.IsEqualTo(true);
        await Assert.That(result.Data).ContainsKey("mode").And.Value.IsEqualTo(nameof(EmailDispatchProcessorMode.Disabled));
        await setup.Repository.DidNotReceiveWithAnyArgs().CountDueDispatchAsync(default, default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountRetryScheduledAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountStaleProcessingAsync(default, default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountDeadLetteredAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountUnknownAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountParkedAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().IsOptionalReminderDeferralActiveAsync(default);
    }

    [Test]
    public async Task CheckHealthAsyncWhenQuartzModeHasDisabledSchedulerReturnsDegraded()
    {
        var settings = new EmailDispatchProcessorSettings
        {
            Enabled = true,
            Mode = EmailDispatchProcessorMode.Quartz,
            ConsumerId = "quartz-disabled"
        };
        var schedulerOptions = new QuartzSchedulerSettings
        {
            Enabled = false
        };
        var setup = CreateHealthCheck(settings, schedulerOptions);
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("Quartz");
        await Assert.That(result.Data).ContainsKey("mode").And.Value.IsEqualTo(nameof(EmailDispatchProcessorMode.Quartz));
        await Assert.That(result.Data).ContainsKey("schedulerEnabled").And.Value.IsEqualTo(false);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountDueDispatchAsync(default, default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountRetryScheduledAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountStaleProcessingAsync(default, default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountDeadLetteredAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountUnknownAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().CountParkedAsync(default);
        await setup.Repository.DidNotReceiveWithAnyArgs().IsOptionalReminderDeferralActiveAsync(default);
    }

    [Test]
    public async Task CheckHealthAsyncWhenHostedServiceModeReturnsHealthyWithoutScheduler()
    {
        var settings = new EmailDispatchProcessorSettings
        {
            Enabled = true,
            Mode = EmailDispatchProcessorMode.HostedService,
            ConsumerId = "hosted-service"
        };
        var schedulerOptions = new QuartzSchedulerSettings
        {
            Enabled = false
        };
        var setup = CreateHealthCheck(settings, schedulerOptions);
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).Contains("hosted service");
        await Assert.That(result.Data).ContainsKey("mode").And.Value.IsEqualTo(nameof(EmailDispatchProcessorMode.HostedService));
        await Assert.That(result.Data).ContainsKey("schedulerEnabled").And.Value.IsEqualTo(false);
    }

    [Test]
    public async Task CheckHealthAsyncWhenStaleProcessingAtThresholdReturnsDegraded()
    {
        var setup = CreateHealthCheck(
            new EmailDispatchProcessorSettings
            {
                Enabled = true,
                Mode = EmailDispatchProcessorMode.Quartz,
                HealthStaleProcessingWarningThreshold = 1,
                ConsumerId = "stale-processing"
            },
            new QuartzSchedulerSettings { Enabled = true },
            staleProcessingCount: 1);
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("stale processing");
        await Assert.That(result.Data).ContainsKey("staleProcessingCount").And.Value.IsEqualTo(1);
    }

    [Test]
    public async Task CheckHealthAsyncWhenDeadLetteredAtThresholdReturnsDegraded()
    {
        var setup = CreateHealthCheck(
            new EmailDispatchProcessorSettings
            {
                Enabled = true,
                Mode = EmailDispatchProcessorMode.Quartz,
                HealthDeadLetterWarningThreshold = 1,
                ConsumerId = "dead-letter"
            },
            new QuartzSchedulerSettings { Enabled = true },
            deadLetteredCount: 1);
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("dead-lettered");
        await Assert.That(result.Data).ContainsKey("deadLetteredCount").And.Value.IsEqualTo(1);
    }

    [Test]
    public async Task CheckHealthAsyncWhenUnknownAtThresholdReturnsDegradedWhileParkedRowsRemainInformational()
    {
        var setup = CreateHealthCheck(
            new EmailDispatchProcessorSettings
            {
                Enabled = true,
                Mode = EmailDispatchProcessorMode.Quartz,
                HealthUnknownWarningThreshold = 1,
                ConsumerId = "unknown-health"
            },
            new QuartzSchedulerSettings { Enabled = true },
            unknownCount: 1,
            parkedCount: 5,
            optionalReminderDeferralActive: true);
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("reconciliation");
        await Assert.That(result.Data).ContainsKey("unknownCount").And.Value.IsEqualTo(1);
        await Assert.That(result.Data).ContainsKey("parkedCount").And.Value.IsEqualTo(5);
        await Assert.That(result.Data).ContainsKey("optionalReminderDeferralActive").And.Value.IsEqualTo(true);
    }

    [Test]
    public async Task CheckHealthAsyncWhenDueRetryBacklogAtThresholdReturnsDegraded()
    {
        var setup = CreateHealthCheck(
            new EmailDispatchProcessorSettings
            {
                Enabled = true,
                Mode = EmailDispatchProcessorMode.Quartz,
                HealthDueDispatchWarningThreshold = 2,
                ConsumerId = "retry-backlog"
            },
            new QuartzSchedulerSettings { Enabled = true },
            dueDispatchCount: 2,
            retryScheduledCount: 1);
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("due backlog");
        await Assert.That(result.Data).ContainsKey("dueDispatchCount").And.Value.IsEqualTo(2);
        await Assert.That(result.Data).ContainsKey("retryScheduledCount").And.Value.IsEqualTo(1);
    }

    [Test]
    public async Task CheckHealthAsyncWhenOldestOrTenantBacklogCrossesThresholdReturnsDegradedWithBoundedData()
    {
        var tenantId = Guid.CreateVersion7();
        var setup = CreateHealthCheck(
            new EmailDispatchProcessorSettings
            {
                Enabled = true,
                Mode = EmailDispatchProcessorMode.Quartz,
                HealthDueDispatchWarningThreshold = 100,
                HealthOldestPendingWarningSeconds = 60,
                HealthTenantBacklogWarningThreshold = 3,
                HealthTenantSampleLimit = 1,
                ConsumerId = "fairness-health"
            },
            new QuartzSchedulerSettings { Enabled = true },
            dueDispatchCount: 3,
            oldestDueCreatedAt: DateTime.UtcNow.AddMinutes(-5),
            dueDispatchByTenant: new Dictionary<Guid, int> { [tenantId] = 3, [Guid.CreateVersion7()] = 2 });
        using var services = setup.Services;

        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("oldest pending");
        var oldestActivePendingAgeSeconds = (double)result.Data["oldestActivePendingAgeSeconds"];
        await Assert.That(oldestActivePendingAgeSeconds).IsGreaterThan(60);
        var tenantBacklogSample = (IReadOnlyDictionary<Guid, int>)result.Data["tenantBacklogSample"];
        await Assert.That(tenantBacklogSample).HasSingleItem();
        await Assert.That(tenantBacklogSample.Single().Key).IsEqualTo(tenantId);
    }

    [Test]
    public async Task PublicHealthSerializationRedactsTenantBacklogAndSensitiveEmailIdentifiers()
    {
        var tenantId = Guid.CreateVersion7();
        var setup = CreateHealthCheck(
            new EmailDispatchProcessorSettings
            {
                Enabled = true,
                Mode = EmailDispatchProcessorMode.Quartz,
                ConsumerId = "public-health"
            },
            new QuartzSchedulerSettings { Enabled = true },
            dueDispatchByTenant: new Dictionary<Guid, int> { [tenantId] = 2 });
        using var services = setup.Services;
        var result = await setup.HealthCheck.CheckHealthAsync(new HealthCheckContext());
        var sensitiveData = result.Data.ToDictionary(entry => entry.Key, entry => entry.Value);
        sensitiveData["tenantId"] = tenantId;
        sensitiveData["recipientAddress"] = "person@example.test";
        sensitiveData["subject"] = "Private subject";
        sensitiveData["body"] = "Private body";
        sensitiveData["reportEvidence"] = "Private evidence";
        sensitiveData["eventTitle"] = "Private event";
        sensitiveData["userId"] = 42;
        sensitiveData["providerId"] = 17;
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["email-dispatch"] = new(
                    result.Status,
                    result.Description,
                    TimeSpan.Zero,
                    result.Exception,
                    sensitiveData)
            },
            TimeSpan.Zero);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthCheckResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        var serializedData = JsonNode.Parse(json)!["checks"]!.AsArray()[0]!["data"]!.AsObject();
        await Assert.That(serializedData["tenantId"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(serializedData["userId"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(serializedData["providerId"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(json).DoesNotContain(tenantId.ToString());
        await Assert.That(json).DoesNotContain("person@example.test");
        await Assert.That(json).DoesNotContain("Private subject");
        await Assert.That(json).DoesNotContain("Private body");
        await Assert.That(json).DoesNotContain("Private evidence");
        await Assert.That(json).DoesNotContain("Private event");
        await Assert.That(json).Contains(HealthCheckResponseWriter.RedactedValue);
    }

    private static (
        EmailDispatchHealthCheck HealthCheck,
        ServiceProvider Services,
        IEmailDispatchOutboxRepository Repository) CreateHealthCheck(
            EmailDispatchProcessorSettings settings,
            QuartzSchedulerSettings? schedulerOptions = null,
            int dueDispatchCount = 0,
            int retryScheduledCount = 0,
            int staleProcessingCount = 0,
            int deadLetteredCount = 0,
            int unknownCount = 0,
            int parkedCount = 0,
            bool optionalReminderDeferralActive = false,
            DateTime? oldestDueCreatedAt = null,
            IReadOnlyDictionary<Guid, int>? dueDispatchByTenant = null)
    {
        var repository = Substitute.For<IEmailDispatchOutboxRepository>();
        repository.CountDueDispatchAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(dueDispatchCount));
        repository.CountRetryScheduledAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(retryScheduledCount));
        repository.CountStaleProcessingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(staleProcessingCount));
        repository.CountDeadLetteredAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(deadLetteredCount));
        repository.CountUnknownAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(unknownCount));
        repository.CountParkedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(parkedCount));
        repository.IsOptionalReminderDeferralActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(optionalReminderDeferralActive));
        repository.GetOldestDueCreatedAtAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(oldestDueCreatedAt));
        repository.CountDueDispatchByTenantAsync(
                Arg.Any<DateTime>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<Guid, int>>(
                (dueDispatchByTenant ?? new Dictionary<Guid, int>())
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .Take(call.ArgAt<int>(1))
                .ToDictionary()));

        var services = new ServiceCollection()
            .AddSingleton(repository)
            .BuildServiceProvider();
        var healthCheck = new EmailDispatchHealthCheck(
            Options.Create(settings),
            Options.Create(schedulerOptions ?? new QuartzSchedulerSettings()),
            services.GetRequiredService<IServiceScopeFactory>(),
            CreateMetrics());

        return (healthCheck, services, repository);
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }
}
