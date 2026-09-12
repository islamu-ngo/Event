using System.Reflection;
using Explore.Secrets.Configuration;
using Explore.Secrets.Extensions;
using Microsoft.Extensions.Configuration;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Configuration;

[NotInParallel]
public sealed class InfisicalConfigurationProviderTests
{
    private static readonly string[] BootstrapKeys =
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

    [Test]
    public async Task AddInfisical_WhenProcessEnvironmentCredentialsAreConfigured_AddsConfiguredSource()
    {
        string clientSecret = SecretsTestValues.CreateSecret();
        var previous = CaptureBootstrapEnvironment();
        ClearBootstrapEnvironment();
        Environment.SetEnvironmentVariable("SecretProvider__Infisical__Url", "https://secrets.example.test");
        Environment.SetEnvironmentVariable("SecretProvider__Infisical__ProjectId", "project-id");
        Environment.SetEnvironmentVariable("SecretProvider__Infisical__ClientId", "client-id");
        Environment.SetEnvironmentVariable("SecretProvider__Infisical__ClientSecret", clientSecret);
        Environment.SetEnvironmentVariable("SecretProvider__Infisical__Environment", "staging");
        var bootstrapConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Infisical:Paths:0"] = "/api",
                ["SecretProvider:Infisical:Paths:1"] = "/keycloak",
            })
            .Build();
        var builder = new ConfigurationBuilder();

        try
        {
            builder.AddInfisical(bootstrapConfiguration, source =>
            {
                source.Url = "https://attacker.example.test";
                source.ClientSecret = SecretsTestValues.CreateSecret();
            });

            await Assert.That(builder.Sources).Count().IsEqualTo(1);
            var source = builder.Sources.Single();
            await Assert.That(source).IsTypeOf<InfisicalConfigurationSource>();
            var infisicalSource = (InfisicalConfigurationSource)source;
            await Assert.That(infisicalSource.Url).IsEqualTo("https://secrets.example.test");
            await Assert.That(infisicalSource.ProjectId).IsEqualTo("project-id");
            await Assert.That(infisicalSource.ClientId).IsEqualTo("client-id");
            await Assert.That(infisicalSource.ClientSecret).IsEqualTo(clientSecret);
            await Assert.That(infisicalSource.Environment).IsEqualTo("staging");
            await Assert.That(infisicalSource.Paths.SequenceEqual(["/api", "/keycloak"])).IsTrue();
        }
        finally
        {
            RestoreBootstrapEnvironment(previous);
        }
    }

    [Test]
    public async Task AddInfisical_WhenCredentialsExistOnlyInMergedConfiguration_FailsClosed()
    {
        var previous = CaptureBootstrapEnvironment();
        ClearBootstrapEnvironment();
        var bootstrapConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Infisical:Url"] = "https://attacker.example.test",
                ["SecretProvider:Infisical:ProjectId"] = "project-id",
                ["SecretProvider:Infisical:ClientId"] = "client-id",
                ["SecretProvider:Infisical:ClientSecret"] = SecretsTestValues.CreateSecret(),
                ["SecretProvider:Infisical:Environment"] = "staging",
            })
            .Build();
        var builder = new ConfigurationBuilder();

        try
        {
            Action act = () => builder.AddInfisical(bootstrapConfiguration);

            await Assert.That(act).Throws<InvalidOperationException>();
        }
        finally
        {
            RestoreBootstrapEnvironment(previous);
        }
    }

    [Test]
    public async Task ConvertToConfigurationKey_WhenAiToolProposalsSecretProvided_MapsToAiProviderSetting()
    {
        var key = await ConvertToConfigurationKey("AI_TOOL_PROPOSALS_ENABLED", "/");

        await Assert.That(key).IsEqualTo("AiProvider:ToolProposalsEnabled");
    }

    [Test]
    public async Task ConvertToConfigurationKey_WhenDatabaseErasureTopologySecretsProvided_MapsToPrivacyErasureTopology()
    {
        var canonical = await ConvertToConfigurationKey("ERASURE_DATABASE_TOPOLOGY", "/database");
        var subfolder = await ConvertToConfigurationKey("ERASURE_DATABASE_TOPOLOGY", "/database/erasure");

        await Assert.That(canonical).IsEqualTo("PrivacyErasure:Authority:Topology");
        await Assert.That(subfolder).IsEqualTo("PrivacyErasure:Authority:Topology");
    }

    [Test]
    public async Task ConvertToConfigurationKey_WhenIdentityTopologySecretsProvided_MapsToIdentityDatabaseTopology()
    {
        var inDatabaseFolder = await ConvertToConfigurationKey("IDENTITY_DATABASE_TOPOLOGY", "/database");
        var inSubfolder = await ConvertToConfigurationKey("IDENTITY_DATABASE_TOPOLOGY", "/database/identity");
        var host = await ConvertToConfigurationKey("IDENTITY_DATABASE_HOST", "/database/identity");
        var password = await ConvertToConfigurationKey("IDENTITY_DATABASE_RUNTIME_PASSWORD", "/database/identity");

        await Assert.That(inDatabaseFolder).IsEqualTo("IdentityDatabase:Topology");
        await Assert.That(inSubfolder).IsEqualTo("IdentityDatabase:Topology");
        await Assert.That(host).IsEqualTo("IdentityDatabase:Host");
        await Assert.That(password).IsEqualTo("IdentityDatabase:Runtime:Password");
    }

    private static async Task<string> ConvertToConfigurationKey(string secretKey, string path)
    {
        var method = typeof(InfisicalConfigurationProvider).GetMethod(
            "ConvertToConfigurationKey",
            BindingFlags.NonPublic | BindingFlags.Static);

        await Assert.That(method).IsNotNull();
        return ((string?)method!.Invoke(null, [secretKey, path]))!;
    }

    private static Dictionary<string, string?> CaptureBootstrapEnvironment() =>
        BootstrapKeys.ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.Ordinal);

    private static void ClearBootstrapEnvironment()
    {
        foreach (var key in BootstrapKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private static void RestoreBootstrapEnvironment(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var pair in values)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
