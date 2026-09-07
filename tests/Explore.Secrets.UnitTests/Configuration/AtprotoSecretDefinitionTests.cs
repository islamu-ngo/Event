using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class AtprotoSecretDefinitionTests
{
    [Test]
    public async Task RegistryDefinesSeparateInstanceScopedAtprotoKeyPurposes()
    {
        var expectations = new Dictionary<string, string>
        {
            [SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks] = "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS",
            [SecretDefinitionRegistry.Keys.Atproto.SessionEncryptionKeyRing] = "ATPROTO_SESSION_ENCRYPTION_KEYRING",
            [SecretDefinitionRegistry.Keys.Atproto.SessionJwtPrivateJwks] = "ATPROTO_SESSION_JWT_PRIVATE_JWKS"
        };

        foreach (var expectation in expectations)
        {
            var definition = SecretDefinitionRegistry.GetRequired(expectation.Key);
            await Assert.That(definition.AllowedScopes.SequenceEqual([SecretScope.Instance])).IsTrue();
            await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/atproto");
            await Assert.That(definition.DefaultInfisicalKey).IsEqualTo(expectation.Value);
            await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo(expectation.Value);
            await Assert.That(definition.IsBootstrapSecret).IsFalse();
        }
    }
}
