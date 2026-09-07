using Explore.Infrastructure;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EmailDispatchRetentionSettingsValidatorTests
{
    private readonly EmailDispatchRetentionSettingsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new EmailDispatchRetentionSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateUnsafeSettingsReturnsAllFailures()
    {
        var result = _validator.Validate(null, new EmailDispatchRetentionSettings
        {
            InitialDelaySeconds = -1,
            PollingIntervalMinutes = 0,
            MaxTenantsPerPass = 0,
            BatchSize = 0,
            RetentionDays = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRetentionSettings.InitialDelaySeconds));
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRetentionSettings.PollingIntervalMinutes));
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRetentionSettings.MaxTenantsPerPass));
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRetentionSettings.BatchSize));
        await Assert.That(result.FailureMessage).Contains(nameof(EmailDispatchRetentionSettings.RetentionDays));
    }
}
