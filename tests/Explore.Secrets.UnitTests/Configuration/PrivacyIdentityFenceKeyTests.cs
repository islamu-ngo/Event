using System.Security.Cryptography;
using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Secrets.Services;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.UnitTests.Configuration;

[NotInParallel]
public sealed class PrivacyIdentityFenceKeyTests
{
    private const string Variable = "PRIVACY_ERASURE_IDENTITY_FENCE_KEY";

    [Test]
    public async Task SelectedEnvironmentKeySurvivesProviderRecreation()
    {
        string? original = Environment.GetEnvironmentVariable(Variable);
        byte[] material = RandomNumberGenerator.GetBytes(32);
        try
        {
            Environment.SetEnvironmentVariable(Variable, Convert.ToBase64String(material));
            using PrivacyIdentityFenceKey first = await new PrivacyIdentityFenceKeyProvider(
                Configuration("Environment")).ResolveAsync(CancellationToken.None);
            using PrivacyIdentityFenceKey restarted = await new PrivacyIdentityFenceKeyProvider(
                Configuration("Environment")).ResolveAsync(CancellationToken.None);
            var account = new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:opaque");
            await Assert.That(first.Fingerprint(account)).IsEqualTo(restarted.Fingerprint(account));
            await Assert.That(first.VerificationTag).IsEqualTo(restarted.VerificationTag);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, original);
            CryptographicOperations.ZeroMemory(material);
        }
    }

    [Test]
    public async Task MissingSelectedEnvironmentSecretCannotFallBackToApplicationConfiguration()
    {
        string? original = Environment.GetEnvironmentVariable(Variable);
        byte[] material = RandomNumberGenerator.GetBytes(32);
        try
        {
            Environment.SetEnvironmentVariable(Variable, null);
            IConfiguration configuration = Configuration("Environment");
            configuration[Variable] = Convert.ToBase64String(material);
            await Assert.That(async () =>
            {
                using var key = await new PrivacyIdentityFenceKeyProvider(configuration)
                    .ResolveAsync(CancellationToken.None);
            }).Throws<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, original);
            CryptographicOperations.ZeroMemory(material);
        }
    }

    [Test]
    public async Task SharedUserSecretsAreRejectedOutsideDevelopmentAndTesting()
    {
        await Assert.That(async () =>
        {
            using var key = await new PrivacyIdentityFenceKeyProvider(Configuration("UserSecrets", "Production"))
                .ResolveAsync(CancellationToken.None);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CatalogueKeepsFenceKeyDeploymentOwnedAndNonrotatableLive()
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.PrivacyIdentityFenceKey);
        await Assert.That(definition.IsBootstrapSecret).IsTrue();
        await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
        await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo(Variable);
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/privacy");
        await Assert.That(SecretDefinitionRegistry.GetRotationProfile(definition.Key).Mode)
            .IsEqualTo(SecretRotationMode.UnsupportedLive);
    }

    private static IConfiguration Configuration(string provider, string environment = "Testing") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = provider,
            ["DOTNET_ENVIRONMENT"] = environment,
            ["PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID"] = "test-key"
        }).Build();
}
