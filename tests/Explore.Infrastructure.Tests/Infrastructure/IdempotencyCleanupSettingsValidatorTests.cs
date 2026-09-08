using Explore.Infrastructure;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class IdempotencyCleanupSettingsValidatorTests
{
    private readonly IdempotencyCleanupSettingsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new IdempotencyCleanupSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateNegativeInitialDelayReturnsFailure()
    {
        var result = _validator.Validate(null, new IdempotencyCleanupSettings
        {
            InitialDelaySeconds = -1
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(IdempotencyCleanupSettings.InitialDelaySeconds));
    }

    [Test]
    public async Task ValidateZeroPollingIntervalReturnsFailure()
    {
        var result = _validator.Validate(null, new IdempotencyCleanupSettings
        {
            PollingIntervalMinutes = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(IdempotencyCleanupSettings.PollingIntervalMinutes));
    }

    [Test]
    public async Task ValidateZeroBatchSizeReturnsFailure()
    {
        var result = _validator.Validate(null, new IdempotencyCleanupSettings
        {
            BatchSize = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(IdempotencyCleanupSettings.BatchSize));
    }

    [Test]
    public async Task ValidateNegativeExpirationGraceReturnsFailure()
    {
        var result = _validator.Validate(null, new IdempotencyCleanupSettings
        {
            ExpirationGraceHours = -1
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(IdempotencyCleanupSettings.ExpirationGraceHours));
    }
}
