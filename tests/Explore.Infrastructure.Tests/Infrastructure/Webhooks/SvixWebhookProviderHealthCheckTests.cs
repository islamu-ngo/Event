using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class SvixWebhookProviderHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenSvixProviderNotSelected_ReturnsHealthyWithoutResolvingSecrets()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        var healthCheck = CreateHealthCheck(
            secretResolver,
            new WebhookOptions { Enabled = true, Provider = WebhookOptions.ProviderLocal });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["svixProviderSelected"]).IsEqualTo(false);
        await secretResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default, default);
    }

    [Test]
    public async Task CheckHealthAsync_WhenSvixAuthTokenCannotResolve_ReturnsUnhealthyWithoutSecretData()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync("webhooks.svix.auth_token", null, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);
        var healthCheck = CreateHealthCheck(
            secretResolver,
            new WebhookOptions
            {
                Enabled = true,
                Provider = WebhookOptions.ProviderSvix,
                Svix = SupportedSelfHostedOptions(operationalWebhookSecretRef: null)
            });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["authTokenResolved"]).IsEqualTo(false);
        await Assert.That(result.Data.Keys).DoesNotContain("baseUrl");
        await Assert.That(result.Data.Keys).DoesNotContain("authToken");
        await Assert.That(result.Data.Keys).DoesNotContain("secretRef");
    }

    [Test]
    public async Task CheckHealthAsync_WhenSvixProcessorIsDisabled_ReturnsUnhealthyBeforeSecretResolution()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        var healthCheck = CreateHealthCheck(
            secretResolver,
            new WebhookOptions
            {
                Enabled = true,
                Provider = WebhookOptions.ProviderSvix,
                Svix = SupportedSelfHostedOptions()
            },
            processorEnabled: false);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Data["processorEnabled"]).IsEqualTo(false);
        await secretResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default, default);
    }

    [Test]
    public async Task CheckHealthAsync_WhenSvixSecretsResolve_ReturnsHealthy()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync("webhooks.svix.auth_token", null, Arg.Any<CancellationToken>())
            .Returns(Resolved("webhooks.svix.auth_token", Guid.NewGuid().ToString("N")));
        secretResolver.ResolveAsync("webhooks.svix.operational_webhook_secret", null, Arg.Any<CancellationToken>())
            .Returns(Resolved("webhooks.svix.operational_webhook_secret", Guid.NewGuid().ToString("N")));
        var healthCheck = CreateHealthCheck(
            secretResolver,
            new WebhookOptions
            {
                Enabled = true,
                Provider = WebhookOptions.ProviderComposite,
                Svix = SupportedSelfHostedOptions()
            });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["svixProviderSelected"]).IsEqualTo(true);
        await Assert.That(result.Data["authTokenResolved"]).IsEqualTo(true);
        await Assert.That(result.Data["operationalWebhookSecretResolved"]).IsEqualTo(true);
        await Assert.That(result.Data["deploymentKind"]).IsEqualTo(nameof(SvixDeploymentKind.SelfHosted));
        await Assert.That(result.Data["conformanceExecutedTestCount"]).IsEqualTo(11);
        await Assert.That(result.Data["exactMessageLookupSupported"]).IsEqualTo(false);
        await Assert.That(result.Data["providerCapabilityCount"]).IsEqualTo(4);
        await Assert.That((string[])result.Data["providerCapabilityCodes"])
            .IsEquivalentTo(["ENDPOINT_MANAGEMENT", "PAYLOAD_INSPECTION", "APP_PORTAL", "EVENT_CATALOG"]);
        await Assert.That(result.Data.Keys).DoesNotContain("baseUrl");
        await Assert.That(result.Data.Keys).DoesNotContain("authToken");
        await Assert.That(result.Data.Keys).DoesNotContain("secretRef");
    }

    [Test]
    public async Task CheckHealthAsync_WhenOperationalWebhookSecretCannotResolve_ReturnsHealthyWithSafeFlag()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        secretResolver.ResolveAsync("webhooks.svix.auth_token", null, Arg.Any<CancellationToken>())
            .Returns(Resolved("webhooks.svix.auth_token", Guid.NewGuid().ToString("N")));
        secretResolver.ResolveAsync("webhooks.svix.operational_webhook_secret", null, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);
        var healthCheck = CreateHealthCheck(
            secretResolver,
            new WebhookOptions
            {
                Enabled = true,
                Provider = WebhookOptions.ProviderSvix,
                Svix = SupportedSelfHostedOptions()
            });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["operationalWebhookSecretResolved"]).IsEqualTo(false);
    }

    [Test]
    public async Task CheckHealthAsync_WhenProfileIsUnsupported_ReturnsUnhealthyWithoutResolvingSecrets()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        var svixOptions = SupportedSelfHostedOptions();
        svixOptions.ProviderVersion = "unsupported";
        var healthCheck = CreateHealthCheck(
            secretResolver,
            new WebhookOptions
            {
                Enabled = true,
                Provider = WebhookOptions.ProviderSvix,
                Svix = svixOptions
            });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("absent from the conformance matrix");
        await Assert.That(result.Data.Keys).DoesNotContain("authTokenResolved");
        await secretResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default, default);
    }

    [Test]
    public async Task CheckHealthAsync_WhenManagedProfileHasNoEvidence_ReturnsUnhealthyWithoutResolvingSecrets()
    {
        var secretResolver = Substitute.For<ISecretResolver>();
        var healthCheck = CreateHealthCheck(
            secretResolver,
            new WebhookOptions
            {
                Enabled = true,
                Provider = WebhookOptions.ProviderSvix,
                Svix = new WebhookSvixOptions
                {
                    BaseUrl = null,
                    Environment = SvixConformanceProfileRegistry.ManagedEnvironment,
                    ProviderVersion = SvixConformanceProfileRegistry.ManagedProviderVersion,
                    CapabilityPolicyVersion = SvixConformanceProfileRegistry.ManagedCapabilityPolicyVersion,
                    AuthTokenSecretRef = "webhooks.svix.auth_token"
                }
            });

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("no executed conformance evidence");
        await Assert.That(result.Data["conformanceExecutedTestCount"]).IsEqualTo(0);
        await Assert.That(result.Data["exactMessageLookupSupported"]).IsEqualTo(false);
        await secretResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default, default);
    }

    private static SvixWebhookProviderHealthCheck CreateHealthCheck(
        ISecretResolver secretResolver,
        WebhookOptions options,
        bool processorEnabled = true)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => secretResolver);
        var serviceProvider = services.BuildServiceProvider();

        return new SvixWebhookProviderHealthCheck(
            new StaticOptionsMonitor<WebhookOptions>(options),
            Options.Create(new WebhookProviderPublicationProcessorSettings
            {
                Enabled = processorEnabled
            }),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            CreateMetrics());
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private static SecretResolutionResult Resolved(string settingKey, string value) =>
        SecretResolutionResult.Resolved(new ResolvedSecret(
            settingKey,
            value,
            SecretSourceType.EnvironmentVariable,
            SecretScope.Instance,
            ScopeId: null,
            DateTimeOffset.UtcNow));

    private static WebhookSvixOptions SupportedSelfHostedOptions(
        string? operationalWebhookSecretRef = "webhooks.svix.operational_webhook_secret") =>
        new()
        {
            BaseUrl = "http://svix:8071",
            Environment = SvixConformanceProfileRegistry.SelfHostedEnvironment,
            ProviderVersion = SvixConformanceProfileRegistry.SelfHostedProviderVersion,
            CapabilityPolicyVersion = SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion,
            AuthTokenSecretRef = "webhooks.svix.auth_token",
            OperationalWebhookSecretRef = operationalWebhookSecretRef
        };

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
