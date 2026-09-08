using System.ComponentModel.DataAnnotations;
using Explore.Application.Services.Webhooks;
using Explore.Infrastructure.Webhooks;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class QueueDrainSettingsValidatorTests
{
    [Test]
    public async Task IntegrationSyncRejectsInvalidHealthThresholds()
    {
        var result = new IntegrationSyncProcessorSettingsValidator().Validate(null,
            new IntegrationSyncProcessorSettings
            {
                HealthDueWarningThreshold = 0,
                HealthStaleWarningThreshold = 0,
                HealthAmbiguousWarningThreshold = 0
            });

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Count().IsEqualTo(3);
    }

    [Test]
    public async Task PdsRejectsInvalidHealthThresholds()
    {
        var result = new PdsSyncSettingsValidator().Validate(null,
            new PdsSyncSettings
            {
                HealthDueWarningThreshold = 0,
                HealthStaleWarningThreshold = 0,
                HealthDeadLetterWarningThreshold = 0
            });

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Count().IsEqualTo(3);
    }

    [Test]
    public async Task WebhookDrainsRejectInvalidHealthThresholds()
    {
        var replay = new WebhookBulkReplaySettingsValidator().Validate(null,
            new WebhookBulkReplaySettings
            {
                HealthQueuedWarningThreshold = 0,
                HealthExecutingWarningThreshold = 0
            });
        var publication = new WebhookProviderPublicationProcessorSettingsValidator().Validate(null,
            new WebhookProviderPublicationProcessorSettings
            {
                HealthDueWarningThreshold = 0,
                HealthStaleWarningThreshold = 0,
                HealthUnknownWarningThreshold = 0
            });

        await Assert.That(replay.Failed).IsTrue();
        await Assert.That(replay.Failures).Count().IsEqualTo(2);
        await Assert.That(publication.Failed).IsTrue();
        await Assert.That(publication.Failures).Count().IsEqualTo(3);
    }

    [Test]
    public async Task IncomingWebhookRejectsInvalidIntakeHealthThresholds()
    {
        var settings = new IncomingWebhookProcessingSettings
        {
            IntakeBacklogWarningThreshold = 0,
            IntakeStaleLeaseWarningThreshold = 0
        };
        var failures = new List<ValidationResult>();

        bool valid = Validator.TryValidateObject(settings, new ValidationContext(settings), failures, true);

        await Assert.That(valid).IsFalse();
        await Assert.That(failures.Count).IsEqualTo(2);
    }
}
