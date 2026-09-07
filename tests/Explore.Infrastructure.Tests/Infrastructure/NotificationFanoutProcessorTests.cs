using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.NotificationFanout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class NotificationFanoutProcessorTests
{
    [Test]
    public async Task Validate_DefaultSettings_Succeeds()
    {
        var validator = new NotificationFanoutProcessorSettingsValidator();

        ValidateOptionsResult result = validator.Validate(null, new NotificationFanoutProcessorSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_UnsafeConcurrencyAndWatermarks_Fails()
    {
        var validator = new NotificationFanoutProcessorSettingsValidator();
        var settings = new NotificationFanoutProcessorSettings
        {
            MaxClaimsPerRound = 9,
            MaxActiveClaims = 8,
            MaxActiveClaimsPerTenant = 9,
            OptionalReminderBacklogHighWatermark = 10,
            OptionalReminderBacklogLowWatermark = 10
        };

        ValidateOptionsResult result = validator.Validate(null, settings);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("MaxClaimsPerRound");
        await Assert.That(result.FailureMessage).Contains("MaxActiveClaimsPerTenant");
        await Assert.That(result.FailureMessage).Contains("OptionalReminderBacklogLowWatermark");
    }

    [Test]
    public async Task ProcessRoundAsync_ResolvesEachClaimProcessorFromFreshScope()
    {
        var runRepository = Substitute.For<INotificationFanoutRunRepository>();
        NotificationFanoutClaim[] claims =
        [
            Claim(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()),
            Claim(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7())
        ];
        runRepository.ClaimDueRoundAsync(
                Arg.Any<NotificationFanoutClaimRoundRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new NotificationFanoutClaimRoundResult(claims, 2, 0, 0, 0));
        runRepository.GetProcessorSnapshotAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationFanoutProcessorSnapshot(0, 0, 0, 0, 0, 0, 0, null, false));

        int scopedProcessorCount = 0;
        var services = new ServiceCollection();
        services.AddSingleton(runRepository);
        services.AddScoped(_ =>
        {
            Interlocked.Increment(ref scopedProcessorCount);
            return CreateUnavailablePageProcessor(runRepository);
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        var processor = new NotificationFanoutProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NotificationFanoutProcessorSettings()),
            TimeProvider.System,
            CreateMetrics(),
            NullLogger<NotificationFanoutProcessor>.Instance);

        NotificationFanoutProcessorRoundResult result = await processor.ProcessRoundAsync(
            CancellationToken.None);

        await Assert.That(result.ClaimedCount).IsEqualTo(2);
        await Assert.That(result.UnavailableCount).IsEqualTo(2);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(scopedProcessorCount).IsEqualTo(2);
    }

    [Test]
    public async Task HealthCheck_ExpiredClaim_DegradesWithAggregateDataOnly()
    {
        var repository = Substitute.For<INotificationFanoutRunRepository>();
        repository.GetProcessorSnapshotAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationFanoutProcessorSnapshot(
                2,
                1,
                1,
                1,
                1,
                3,
                5,
                DateTime.UtcNow.AddMinutes(-1),
                true));
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        await using ServiceProvider provider = services.BuildServiceProvider();
        var healthCheck = new NotificationFanoutHealthCheck(
            Options.Create(new NotificationFanoutProcessorSettings()),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            CreateMetrics());

        HealthCheckResult result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Data["expiredClaimCount"]).IsEqualTo(1);
        await Assert.That(result.Data["optionalReminderDeferralActive"]).IsEqualTo(true);
        await Assert.That(result.Data.Keys).DoesNotContain("consumerId");
        await Assert.That(result.Data.Keys).DoesNotContain("tenantId");
        await Assert.That(result.Data.Keys).DoesNotContain("recipientId");
    }

    private static NotificationFanoutPageProcessor CreateUnavailablePageProcessor(
        INotificationFanoutRunRepository runRepository)
    {
        var occurrenceRepository = Substitute.For<INotificationFanoutOccurrenceRepository>();
        var registrationRepository = Substitute.For<IRegistrationInventoryRepository>();
        var materializationService = Substitute.For<INotificationFanoutRecipientMaterializationService>();
        return new NotificationFanoutPageProcessor(
            occurrenceRepository,
            registrationRepository,
            runRepository,
            materializationService,
            new NotificationFanoutRecipientTemplateFactory(),
            TimeProvider.System);
    }

    private static NotificationFanoutClaim Claim(Guid runId, Guid tenantId, Guid occurrenceId) =>
        new(runId, tenantId, occurrenceId, Guid.CreateVersion7(), 1, 1, null);

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }
}
