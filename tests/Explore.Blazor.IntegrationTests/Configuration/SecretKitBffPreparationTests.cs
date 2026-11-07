using Explore.Blazor.Configuration;
using Explore.Blazor.Extensions;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Blazor.IntegrationTests.Configuration;

public sealed class SecretKitBffPreparationTests
{
    [Test]
    [Arguments(SecretConsumerRole.Bff)]
    [Arguments(SecretConsumerRole.Combined)]
    public async Task PublicKeycloakMetadataWithoutCredentialLeavesOptionalOidcUnconfigured(
        SecretConsumerRole consumer)
    {
        using var authority = Configuration(new()
        {
            ["KEYCLOAK_ENDPOINT"] = "https://identity.example.test",
            ["KEYCLOAK_REALM"] = Guid.NewGuid().ToString("N"),
            ["KEYCLOAK_CLIENT_ID"] = Guid.NewGuid().ToString("N")
        });
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", consumer), authority);
        var output = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AUTHENTICATION_PROVIDER"] = "Local"
        });
        var receipts = new AuthenticationCredentialAcquisitionReceipts();

        await output.AddPreparedBffConfigurationAsync(owner, receipts);

        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["Authentication:Provider"]).IsEqualTo("Local");
        await Assert.That(published["Keycloak:ClientSecret"]).IsNull();
        await Assert.That(published["Keycloak:Authority"]).IsNull();
        await Assert.That(receipts.Snapshot()).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryEndpointAndProviderIntentSurviveWithoutCredentialFallback(bool aspireReference)
    {
        using var authority = Configuration(new());
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), authority);
        var output = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_ENDPOINT"] = "https://api.example.test",
            ["AUTHENTICATION_PROVIDER"] = "Local",
            ["Google:ClientSecret"] = Guid.NewGuid().ToString("N"),
            ["services:explore-api:https:0"] = aspireReference ? "https://internal.example.test" : null
        });
        await output.AddPreparedBffConfigurationAsync(owner, new());
        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["ExploreApi:BaseUrl"])
            .IsEqualTo(aspireReference ? null : "https://api.example.test");
        await Assert.That(published["Authentication:Provider"]).IsEqualTo("Local");
        await Assert.That(published["Google:ClientSecret"]).IsNull();
    }

    [Test]
    public async Task CancellationDuringLaterGroupCannotPublishEarlierCredentials()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["GOOGLE_CLIENT_ID"] = Guid.NewGuid().ToString("N");
        authority.Values["GOOGLE_CLIENT_SECRET"] = Guid.NewGuid().ToString("N");
        authority.Values["ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS"] = Guid.NewGuid().ToString("N");
        authority.BlockedKey = "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS";
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Bff),
            [new("/runtime/bff/oidc/google", "GOOGLE_CLIENT_ID", "Google:ClientId")],
            new(authority.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)),
            bootstrap, new());
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        using var cancellation = new CancellationTokenSource();
        var output = new ConfigurationBuilder();
        var receipts = new AuthenticationCredentialAcquisitionReceipts();
        Task pending = Task.Run(async () =>
            await output.AddPreparedBffConfigurationAsync(owner, receipts, cancellation.Token));
        try
        {
            await authority.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellation.CancelAsync();
        }
        finally
        {
            authority.ReadReleased.TrySetResult();
        }
        Func<Task> observe = async () => await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(observe).Throws<OperationCanceledException>();
        await Assert.That(output.Sources.Count).IsEqualTo(0);
        await Assert.That(receipts.Snapshot().Any()).IsFalse();
    }

    [Test]
    public async Task IncompleteOptionalGroupCannotExportItsSecretOrReceipt()
    {
        using var authority = Configuration(new()
        {
            ["KEYCLOAK_ENDPOINT"] = "https://identity.example.test",
            ["KEYCLOAK_REALM"] = Guid.NewGuid().ToString("N"),
            ["KEYCLOAK_CLIENT_ID"] = Guid.NewGuid().ToString("N"),
            ["GOOGLE_CLIENT_SECRET"] = Guid.NewGuid().ToString("N")
        });
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), authority);
        var output = new ConfigurationBuilder();
        var receipts = new AuthenticationCredentialAcquisitionReceipts();
        Func<Task> prepare = async () => await output.AddPreparedBffConfigurationAsync(owner, receipts);
        await Assert.That(prepare).Throws<InvalidOperationException>();
        await Assert.That(output.Sources.Count).IsEqualTo(0);
        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["Google:ClientSecret"]).IsNull();
        await Assert.That(receipts.Snapshot().Any()).IsFalse();
    }

    [Test]
    [Arguments(SecretConsumerRole.Bff)]
    [Arguments(SecretConsumerRole.Combined)]
    public async Task CompleteKeycloakGroupPublishesCredentialAndAcquisitionEvidence(SecretConsumerRole consumer)
    {
        string realm = Guid.NewGuid().ToString("N");
        string clientId = Guid.NewGuid().ToString("N");
        string secret = Guid.NewGuid().ToString("N");
        using var authority = Configuration(new()
        {
            ["KEYCLOAK_ENDPOINT"] = "https://identity.example.test",
            ["KEYCLOAK_REALM"] = realm,
            ["KEYCLOAK_CLIENT_ID"] = clientId,
            ["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = secret
        });
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", consumer), authority);
        var output = new ConfigurationBuilder();
        var receipts = new AuthenticationCredentialAcquisitionReceipts();

        await output.AddPreparedBffConfigurationAsync(owner, receipts);

        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["Keycloak:Authority"])
            .IsEqualTo($"https://identity.example.test/realms/{realm}");
        await Assert.That(published["Keycloak:ClientId"]).IsEqualTo(clientId);
        await Assert.That(published["Keycloak:ClientSecret"]).IsEqualTo(secret);
        var receipt = receipts.Snapshot().Single();
        await Assert.That(receipt.Consumer).IsEqualTo(AuthenticationCredentialAcquisitionReceipts.Consumer.KeycloakOidc);
        await Assert.That(receipt.Authority).IsEqualTo(AuthenticationCredentialAcquisitionReceipts.Authority.Environment);
        await Assert.That(published["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsNull();
    }

    [Test]
    public async Task CompleteGroupPublishesOnlyMappedCredentialsWithAuthorityEvidence()
    {
        string clientId = Guid.NewGuid().ToString("N");
        string secret = Guid.NewGuid().ToString("N");
        using var authority = Configuration(new()
        {
            ["GOOGLE_CLIENT_ID"] = clientId,
            ["GOOGLE_CLIENT_SECRET"] = secret,
            ["DATABASE_PASSWORD"] = Guid.NewGuid().ToString("N")
        });
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), authority);
        var output = new ConfigurationBuilder();
        var receipts = new AuthenticationCredentialAcquisitionReceipts();
        await output.AddPreparedBffConfigurationAsync(owner, receipts);
        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["Google:ClientId"]).IsEqualTo(clientId);
        await Assert.That(published["Google:ClientSecret"]).IsEqualTo(secret);
        await Assert.That(published["DATABASE_PASSWORD"]).IsNull();
        await Assert.That(receipts.Snapshot().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task CanceledPreparationCannotPublishMappingsOrReceipts()
    {
        using var authority = Configuration(new() { ["GOOGLE_CLIENT_SECRET"] = Guid.NewGuid().ToString("N") });
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), authority);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var output = new ConfigurationBuilder();
        var receipts = new AuthenticationCredentialAcquisitionReceipts();
        Func<Task> prepare = async () => await output.AddPreparedBffConfigurationAsync(owner, receipts, canceled.Token);
        await Assert.That(prepare).Throws<OperationCanceledException>();
        await Assert.That(output.Sources.Count).IsEqualTo(0);
        await Assert.That(receipts.Snapshot().Any()).IsFalse();
    }

    private static ConfigurationRoot Configuration(Dictionary<string, string?> values) =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();

}
