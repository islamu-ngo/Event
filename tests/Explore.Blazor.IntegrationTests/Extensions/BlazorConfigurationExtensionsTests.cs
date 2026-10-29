using Explore.Blazor.Configuration;
using Explore.Blazor.Extensions;
using Explore.Blazor.IntegrationTests.Configuration;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Blazor.IntegrationTests.Extensions;

[NotInParallel]
public sealed class BlazorConfigurationExtensionsTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreparedBff_PreservesExplicitOrdinaryMetadataTransportPolicy(bool requireHttps)
    {
        await using var host = new BffSecretPreparationTestHost();
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:RequireHttpsMetadata"] = requireHttps.ToString()
        });
        await host.PrepareLocalAsync(new()
        {
            ["KEYCLOAK_ENDPOINT"] = "https://identity.example.test",
            ["KEYCLOAK_REALM"] = "event",
            ["KEYCLOAK_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
            ["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = Guid.NewGuid().ToString("N")
        });

        await Assert.That(host.Builder.Configuration.GetValue<bool>("Keycloak:RequireHttpsMetadata"))
            .IsEqualTo(requireHttps);
    }

    [Test]
    [Arguments("services:explore-api:https:0")]
    [Arguments("services:explore-api:http:0")]
    [Arguments("services__explore-api__https__0")]
    [Arguments("services__explore-api__http__0")]
    public async Task PreparedBff_WhenAspireApiReferenceExists_DoesNotMapProviderApiEndpoint(string reference)
    {
        await using var host = new BffSecretPreparationTestHost();
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_ENDPOINT"] = "https://localhost:7039",
            [reference] = "https://localhost:7211"
        });
        await host.PrepareLocalAsync();

        await Assert.That(host.Builder.Configuration["ExploreApi:BaseUrl"]).IsNull();
        await Assert.That(host.Builder.Configuration[reference]).IsEqualTo("https://localhost:7211");
    }

    [Test]
    public async Task PreparedBff_WhenAspireApiReferenceIsMissing_MapsOrdinaryApiEndpoint()
    {
        await using var host = new BffSecretPreparationTestHost();
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_ENDPOINT"] = "https://localhost:7039"
        });
        await host.PrepareLocalAsync();

        await Assert.That(host.Builder.Configuration["ExploreApi:BaseUrl"]).IsEqualTo("https://localhost:7039");
    }

    [Test]
    public async Task PreparedBff_PreservesExplicitOrdinaryApiEndpointAndAuthenticationProvider()
    {
        await using var host = new BffSecretPreparationTestHost();
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_ENDPOINT"] = "https://ignored.example.test",
            ["ExploreApi:BaseUrl"] = "https://api.example.test",
            ["AUTHENTICATION_PROVIDER"] = "Keycloak",
            ["Authentication:Provider"] = "Google"
        });
        await host.PrepareLocalAsync();

        await Assert.That(host.Builder.Configuration["ExploreApi:BaseUrl"]).IsEqualTo("https://api.example.test");
        await Assert.That(host.Builder.Configuration["Authentication:Provider"]).IsEqualTo("Google");
    }

    [Test]
    public async Task PreparedBff_MapsOrdinaryAuthenticationProviderWhenStructuredSettingIsAbsent()
    {
        await using var host = new BffSecretPreparationTestHost();
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AUTHENTICATION_PROVIDER"] = "Google"
        });
        await host.PrepareLocalAsync();

        await Assert.That(host.Builder.Configuration["Authentication:Provider"]).IsEqualTo("Google");
    }

    [Test]
    public async Task PreparedBff_WhenOnlyLegacyCredentialAliasExists_DoesNotMapIt()
    {
        await using var host = new BffSecretPreparationTestHost();
        string canary = Guid.NewGuid().ToString("N");
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:ClientSecret"] = canary,
            ["Google:ClientSecret"] = canary
        });
        await host.PrepareLocalAsync(new()
        {
            ["Keycloak__ClientSecret"] = canary,
            ["Google__ClientSecret"] = canary
        });

        await Assert.That(host.Builder.Configuration["Keycloak:ClientSecret"]).IsNull();
        await Assert.That(host.Builder.Configuration["Google:ClientSecret"]).IsNull();
        await Assert.That(host.Receipts.Snapshot()).IsEmpty();
    }

    [Test]
    public async Task PreparedBff_CannotImportBackendOrOrdinarySettingsFromSelectedAuthority()
    {
        await using var host = new BffSecretPreparationTestHost();
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_ENDPOINT"] = "https://api.example.test",
            ["AUTHENTICATION_PROVIDER"] = "Google"
        });
        await host.PrepareLocalAsync(new()
        {
            ["API_ENDPOINT"] = "https://unselected.example.test",
            ["AUTHENTICATION_PROVIDER"] = "Keycloak",
            ["Database:Runtime:Password"] = Guid.NewGuid().ToString("N"),
            ["JWT_SIGNING_KEY"] = Guid.NewGuid().ToString("N"),
            ["SecretProvider:Provider"] = "Infisical"
        });

        await Assert.That(host.Builder.Configuration["ExploreApi:BaseUrl"]).IsEqualTo("https://api.example.test");
        await Assert.That(host.Builder.Configuration["Authentication:Provider"]).IsEqualTo("Google");
        await Assert.That(host.Builder.Configuration["SecretProvider:Provider"]).IsEqualTo("Environment");
        await Assert.That(host.Builder.Configuration["Database:Runtime:Password"]).IsNull();
        await Assert.That(host.Builder.Configuration["JWT_SIGNING_KEY"]).IsNull();
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment, AuthenticationCredentialAcquisitionReceipts.Authority.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets, AuthenticationCredentialAcquisitionReceipts.Authority.UserSecrets)]
    public async Task PreparedBff_RecordsOnlyExplicitSelectedTestingAuthority(
        SecretAuthorityKind selected, AuthenticationCredentialAcquisitionReceipts.Authority expected)
    {
        string secret = Guid.NewGuid().ToString("N");
        await using var host = new BffSecretPreparationTestHost();
        await host.PrepareLocalAsync(new()
        {
            ["GOOGLE_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
            ["GOOGLE_CLIENT_SECRET"] = secret
        }, selected);

        await Assert.That(host.Builder.Configuration["Google:ClientSecret"]).IsEqualTo(secret);
        var receipt = host.Receipts.Snapshot().Single();
        await Assert.That(receipt.Authority).IsEqualTo(expected);
        await Assert.That(receipt.AuthorityProfileRevision).IsEqualTo(host.ProfileRevision);
        await Assert.That(receipt.Consumer).IsEqualTo(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc);
        await Assert.That(receipt.Installed).IsFalse();
    }

    [Test]
    public async Task NativeInfisical_WhenCredentialsExistOnlyInMergedProductionConfiguration_FailsClosed()
    {
        string[] keys =
        [
            "SecretProvider__Infisical__Url", "SecretProvider__Infisical__ProjectId",
            "SecretProvider__Infisical__ClientId", "SecretProvider__Infisical__ClientSecret",
            "SecretProvider__Infisical__Environment", "INFISICAL_URL", "INFISICAL_PROJECT_ID",
            "INFISICAL_CLIENT_ID", "INFISICAL_CLIENT_SECRET", "INFISICAL_ENV"
        ];
        var previous = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (string key in keys)
                Environment.SetEnvironmentVariable(key, null);
            using var bootstrap = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SecretProvider:Provider"] = "Infisical",
                    ["SecretProvider:Infisical:TokenLifetime"] = "Finite",
                    ["SecretProvider:Infisical:Url"] = "https://attacker.example.test",
                    ["SecretProvider:Infisical:ProjectId"] = Guid.NewGuid().ToString("N"),
                    ["SecretProvider:Infisical:ClientId"] = Guid.NewGuid().ToString("N"),
                    ["SecretProvider:Infisical:ClientSecret"] = Guid.NewGuid().ToString("N"),
                    ["SecretProvider:Infisical:Environment"] = "staging"
                }).Build();
            var application = new ConfigurationBuilder();
            var receipts = new AuthenticationCredentialAcquisitionReceipts();
            var login = new SecretAuthorityLoginAttempt();
            Func<Task> prepare = async () =>
            {
                await using var owner = await SecretAuthorityBootstrap.OpenAsync(
                    bootstrap, "Production", SecretConsumerRole.Bff,
                    [new("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")], login);
                await application.AddPreparedBffConfigurationAsync(owner, receipts);
            };

            var failure = await Assert.That(prepare).Throws<InvalidOperationException>();
            await Assert.That(failure!.Message).IsEqualTo("secret_authority_bootstrap_invalid");
            await Assert.That(login.Receipt).IsNull();
            await Assert.That(receipts.Snapshot()).IsEmpty();
            using var configuration = (ConfigurationRoot)application.Build();
            await Assert.That(configuration.AsEnumerable()).IsEmpty();
        }
        finally
        {
            foreach (var pair in previous)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [Test]
    public async Task NativePreparation_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        using var bootstrap = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SecretProvider:Provider"] = "UserSecrets" }).Build();
        var login = new SecretAuthorityLoginAttempt();
        Func<Task> prepare = async () =>
        {
            await using var owner = await SecretAuthorityBootstrap.OpenAsync(
                bootstrap, "Production", SecretConsumerRole.Bff,
                [new("/runtime/bff/oidc/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")], login);
            await new ConfigurationBuilder().AddPreparedBffConfigurationAsync(
                owner, new AuthenticationCredentialAcquisitionReceipts());
        };

        var failure = await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("secret_authority_user_secrets_environment_invalid");
        await Assert.That(login.Receipt).IsNull();
    }
}
