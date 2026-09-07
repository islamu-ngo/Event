using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Validation;

namespace Explore.Secrets.UnitTests.Validation;

public sealed class SecretProviderOptionsValidatorTests
{
    private readonly SecretProviderOptionsValidator _validator = new();

    [Test]
    public async Task UnspecifiedProviderFailsClosed()
    {
        var result = _validator.Validate(null, new SecretProviderOptions());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("Environment, Infisical, or UserSecrets");
    }

    [Test]
    public async Task EnvironmentProviderNeedsNoExternalBootstrapCredentials()
    {
        var result = _validator.Validate(
            null,
            new SecretProviderOptions { Provider = SecretProviderType.Environment });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task UserSecretsProviderNeedsNoExternalBootstrapCredentials()
    {
        var result = _validator.Validate(
            null,
            new SecretProviderOptions { Provider = SecretProviderType.UserSecrets });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task IncompleteInfisicalBootstrapFailsClosed()
    {
        var result = _validator.Validate(
            null,
            new SecretProviderOptions
            {
                Provider = SecretProviderType.Infisical,
                Infisical = new InfisicalOptions()
            });

        await Assert.That(result.Succeeded).IsFalse();
    }

    [Test]
    public async Task CompleteInfisicalBootstrapIsAccepted()
    {
        var result = _validator.Validate(
            null,
            new SecretProviderOptions
            {
                Provider = SecretProviderType.Infisical,
                Infisical = new InfisicalOptions
                {
                    Url = "https://infisical.example.com",
                    ProjectId = "project",
                    ClientId = "client",
                    ClientSecret = SecretsTestValues.CreateSecret(),
                    Environment = "production"
                }
            });

        await Assert.That(result.Succeeded).IsTrue();
    }

}
