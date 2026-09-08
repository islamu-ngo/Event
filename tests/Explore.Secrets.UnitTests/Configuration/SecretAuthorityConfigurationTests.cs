using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.UnitTests.Configuration;

[NotInParallel]
public sealed class SecretAuthorityConfigurationTests
{
    [Test]
    public async Task Build_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        IConfiguration configuration = ProviderConfiguration(SecretProviderType.UserSecrets);

        Action act = () => SecretAuthorityConfiguration.Build(configuration, "Production", "/api");

        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("secret_authority_user_secrets_environment_invalid");
    }

    [Test]
    public async Task Build_WhenUserSecretsIsSelected_DoesNotReadEnvironmentFallback()
    {
        string key = $"AUTHORITY_CANARY_{Guid.CreateVersion7():N}";
        string canary = Guid.CreateVersion7().ToString("N");
        Environment.SetEnvironmentVariable(key, canary);
        try
        {
            IConfiguration configuration = ProviderConfiguration(SecretProviderType.UserSecrets);

            IConfiguration authority = SecretAuthorityConfiguration.Build(configuration, "Testing", "/api");

            await Assert.That(authority[key]).IsNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Test]
    public async Task PreserveProviderSelection_WhenAuthorityContainsConflictingSelectors_KeepsUserSecrets()
    {
        IConfiguration conflictingAuthority = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Provider"] = "Environment",
                ["SECRET_PROVIDER"] = "Infisical",
            })
            .Build();

        IConfiguration locked = SecretAuthorityConfiguration.PreserveProviderSelection(
            conflictingAuthority,
            SecretProviderType.UserSecrets);

        await Assert.That(SecretAuthorityConfiguration.GetRequiredProvider(locked))
            .IsEqualTo(SecretProviderType.UserSecrets);
        await Assert.That(locked["SECRET_PROVIDER"]).IsNull();
    }

    private static IConfiguration ProviderConfiguration(SecretProviderType provider) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = provider.ToString(),
        }).Build();
}
