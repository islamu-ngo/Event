using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.ControlPlane.Handlers.Queries;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.ControlPlane.Queries;

public sealed class GetControlPlaneOperationsQueryHandlerTests
{
    [Test]
    public async Task Handle_WhenOperationalWarningsExist_ReturnsBoundedStatusCards()
    {
        var outboxRepository = Substitute.For<IOutboxRepository>();
        var emailRepository = Substitute.For<IEmailDispatchOutboxRepository>();
        var eventReportRepository = Substitute.For<IEventReportRepository>();
        var tenantRepository = Substitute.For<ITenantRepository>();
        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var storageService = Substitute.For<IInstanceStorageSettingService>();
        var smtpService = Substitute.For<IInstanceSmtpSettingService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailDispatchProcessor:HealthDueDispatchWarningThreshold"] = "5",
                ["EmailDispatchProcessor:HealthStaleProcessingWarningThreshold"] = "1",
                ["EmailDispatchProcessor:HealthDeadLetterWarningThreshold"] = "1",
                ["EmailDispatchProcessor:ProcessingLeaseTimeoutSeconds"] = "60",
                ["Reporting:Health:FailedProviderSyncWarningThreshold"] = "1",
                ["Reporting:Health:StuckProviderSyncMinutes"] = "60"
            })
            .Build();
        outboxRepository.GetPendingBatch(100, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 100).Select(_ => CreateOutbox(OutboxMessageStatus.Pending)).ToList());
        outboxRepository.GetFailedEntries(100, Arg.Any<CancellationToken>())
            .Returns(
            [
                CreateOutbox(OutboxMessageStatus.Failed),
                CreateOutbox(OutboxMessageStatus.DeadLettered)
            ]);
        emailRepository.CountDueDispatchAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(5);
        emailRepository.CountRetryScheduledAsync(Arg.Any<CancellationToken>()).Returns(2);
        emailRepository.CountStaleProcessingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(1);
        emailRepository.CountDeadLetteredAsync(Arg.Any<CancellationToken>()).Returns(1);
        eventReportRepository.CountExternalLinksBySyncStateAsync(EventReportSyncState.Pending, Arg.Any<CancellationToken>()).Returns(7);
        eventReportRepository.CountExternalLinksBySyncStateAsync(EventReportSyncState.Failed, Arg.Any<CancellationToken>()).Returns(2);
        eventReportRepository.CountExternalLinksBySyncStateAsync(EventReportSyncState.Disabled, Arg.Any<CancellationToken>()).Returns(1);
        eventReportRepository.CountExternalLinksBySyncStateAsync(EventReportSyncState.Ignored, Arg.Any<CancellationToken>()).Returns(3);
        eventReportRepository.CountExternalLinksBySyncStateBeforeAsync(
            EventReportSyncState.Pending,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>()).Returns(4);
        tenantRepository.GetActiveTenantCountAsync().Returns(6);
        settingsResolver.ResolveGroupAsync<TenantDelegationSettingGroup>(Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(CreateTenantDelegationSettings());
        storageService.ReadSettingsAsync(Arg.Any<CancellationToken>()).Returns(new InstanceStorageSettingsDto
        {
            Provider = StorageProviders.S3Compatible,
            ProviderStatus = new InstanceStorageProviderStatusDto
            {
                Provider = StorageProviders.S3Compatible,
                IsAvailable = false,
                FailureCode = "provider_unavailable",
                Message = "Provider unavailable"
            },
            Usage = new InstanceStorageUsageDto
            {
                UsedBytes = 10,
                ReservedBytes = 3,
                QuarantinedBytes = 2,
                ObjectCount = 4
            }
        });
        smtpService.ReadSettingsAsync().Returns(new InstanceSmtpSettingsDto());
        var handler = new GetControlPlaneOperationsQueryHandler(
            outboxRepository,
            emailRepository,
            eventReportRepository,
            tenantRepository,
            settingsResolver,
            storageService,
            smtpService,
            configuration);

        var result = await handler.Handle(new GetControlPlaneOperationsQuery(), CancellationToken.None);

        await Assert.That(result.Statuses.Select(status => status.Key))
            .IsEquivalentTo(["general-outbox", "email-dispatch", "moderation-reporting", "storage"]);
        var outbox = result.Statuses.Single(status => status.Key == "general-outbox");
        await Assert.That(outbox.Severity).IsEqualTo("critical");
        await Assert.That(outbox.Metrics.Single(metric => metric.Key == "due").IsCapped).IsTrue();
        await Assert.That(outbox.Metrics.Single(metric => metric.Key == "dead-lettered").Value).IsEqualTo(1);

        var email = result.Statuses.Single(status => status.Key == "email-dispatch");
        await Assert.That(email.Severity).IsEqualTo("critical");
        await Assert.That(email.Metrics.Single(metric => metric.Key == "due").Value).IsEqualTo(5);

        var storage = result.Statuses.Single(status => status.Key == "storage");
        await Assert.That(storage.Severity).IsEqualTo("critical");
        await Assert.That(storage.Metrics.Single(metric => metric.Key == "object-count").Value).IsEqualTo(4);

        var moderationReporting = result.Statuses.Single(status => status.Key == "moderation-reporting");
        await Assert.That(moderationReporting.Severity).IsEqualTo("warning");
        await Assert.That(moderationReporting.Metrics.Single(metric => metric.Key == "pending-sync").Value).IsEqualTo(7);
        await Assert.That(moderationReporting.Metrics.Single(metric => metric.Key == "stuck-pending-sync").Value).IsEqualTo(4);
        await Assert.That(moderationReporting.Metrics.Single(metric => metric.Key == "failed-sync").Value).IsEqualTo(2);
        await Assert.That(moderationReporting.Metrics.Single(metric => metric.Key == "reporting-locked-tenants").Value).IsEqualTo(6);
        await Assert.That(moderationReporting.Metrics.Single(metric => metric.Key == "reporting-unlocked-tenants").Value).IsEqualTo(0);
        await Assert.That(moderationReporting.Metrics.Single(metric => metric.Key == "osprey-locked-tenants").Value).IsEqualTo(0);
        await Assert.That(moderationReporting.Metrics.Single(metric => metric.Key == "coop-locked-tenants").Value).IsEqualTo(6);
        await Assert.That(result.Warnings.Select(warning => warning.Code))
            .Contains("email_dispatch_dead_letters");
        await Assert.That(result.Warnings.Select(warning => warning.Code))
            .Contains("storage_provider_unavailable");
        await Assert.That(result.Warnings.Select(warning => warning.Code))
            .Contains("moderation_reporting_provider_sync_failures");
        await Assert.That(result.Warnings.Select(warning => warning.Code))
            .Contains("moderation_reporting_provider_sync_stuck");
        await Assert.That(result.Warnings.Single(warning => warning.Code == "email_dispatch_dead_letters").Remediation)
            .Contains("dead-lettered email dispatch rows");
        await Assert.That(result.Warnings.Single(warning => warning.Code == "storage_provider_unavailable").Remediation)
            .Contains("Verify storage provider settings");
        await Assert.That(result.Warnings.Single(warning => warning.Code == "moderation_reporting_provider_sync_failures").Remediation)
            .DoesNotContain("provider id");
    }

    private static OutboxMessage CreateOutbox(OutboxMessageStatus status) => new()
    {
        Id = Guid.NewGuid(),
        AggregateType = "TestAggregate",
        AggregateId = Guid.NewGuid(),
        EventType = "TestEvent",
        Status = status,
        CreatedAt = DateTime.UtcNow
    };

    private static TenantDelegationSettingGroup CreateTenantDelegationSettings()
    {
        var group = new TenantDelegationSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.TenantDelegation.LockReportingProviders] = BoolSetting(true),
            [GovernanceSettingKeys.TenantDelegation.LockTenantOspreyProvider] = BoolSetting(false),
            [GovernanceSettingKeys.TenantDelegation.LockTenantCoopProvider] = BoolSetting(true)
        });

        return group;
    }

    private static ResolvedSetting BoolSetting(bool value) => new()
    {
        Value = SettingValueSerializer.Serialize(value)
    };
}
