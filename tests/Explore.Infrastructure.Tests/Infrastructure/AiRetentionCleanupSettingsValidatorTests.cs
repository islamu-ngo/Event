using Explore.Infrastructure;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiRetentionCleanupSettingsValidatorTests
{
    private readonly AiRetentionCleanupSettingsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new AiRetentionCleanupSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateNegativeInitialDelayReturnsFailure()
    {
        var result = _validator.Validate(null, new AiRetentionCleanupSettings
        {
            InitialDelaySeconds = -1
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(AiRetentionCleanupSettings.InitialDelaySeconds));
    }

    [Test]
    public async Task ValidateZeroPollingIntervalReturnsFailure()
    {
        var result = _validator.Validate(null, new AiRetentionCleanupSettings
        {
            PollingIntervalMinutes = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(AiRetentionCleanupSettings.PollingIntervalMinutes));
    }

    [Test]
    public async Task ValidateZeroMaxTenantsReturnsFailure()
    {
        var result = _validator.Validate(null, new AiRetentionCleanupSettings
        {
            MaxTenantsPerPass = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(AiRetentionCleanupSettings.MaxTenantsPerPass));
    }
}
