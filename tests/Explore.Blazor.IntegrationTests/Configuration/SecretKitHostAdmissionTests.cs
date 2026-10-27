using Explore.Blazor.Configuration;
using Explore.Blazor.Extensions;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Blazor.IntegrationTests.Configuration;

[NotInParallel]
public sealed class SecretKitHostAdmissionTests
{
    [Test]
    public async Task ConflictingAuthoritiesCannotReachBffComposition()
    {
        await using var host = new BffSecretPreparationTestHost();
        host.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "Infisical"
        });

        Func<Task> prepare = () => host.PrepareLocalAsync();

        var failure = await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("secret_authority_selection_conflict");
        await Assert.That(host.LoginAttempt.Receipt).IsNull();
        await host.AssertNothingPublished();
    }

    [Test]
    [Arguments("GOOGLE_CLIENT_ID")]
    [Arguments("GOOGLE_CLIENT_SECRET")]
    public async Task PartialGoogleGroupCannotPublishMaterialOrAcquisitionEvidence(string missingKey)
    {
        var values = new Dictionary<string, string?>
        {
            ["GOOGLE_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
            ["GOOGLE_CLIENT_SECRET"] = Guid.NewGuid().ToString("N")
        };
        values.Remove(missingKey);
        await AssertGroupRejected(values);
    }

    [Test]
    [Arguments("KEYCLOAK_ENDPOINT")]
    [Arguments("KEYCLOAK_REALM")]
    [Arguments("KEYCLOAK_CLIENT_ID")]
    [Arguments("KEYCLOAK_BLAZOR_CLIENT_SECRET")]
    public async Task PartialKeycloakGroupCannotPublishMaterialOrAcquisitionEvidence(string missingKey)
    {
        var values = KeycloakValues();
        values.Remove(missingKey);
        await AssertGroupRejected(values);
    }

    [Test]
    [Arguments("KEYCLOAK_BLAZOR_CLIENT_SECRET")]
    [Arguments("GOOGLE_CLIENT_SECRET")]
    public async Task WhitespaceCredentialCannotProduceAcquisitionEvidence(string key)
    {
        var values = KeycloakValues();
        values["GOOGLE_CLIENT_ID"] = Guid.NewGuid().ToString("N");
        values["GOOGLE_CLIENT_SECRET"] = Guid.NewGuid().ToString("N");
        values[key] = " ";
        await AssertGroupRejected(values);
    }

    [Test]
    public async Task LaterGroupFailureCannotPublishEarlierCompleteGroupOrReceipts()
    {
        var values = KeycloakValues();
        values["GOOGLE_CLIENT_SECRET"] = Guid.NewGuid().ToString("N");
        await AssertGroupRejected(values);
    }

    [Test]
    public async Task EntirelyAbsentFrontendGroupsRemainAbsentWithRealDataProtectionPreparation()
    {
        await using var host = new BffSecretPreparationTestHost();
        await host.PrepareLocalAsync();

        await Assert.That(host.Receipts.Snapshot()).IsEmpty();
        await Assert.That(host.Builder.Configuration["Keycloak:ClientSecret"]).IsNull();
        await Assert.That(host.Builder.Configuration["Google:ClientSecret"]).IsNull();
        await Assert.That(host.Builder.Services.Single(descriptor =>
            descriptor.ServiceType == typeof(SecretAuthorityAcquisition)).ImplementationInstance)
            .IsSameReferenceAs(host.Owner);
    }

    private static async Task AssertGroupRejected(Dictionary<string, string?> values)
    {
        using var authority = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), authority);
        var builder = new ConfigurationBuilder();
        var receipts = new AuthenticationCredentialAcquisitionReceipts();

        Func<Task> prepare = async () => await builder.AddPreparedBffConfigurationAsync(owner, receipts);

        var failure = await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("bff_secret_authority_unavailable");
        using var configuration = (ConfigurationRoot)builder.Build();
        await Assert.That(configuration.AsEnumerable().Any(pair => pair.Value is not null)).IsFalse();
        await Assert.That(receipts.Snapshot()).IsEmpty();
    }

    private static Dictionary<string, string?> KeycloakValues() => new()
    {
        ["KEYCLOAK_ENDPOINT"] = "https://identity.example.test",
        ["KEYCLOAK_REALM"] = "operators",
        ["KEYCLOAK_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
        ["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = Guid.NewGuid().ToString("N")
    };
}
