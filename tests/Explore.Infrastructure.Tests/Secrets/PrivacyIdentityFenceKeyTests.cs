using System.Security.Cryptography;
using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class PrivacyIdentityFenceKeyTests
{
    private const string Variable = "PRIVACY_ERASURE_IDENTITY_FENCE_KEY";

    [Test]
    public async Task SelectedEnvironmentKeySurvivesProviderRecreation()
    {
        byte[] material = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var authority = Configuration(Convert.ToBase64String(material));
            await using var firstOwner = Owner(authority);
            await using var secondOwner = Owner(authority);
            using var first = await new PrivacyIdentityFenceKeyProvider(firstOwner, authority).ResolveAsync(default);
            using var restarted = await new PrivacyIdentityFenceKeyProvider(secondOwner, authority).ResolveAsync(default);
            await Assert.That(first.KeyId).IsEqualTo("selected-key");
            await Assert.That(restarted.KeyId).IsEqualTo(first.KeyId);
            var account = new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:opaque");
            await Assert.That(first.Fingerprint(account)).IsEqualTo(restarted.Fingerprint(account));
            await Assert.That(first.VerificationTag).IsEqualTo(restarted.VerificationTag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    [Test]
    public async Task MissingSelectedEnvironmentSecretCannotFallBackToApplicationConfiguration()
    {
        using var authority = Configuration(null);
        using var ordinary = Configuration(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var owner = Owner(authority);
        await Assert.That(async () =>
        {
            using var key = await new PrivacyIdentityFenceKeyProvider(owner, ordinary).ResolveAsync(default);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SharedUserSecretsAreRejectedOutsideDevelopmentAndTesting()
    {
        using var authority = Configuration(null);
        await Assert.That(() => SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.UserSecrets, "Production", SecretConsumerRole.Api), authority))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CatalogueKeepsFenceKeyDeploymentOwnedAndNonrotatableLive()
    {
        SecretDefinition definition = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.PrivacyIdentityFenceKey);
        await Assert.That(definition.IsBootstrapSecret).IsTrue();
        await Assert.That(definition.AllowedScopes).IsEquivalentTo([SecretScope.Instance]);
        await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo(Variable);
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/api");
        await Assert.That(SecretDefinitionRegistry.GetRotationProfile(definition.Key).Mode)
            .IsEqualTo(SecretRotationMode.UnsupportedLive);
    }

    [Test]
    public async Task RegistrationDoesNotRequireFingerprintAuthorityUntilInvoked()
    {
        using var authority = Configuration(null);
        await using var owner = Owner(authority);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(authority);
        services.AddSingleton(owner);
        services.AddNativeSecretResolution();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var keys = scope.ServiceProvider.GetRequiredService<IPrivacyIdentityFenceKeyProvider>();
        await Assert.That(async () =>
        {
            using var key = await keys.ResolveAsync(default);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MalformedOrWeakSelectedMaterialCannotProduceAFingerprintAuthority(bool weak)
    {
        using var authority = Configuration(weak
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(8)) : Guid.NewGuid().ToString());
        await using var owner = Owner(authority);
        Func<Task> resolve = async () =>
        {
            using var key = await new PrivacyIdentityFenceKeyProvider(owner, authority).ResolveAsync(default);
        };
        if (weak)
            await Assert.That(resolve).Throws<ArgumentException>();
        else
            await Assert.That(resolve).Throws<InvalidOperationException>();
    }

    private static SecretAuthorityAcquisition Owner(IConfiguration authority) =>
        SecretAuthorityAcquisition.OpenLocal(new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), authority);

    private static ConfigurationRoot Configuration(string? value) =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Variable] = value,
            ["PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID"] = "selected-key"
        }).Build();
}
