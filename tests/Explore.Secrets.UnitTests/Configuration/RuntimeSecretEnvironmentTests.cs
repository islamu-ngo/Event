using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class RuntimeSecretEnvironmentTests
{
    [Test]
    [Arguments(SecretConsumer.Api)]
    [Arguments(SecretConsumer.Bff)]
    [Arguments(SecretConsumer.Combined)]
    [Arguments(SecretConsumer.Migration)]
    public async Task ProcessProjectionKeepsOnlyItsIdentityAndNoProviderAdministration(SecretConsumer consumer)
    {
        var inputs = Inputs();
        var target = new Dictionary<string, object>
        {
            ["KEYCLOAK_ADMIN_PASSWORD"] = SecretsTestValues.CreateSecret(),
            ["CERBOS_ADMIN_PASSWORD_HASH"] = SecretsTestValues.CreateSecret(),
            ["Cerbos__AdminApi__Endpoints__0"] = "http://cerbos.test",
            ["ISLAMU_ASPIRE_MODE"] = "FullLocal",
            ["Storage__Local__RootPath"] = "/data"
        };
        string inheritedCanary = SecretsTestValues.CreateSecret();
        inputs["INFISICAL_CLIENT_SECRET"] = inheritedCanary;
        inputs["UNDECLARED_SECRET"] = inheritedCanary;
        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, SecretProviderType.Infisical);

        await Assert.That(target["SecretProvider__Provider"]).IsEqualTo("Infisical");
        await Assert.That(target["SecretProvider__Infisical__ClientSecret"])
            .IsEqualTo(inputs[$"INFISICAL_{consumer.ToString().ToUpperInvariant()}_CLIENT_SECRET"]!);
        await Assert.That(target["KEYCLOAK_ADMIN_PASSWORD"]).IsEqualTo(string.Empty);
        await Assert.That(target["CERBOS_ADMIN_PASSWORD_HASH"]).IsEqualTo(string.Empty);
        await Assert.That(target["UNDECLARED_SECRET"]).IsEqualTo(string.Empty);
        await Assert.That(target.Values.Contains(inheritedCanary)).IsFalse();
        await Assert.That(target["Storage__Local__RootPath"]).IsEqualTo("/data");
        await Assert.That(target["Cerbos__AdminApi__Endpoints__0"]).IsEqualTo("http://cerbos.test");
        foreach (SecretConsumer sibling in Enum.GetValues<SecretConsumer>().Where(value => value != consumer))
            await Assert.That(target.Values.Contains(
                inputs[$"INFISICAL_{sibling.ToString().ToUpperInvariant()}_CLIENT_SECRET"]!)).IsFalse();
    }

    [Test]
    public async Task PublicAudienceAndPermittedClientsRemainIndependentOfSecretSource()
    {
        var inputs = Inputs();
        var target = new Dictionary<string, object>
        {
            ["KEYCLOAK__AUDIENCE"] = "event-resource",
            ["KEYCLOAK__PERMITTEDCLIENTS__0"] = "event-browser"
        };
        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys,
            SecretConsumer.Api, SecretProviderType.Infisical);
        await Assert.That(target["KEYCLOAK__AUDIENCE"]).IsEqualTo("event-resource");
        await Assert.That(target["KEYCLOAK__PERMITTEDCLIENTS__0"]).IsEqualTo("event-browser");
    }

    [Test]
    public async Task MissingPurposeIdentityCannotFallBackToSharedBootstrap()
    {
        var inputs = Inputs();
        inputs.Remove("INFISICAL_API_CLIENT_SECRET");
        inputs["INFISICAL_CLIENT_SECRET"] = SecretsTestValues.CreateSecret();
        await Assert.That(() => RuntimeSecretEnvironment.Apply(
            new Dictionary<string, object>(), Configuration(inputs), inputs.Keys,
            SecretConsumer.Api, SecretProviderType.Infisical)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReusedIdentityIsRejectedBeforeProjectLaunch()
    {
        var inputs = Inputs();
        inputs["INFISICAL_BFF_CLIENT_ID"] = inputs["INFISICAL_API_CLIENT_ID"];
        await Assert.That(() => RuntimeSecretEnvironment.Apply(
            new Dictionary<string, object>(), Configuration(inputs), inputs.Keys,
            SecretConsumer.Api, SecretProviderType.Infisical)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EnvironmentProjectionSeparatesFrontendAndLifecycleCanaries()
    {
        var inputs = Inputs();
        inputs["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = SecretsTestValues.CreateSecret();
        inputs["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"] = SecretsTestValues.CreateSecret();
        inputs["MAIL_SMTP_PASSWORD"] = SecretsTestValues.CreateSecret();
        var api = new Dictionary<string, object>();
        var bff = new Dictionary<string, object>();
        var combined = new Dictionary<string, object>();
        RuntimeSecretEnvironment.Apply(api, Configuration(inputs), inputs.Keys, SecretConsumer.Api, SecretProviderType.Environment);
        RuntimeSecretEnvironment.Apply(bff, Configuration(inputs), inputs.Keys, SecretConsumer.Bff, SecretProviderType.Environment);
        RuntimeSecretEnvironment.Apply(combined, Configuration(inputs), inputs.Keys, SecretConsumer.Combined, SecretProviderType.Environment);
        await Assert.That(api["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsEqualTo(string.Empty);
        await Assert.That(api["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]).IsEqualTo(inputs["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]!);
        await Assert.That(api["MAIL_SMTP_PASSWORD"]).IsEqualTo(inputs["MAIL_SMTP_PASSWORD"]!);
        await Assert.That(bff["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]).IsEqualTo(string.Empty);
        await Assert.That(bff["MAIL_SMTP_PASSWORD"]).IsEqualTo(string.Empty);
        await Assert.That(bff["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsEqualTo(inputs["KEYCLOAK_BLAZOR_CLIENT_SECRET"]!);
        await Assert.That(combined["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsEqualTo(inputs["KEYCLOAK_BLAZOR_CLIENT_SECRET"]!);
        await Assert.That(combined["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]).IsEqualTo(inputs["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]!);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> inputs) =>
        new ConfigurationBuilder().AddInMemoryCollection(inputs).Build();

    private static Dictionary<string, string?> Inputs()
    {
        var inputs = new Dictionary<string, string?>
        {
            ["INFISICAL_URL"] = "https://authority.test",
            ["INFISICAL_PROJECT_ID"] = Guid.CreateVersion7().ToString("N"),
            ["INFISICAL_ENV"] = "test"
        };
        foreach (string consumer in new[] { "API", "BFF", "COMBINED", "MIGRATION" })
        {
            inputs[$"INFISICAL_{consumer}_CLIENT_ID"] = Guid.CreateVersion7().ToString("N");
            inputs[$"INFISICAL_{consumer}_CLIENT_SECRET"] = SecretsTestValues.CreateSecret();
        }
        return inputs;
    }
}
