using Explore.Infrastructure;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class StorageReconciliationSettingsValidatorTests
{
    private readonly StorageReconciliationSettingsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new StorageReconciliationSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateInvalidSchedulingSettingsReturnsFailure()
    {
        var result = _validator.Validate(null, new StorageReconciliationSettings
        {
            InitialDelaySeconds = -1,
            PollingIntervalMinutes = 0,
            BatchSize = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(StorageReconciliationSettings.InitialDelaySeconds));
        await Assert.That(result.FailureMessage).Contains(nameof(StorageReconciliationSettings.PollingIntervalMinutes));
        await Assert.That(result.FailureMessage).Contains(nameof(StorageReconciliationSettings.BatchSize));
    }

    [Test]
    public async Task ValidateNegativeGraceSettingsReturnsFailure()
    {
        var result = _validator.Validate(null, new StorageReconciliationSettings
        {
            MissingObjectQuarantineGraceHours = -1,
            OrphanFileQuarantineGraceHours = -1,
            DeleteGraceHours = -1
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(nameof(StorageReconciliationSettings.MissingObjectQuarantineGraceHours));
        await Assert.That(result.FailureMessage).Contains(nameof(StorageReconciliationSettings.OrphanFileQuarantineGraceHours));
        await Assert.That(result.FailureMessage).Contains(nameof(StorageReconciliationSettings.DeleteGraceHours));
    }
}
