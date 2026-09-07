// ABOUTME: Exercises optional-email readiness through real settings and the shared HTTP health endpoint.
// ABOUTME: Keeps SMTP degradation independent of core readiness and excludes provider-controlled diagnostics.

using System.Net;
using System.Text.Json.Nodes;
using Explore.API.Configuration;
using Explore.API.HealthChecks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Mail;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Metrics;
using NSubstitute;
using NSubstitute.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed class EmailOptionalHealthTests
{
    public enum EmailScenario { Disabled, ConfiguredDisabled, Incomplete, Accepted, Failed, TransientFailure, Timeout }
    public enum AuthorityFailure { Database, SecretTimeout, SecretCancellation, SecretIo }

    private const string ProviderCanary = "remote-smtp-diagnostic-canary";

    [Test]
    [Arguments(EmailScenario.Disabled, HealthStatus.Healthy)]
    [Arguments(EmailScenario.ConfiguredDisabled, HealthStatus.Healthy)]
    [Arguments(EmailScenario.Incomplete, HealthStatus.Degraded)]
    [Arguments(EmailScenario.Accepted, HealthStatus.Healthy)]
    [Arguments(EmailScenario.Failed, HealthStatus.Degraded)]
    [Arguments(EmailScenario.TransientFailure, HealthStatus.Degraded)]
    [Arguments(EmailScenario.Timeout, HealthStatus.Degraded)]
    public async Task Readiness_EmailStateDoesNotEvictHealthyCore(EmailScenario scenario, HealthStatus expected)
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        if (scenario is not EmailScenario.Disabled and not EmailScenario.Incomplete) await database.ConfigureInstanceAsync();
        if (scenario == EmailScenario.ConfiguredDisabled) await database.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, false);
        if (scenario == EmailScenario.Incomplete) await database.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);
        var transport = new Probe(scenario);
        await using var app = await StartAsync(database, transport);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(CheckStatus(body, "smtp")).IsEqualTo(expected);
        await Assert.That(body).DoesNotContain(ProviderCanary);
        await Assert.That(transport.Attempted).IsEqualTo(scenario is EmailScenario.Accepted or EmailScenario.Failed or EmailScenario.TransientFailure or EmailScenario.Timeout);
        var capability = await database.Capabilities.ResolveAsync(null);
        await Assert.That(capability.Enabled).IsEqualTo(scenario is not EmailScenario.Disabled and not EmailScenario.ConfiguredDisabled);
    }

    [Test]
    public async Task Readiness_CapabilityAuthorityFailureRemainsUnhealthy()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        await database.Context.DisposeAsync();
        var transport = new Probe(EmailScenario.Accepted);
        await using var app = await StartAsync(database, transport);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(CheckStatus(await response.Content.ReadAsStringAsync(), "smtp")).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(transport.Attempted).IsFalse();
    }

    [Test]
    public async Task Readiness_OptionalEmailDoesNotMaskCoreDatabaseFailure()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        await using var core = await SmtpSettingsDatabase.CreateAsync();
        await core.Context.DisposeAsync();
        await using var app = await StartAsync(database, new Probe(EmailScenario.Disabled), coreDatabase: core.Context);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(CheckStatus(body, "smtp")).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(CheckStatus(body, "database")).IsEqualTo(HealthStatus.Unhealthy);
    }

    [Test]
    [Arguments(AuthorityFailure.Database)]
    [Arguments(AuthorityFailure.SecretTimeout)]
    [Arguments(AuthorityFailure.SecretCancellation)]
    [Arguments(AuthorityFailure.SecretIo)]
    public async Task Readiness_AuthorityFailureDuringDiagnosticResolutionRemainsUnhealthy(AuthorityFailure failure)
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        await database.ConfigureInstanceAsync();
        await using var app = await StartAsync(database, new FailingAuthorityProbe(database, failure));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(CheckStatus(body, "smtp")).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(body).DoesNotContain(ProviderCanary);
    }

    [Test]
    [Arguments(false, EmailDispatchProcessorMode.Quartz, HealthStatus.Healthy)]
    [Arguments(true, EmailDispatchProcessorMode.Disabled, HealthStatus.Healthy)]
    [Arguments(true, EmailDispatchProcessorMode.Quartz, HealthStatus.Degraded)]
    public async Task Readiness_OptionalDispatchSchedulingDoesNotEvictCore(bool enabled, EmailDispatchProcessorMode mode, HealthStatus expected)
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        await using var app = await StartAsync(database, new Probe(EmailScenario.Disabled), new EmailDispatchProcessorSettings
        {
            Enabled = enabled, Mode = mode
        });
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(CheckStatus(await response.Content.ReadAsStringAsync(), "email-dispatch")).IsEqualTo(expected);
    }

    [Test]
    public async Task Readiness_EnabledDispatchCannotHideDatabaseFailure()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        await database.Context.DisposeAsync();
        await using var app = await StartAsync(database, new Probe(EmailScenario.Disabled), new EmailDispatchProcessorSettings
        {
            Enabled = true, Mode = EmailDispatchProcessorMode.HostedService
        });
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(CheckStatus(await response.Content.ReadAsStringAsync(), "email-dispatch")).IsEqualTo(HealthStatus.Unhealthy);
    }

    private static HealthStatus CheckStatus(string json, string name) => Enum.Parse<HealthStatus>(JsonNode.Parse(json)!["checks"]!.AsArray()
        .Single(check => check!["name"]!.GetValue<string>() == name)!["status"]!.GetValue<string>());

    private static async Task<WebApplication> StartAsync(SmtpSettingsDatabase database, IEmailConnectionTester transport,
        EmailDispatchProcessorSettings? dispatch = null, ExploreDbContext? coreDatabase = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddPrometheusExporter());
        builder.Services.AddSingleton<IEmailConnectionTester>(transport);
        builder.Services.AddSingleton<IEmailDeliveryCapabilityResolver>(database.Capabilities);
        var health = builder.Services.AddHealthChecks()
            .AddCheck<SmtpHealthCheck>("smtp", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
        if (coreDatabase is not null)
        {
            builder.Services.AddSingleton(coreDatabase);
            health.AddDbContextCheck<ExploreDbContext>("database", tags: ["ready"]);
        }
        if (dispatch is not null)
        {
            builder.Services.Configure<EmailDispatchProcessorSettings>(options =>
            {
                options.Enabled = dispatch.Enabled;
                options.Mode = dispatch.Mode;
            });
            builder.Services.Configure<QuartzSchedulerSettings>(options => options.Enabled = false);
            builder.Services.AddMetrics();
            builder.Services.AddSingleton<BusinessMetrics>();
            builder.Services.AddSingleton<IEmailDispatchOutboxRepository>(new EmailDispatchOutboxRepository(database.Context));
            health.AddCheck<EmailDispatchHealthCheck>("email-dispatch", tags: ["ready"]);
        }
        var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class FailingAuthorityProbe(SmtpSettingsDatabase database, AuthorityFailure failure) : IEmailConnectionTester
    {
        public async Task<EmailResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (failure == AuthorityFailure.Database)
            {
                await database.Context.DisposeAsync();
            }
            else
            {
                database.Secrets.Configure().ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                    .Returns<SecretResolutionResult>(_ => failure switch
                    {
                        AuthorityFailure.SecretTimeout => throw new TimeoutException(ProviderCanary),
                        AuthorityFailure.SecretCancellation => throw new OperationCanceledException(ProviderCanary),
                        _ => throw new IOException(ProviderCanary)
                    });
            }
            return await new SmtpEmailService(database.Smtp, NullLogger<SmtpEmailService>.Instance)
                .TestConnectionAsync(cancellationToken);
        }
    }

    private sealed class Probe(EmailScenario scenario) : IEmailConnectionTester
    {
        public bool Attempted { get; private set; }

        public async Task<EmailResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            Attempted = true;
            if (scenario == EmailScenario.Timeout)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return EmailResult.Fail(ProviderCanary, outcome: SmtpDeliveryOutcome.TransientFailure);
                }
            }
            return scenario switch
            {
                EmailScenario.Accepted => EmailResult.Ok(ProviderCanary),
                EmailScenario.Failed => EmailResult.Fail(ProviderCanary),
                EmailScenario.TransientFailure => EmailResult.Fail(ProviderCanary, outcome: SmtpDeliveryOutcome.TransientFailure),
                _ => throw new InvalidOperationException("SMTP must not be probed for unavailable configuration.")
            };
        }
    }
}
