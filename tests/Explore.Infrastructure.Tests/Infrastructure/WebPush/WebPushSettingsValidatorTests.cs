using Explore.Infrastructure.WebPush;
using WebPush;

namespace Explore.Infrastructure.Tests.Infrastructure.WebPush;

public sealed class WebPushSettingsValidatorTests
{
    private readonly WebPushSettingsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, ValidSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateMissingVapidSecretsReturnsFailure()
    {
        var result = _validator.Validate(null, new WebPushSettings { Enabled = true });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("VapidSubject");
        await Assert.That(result.FailureMessage).Contains("VapidPublicKey");
        await Assert.That(result.FailureMessage).Contains("VapidPrivateKey");
    }

    [Test]
    public async Task ValidateInvalidRetryWindowReturnsFailure()
    {
        var result = _validator.Validate(null, ValidSettings() with
        {
            InitialRetryDelaySeconds = 60,
            MaxRetryDelaySeconds = 10
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("MaxRetryDelaySeconds");
    }

    [Test]
    public async Task ValidateInvalidHealthThresholdsReturnFailure()
    {
        var result = _validator.Validate(null, ValidSettings() with
        {
            HealthDueDispatchWarningThreshold = 0,
            HealthStaleProcessingWarningThreshold = 0,
            HealthTerminalFailureWarningThreshold = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("HealthDueDispatchWarningThreshold");
        await Assert.That(result.FailureMessage).Contains("HealthStaleProcessingWarningThreshold");
        await Assert.That(result.FailureMessage).Contains("HealthTerminalFailureWarningThreshold");
    }

    [Test]
    public async Task ValidateUnsafeOpenPathReturnsFailure()
    {
        var result = _validator.Validate(null, ValidSettings() with
        {
            NotificationOpenPath = "https://evil.example.test/notifications"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("NotificationOpenPath");
    }

    private static WebPushSettings ValidSettings() => new()
    {
        VapidSubject = "mailto:ops@example.test",
        VapidPublicKey = Keys.PublicKey,
        VapidPrivateKey = Keys.PrivateKey,
        Enabled = true
    };

    private static VapidDetails Keys { get; } = VapidHelper.GenerateVapidKeys();
}
