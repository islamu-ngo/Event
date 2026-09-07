using Explore.Application.Configuration;

namespace Event.Application.UnitTests.Configuration;

public sealed class PrivacyErasureOptionsTests
{
    [Test]
    public async Task AuthorityRetention_IsDerivedFromConfiguredHorizonAndSafetyMargin()
    {
        var options = new PrivacyErasureOptions
        {
            MaximumBackupHorizon = TimeSpan.FromDays(14),
            AuthorityRetentionSafetyMargin = TimeSpan.FromHours(12),
        };

        options.Validate();

        await Assert.That(options.AuthorityRetention).IsEqualTo(TimeSpan.FromDays(14.5));
    }
}
