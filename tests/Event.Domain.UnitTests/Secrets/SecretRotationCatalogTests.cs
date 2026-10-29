using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Secrets;

public sealed class SecretRotationCatalogTests
{
    [Test]
    public async Task EveryRegisteredConsumerHasOneNormativeRotationProfile()
    {
        SecretRotationProfile[] profiles = SecretDefinitionRegistry.All.Keys
            .Select(SecretDefinitionRegistry.GetRotationProfile)
            .ToArray();

        await Assert.That(profiles.Length).IsEqualTo(SecretDefinitionRegistry.All.Count);
        await Assert.That(profiles.All(profile =>
            !string.IsNullOrWhiteSpace(profile.Owner)
            && profile.CandidateValidationRequired
            && !string.IsNullOrWhiteSpace(profile.BreakGlassAction))).IsTrue();
    }
}
