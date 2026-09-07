using Event.Api.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[Category(TestCategories.Email)]
public sealed class SmtpHealthCheckRegistrationTests
{
    private static readonly TimeSpan ExpectedSmtpReadinessTimeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task SmtpReadinessRegistrationUsesBoundedTimeout()
    {
        await using var factory = new CustomWebApplicationFactory();

        var options = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = options.Registrations.Single(registration => registration.Name == "smtp");

        await Assert.That(registration.Timeout).IsEqualTo(ExpectedSmtpReadinessTimeout);
        await Assert.That(registration.FailureStatus).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(registration.Tags).Contains("ready");
        await Assert.That(registration.Tags).Contains("smtp");
        await Assert.That(registration.Tags).Contains("infrastructure");
    }

    [Test]
    public async Task WebPushReadinessRegistrationUsesReadyDispatchInfrastructureTags()
    {
        await using var factory = new CustomWebApplicationFactory();

        var options = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = options.Registrations.Single(registration => registration.Name == "web-push-dispatch");

        await Assert.That(registration.FailureStatus).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(registration.Tags).Contains("ready");
        await Assert.That(registration.Tags).Contains("web-push");
        await Assert.That(registration.Tags).Contains("dispatch");
        await Assert.That(registration.Tags).Contains("infrastructure");
    }
}
