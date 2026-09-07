using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class AdmissionCredentialSecretDefinitionTests
{
    [Test]
    public async Task DedicatedAdmissionCredentialKeyUsesServerOnlyInstanceSecretCoordinates()
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(
            SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey);

        await Assert.That(definition.Key).IsEqualTo("admissions.credential_lookup_hmac_key");
        await Assert.That(definition.Key).IsNotEqualTo(SecretDefinitionRegistry.Keys.Promotions.CodeLookupHmacKey);
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/admissions");
        await Assert.That(definition.DefaultInfisicalKey).IsEqualTo("ADMISSIONS_CREDENTIAL_LOOKUP_HMAC_KEY");
        await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo("ADMISSIONS_CREDENTIAL_LOOKUP_HMAC_KEY");
        await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
        await Assert.That(definition.AllowedSources).Contains(SecretSourceType.Infisical);
        await Assert.That(definition.AllowedSources).Contains(SecretSourceType.EnvironmentVariable);
    }
}
