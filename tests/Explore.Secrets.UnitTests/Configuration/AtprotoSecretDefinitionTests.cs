using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class AtprotoSecretDefinitionTests
{
    [Test]
    public async Task RegistryDefinesSeparateInstanceScopedAtprotoKeyPurposes()
    {
        var expectations = new Dictionary<string, (string Path, string Name)>
        {
            [SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks] =
                ("/runtime/bff/atproto/oauth", "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS"),
            [SecretDefinitionRegistry.Keys.Atproto.SessionEncryptionKeyRing] =
                ("/atproto", "ATPROTO_SESSION_ENCRYPTION_KEYRING"),
            [SecretDefinitionRegistry.Keys.Atproto.SessionJwtPrivateJwks] =
                ("/atproto", "ATPROTO_SESSION_JWT_PRIVATE_JWKS")
        };

        foreach (var expectation in expectations)
        {
            var definition = SecretDefinitionRegistry.GetRequired(expectation.Key);
            await Assert.That(definition.AllowedScopes.SequenceEqual([SecretScope.Instance])).IsTrue();
            await Assert.That(definition.DefaultInfisicalPath).IsEqualTo(expectation.Value.Path);
            await Assert.That(definition.DefaultInfisicalKey).IsEqualTo(expectation.Value.Name);
            await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo(expectation.Value.Name);
            await Assert.That(definition.IsBootstrapSecret).IsFalse();
        }
    }
}
