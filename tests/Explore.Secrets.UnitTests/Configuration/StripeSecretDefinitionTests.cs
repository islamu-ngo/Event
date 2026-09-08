using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class StripeSecretDefinitionTests
{
    [Test]
    public async Task RegistryDefinesTwoInstanceScopedStripeSecretPurposes()
    {
        var expectations = new Dictionary<string, string>
        {
            ["payments.stripe.platform_secret_key"] = "STRIPE_PLATFORM_SECRET_KEY",
            ["payments.stripe.webhook_secret"] = "STRIPE_WEBHOOK_SECRET"
        };

        var stripeKeys = SecretDefinitionRegistry.All.Keys
            .Where(key => key.StartsWith("payments.stripe.", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(stripeKeys).IsEquivalentTo(expectations.Keys);

        foreach (var expectation in expectations)
        {
            var settingKey = expectation.Key;
            var definition = SecretDefinitionRegistry.GetRequired(settingKey);

            await Assert.That(definition.Key).IsEqualTo(settingKey);
            await Assert.That(definition.AllowedScopes.SequenceEqual([SecretScope.Instance])).IsTrue();
            await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/stripe");
            await Assert.That(definition.DefaultInfisicalKey).IsEqualTo(expectation.Value);
            await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo(expectation.Value);
            await Assert.That(definition.IsBootstrapSecret).IsFalse();

            var instanceBinding = SecretBinding.CreateInfisical(
                settingKey,
                SecretScope.Instance,
                scopeId: null,
                environment: "prod",
                path: definition.DefaultInfisicalPath,
                key: definition.DefaultInfisicalKey);

            await Assert.That(instanceBinding.Scope).IsEqualTo(SecretScope.Instance);
            await Assert.That(instanceBinding.ScopeId).IsNull();
            await Assert.That(instanceBinding.InfisicalPath).IsEqualTo("/stripe");
            await Assert.That(instanceBinding.InfisicalEnvironment).IsEqualTo("prod");
            await Assert.That(instanceBinding.InfisicalKey).IsEqualTo(definition.DefaultInfisicalKey);

            await Assert.That(() => SecretBinding.CreateInfisical(
                settingKey,
                SecretScope.Tenant,
                Guid.NewGuid(),
                environment: "prod",
                path: definition.DefaultInfisicalPath,
                key: definition.DefaultInfisicalKey)).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task RegistryDefinesInstanceScopedPromotionCodeLookupHmacKey()
    {
        var definition = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Promotions.CodeLookupHmacKey);

        await Assert.That(definition.Key).IsEqualTo("promotions.code_lookup_hmac_key");
        await Assert.That(definition.AllowedScopes.SequenceEqual([SecretScope.Instance])).IsTrue();
        await Assert.That(definition.DefaultInfisicalPath).IsEqualTo("/promotions");
        await Assert.That(definition.DefaultInfisicalKey).IsEqualTo("PROMOTIONS_CODE_LOOKUP_HMAC_KEY");
        await Assert.That(definition.DefaultEnvironmentVariableName).IsEqualTo("PROMOTIONS_CODE_LOOKUP_HMAC_KEY");
        await Assert.That(definition.IsBootstrapSecret).IsFalse();

        var binding = SecretBinding.CreateEnvironmentVariable(
            definition.Key,
            SecretScope.Instance,
            scopeId: null,
            definition.DefaultEnvironmentVariableName,
            qualifier: "v17");

        await Assert.That(binding.Qualifier).IsEqualTo("v17");
        await Assert.That(() => SecretBinding.CreateEnvironmentVariable(
            definition.Key,
            SecretScope.Tenant,
            Guid.CreateVersion7(),
            definition.DefaultEnvironmentVariableName,
            qualifier: "v17")).Throws<ArgumentException>();
    }
}
