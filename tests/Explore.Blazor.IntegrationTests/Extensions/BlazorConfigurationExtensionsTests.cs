using Explore.Blazor.Extensions;
using Microsoft.Extensions.Configuration;

namespace Explore.Blazor.IntegrationTests.Extensions;

[NotInParallel]
public sealed class BlazorConfigurationExtensionsTests
{
    [Test]
    public async Task AddSecretAuthorityConfiguration_WhenAspireApiReferenceExists_DoesNotMapProviderApiEndpoint()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["API_ENDPOINT"] = "https://localhost:7039",
            ["services:explore-api:https:0"] = "https://localhost:7211"
        });
        await Assert.That(configuration["ExploreApi:BaseUrl"]).IsNull();
        await Assert.That(configuration["services:explore-api:https:0"]).IsEqualTo("https://localhost:7211");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_WhenAspireApiReferenceIsMissing_MapsProviderApiEndpoint()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["API_ENDPOINT"] = "https://localhost:7039"
        });
        await Assert.That(configuration["ExploreApi:BaseUrl"]).IsEqualTo("https://localhost:7039");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_WhenOnlyLegacyCredentialAliasExists_DoesNotMapIt()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Keycloak__ClientSecret"] = Guid.CreateVersion7().ToString("N")
        });

        await Assert.That(configuration["Keycloak:ClientSecret"]).IsNull();
    }

    [Test]
    public async Task AddInfisical_WhenCredentialsExistOnlyInMergedConfiguration_FailsClosed()
    {
        string[] keys =
        [
            "SecretProvider__Infisical__Url",
            "SecretProvider__Infisical__ProjectId",
            "SecretProvider__Infisical__ClientId",
            "SecretProvider__Infisical__ClientSecret",
            "SecretProvider__Infisical__Environment",
            "INFISICAL_URL",
            "INFISICAL_PROJECT_ID",
            "INFISICAL_CLIENT_ID",
            "INFISICAL_CLIENT_SECRET",
            "INFISICAL_ENV",
        ];
        var previous = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Infisical:Url"] = "https://attacker.example.test",
                ["SecretProvider:Infisical:ProjectId"] = "project-id",
                ["SecretProvider:Infisical:ClientId"] = "client-id",
                ["SecretProvider:Infisical:ClientSecret"] = Guid.CreateVersion7().ToString("N"),
                ["SecretProvider:Infisical:Environment"] = "staging",
            }).Build();
            var builder = new ConfigurationBuilder();

            Action act = () => builder.AddInfisical(configuration);

            await Assert.That(act).Throws<InvalidOperationException>();
        }
        finally
        {
            foreach (var pair in previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "UserSecrets",
        });

        Action act = () => builder.AddSecretAuthorityConfiguration("Production");

        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message)
            .IsEqualTo("secret_authority_user_secrets_environment_invalid");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        var previous = values.Keys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        try
        {
            foreach (var pair in values)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            var lowerAuthority = new Dictionary<string, string?>(values, StringComparer.Ordinal)
            {
                ["SecretProvider:Provider"] = "Environment",
            };
            var builder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddInMemoryCollection(lowerAuthority);
            builder.AddSecretAuthorityConfiguration("Testing");
            return builder.Build();
        }
        finally
        {
            foreach (var pair in previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
