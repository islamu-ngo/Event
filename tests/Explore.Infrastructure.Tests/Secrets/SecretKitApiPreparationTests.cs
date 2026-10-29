using Explore.API.Extensions;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitApiPreparationTests
{
    [Test]
    [Arguments("/smtp", 401)]
    [Arguments("/smtp", 403)]
    [Arguments("/smtp", 429)]
    [Arguments("/smtp", 500)]
    [Arguments("/analytics", 401)]
    [Arguments("/analytics", 403)]
    [Arguments("/analytics", 429)]
    [Arguments("/analytics", 500)]
    public async Task OptionalProviderInventoryFailureCannotPublishApiConfiguration(string folder, int statusCode)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.InventoryStatusCodes[folder] = statusCode;
        authority.InventoryKeysByFolder = new() { ["/database"] = ["DATABASE_PROVIDER"] };
        authority.Values["DATABASE_PROVIDER"] = "PostgreSql";
        await using var owner = await NativeOwner(authority);
        var output = new ConfigurationBuilder();

        Func<Task> prepare = async () => await output.AddPreparedApiConfigurationAsync(owner);

        await Assert.That(prepare).Throws<InvalidOperationException>()
            .WithMessage("secret_api_configuration_unavailable");
        await Assert.That(output.Sources.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("/smtp", "MAIL_SMTP_HOST", false)]
    [Arguments("/smtp", "MAIL_SMTP_HOST", true)]
    [Arguments("/analytics", "ANALYTICS_ENDPOINT", false)]
    [Arguments("/analytics", "ANALYTICS_ENDPOINT", true)]
    public async Task InvalidOrDisappearingOptionalMaterialCannotPublishApiConfiguration(
        string folder, string key, bool missing)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.InventoryKeysByFolder = new()
        {
            ["/database"] = ["DATABASE_PROVIDER"],
            [folder] = [key]
        };
        authority.Values["DATABASE_PROVIDER"] = "PostgreSql";
        if (!missing)
            authority.Values[key] = "${unresolved}";
        await using var owner = await NativeOwner(authority);
        var output = new ConfigurationBuilder();

        Func<Task> prepare = async () => await output.AddPreparedApiConfigurationAsync(owner);

        await Assert.That(prepare).Throws<InvalidOperationException>()
            .WithMessage("secret_api_configuration_unavailable");
        await Assert.That(output.Sources.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GenuinelyAbsentOptionalGroupsAllowPreparedApiConfiguration()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.InventoryKeysByFolder = new() { ["/database"] = ["DATABASE_PROVIDER"] };
        authority.Values["DATABASE_PROVIDER"] = "PostgreSql";
        await using var owner = await NativeOwner(authority);
        var output = new ConfigurationBuilder();

        await output.AddPreparedApiConfigurationAsync(owner);

        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["Database:Provider"]).IsEqualTo("PostgreSql");
        await Assert.That(published["Smtp:Host"]).IsNull();
        await Assert.That(published["Analytics:Endpoint"]).IsNull();
    }

    [Test]
    public async Task ExplicitEmptyStructuredCredentialCannotActivateItsFlatAlias()
    {
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Database:Runtime:Password"] = string.Empty,
                ["DATABASE_RUNTIME_PASSWORD"] = Guid.NewGuid().ToString("N")
            }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var output = new ConfigurationBuilder();
        await output.AddPreparedApiConfigurationAsync(owner);
        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["Database:Runtime:Password"]).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task PreparedApiMappingCannotReviveUnselectedDatabaseOrSigningCredentials()
    {
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DATABASE_PROVIDER"] = "Sqlite",
                ["DATABASE_NAME"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db"),
                ["AUTHENTICATION_PROVIDER"] = "Local"
            }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var output = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Runtime:Password"] = Guid.NewGuid().ToString("N"),
            ["Authentication:Local:JwtKey"] = Guid.NewGuid().ToString("N")
        });
        await output.AddPreparedApiConfigurationAsync(owner);
        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["Database:Provider"]).IsEqualTo("Sqlite");
        await Assert.That(published["Database:Runtime:Password"]).IsNull();
        await Assert.That(published["Authentication:Local:JwtKey"]).IsNull();
    }

    [Test]
    public async Task PreparedApiMappingReplacesEveryPreviousPermittedClientChild()
    {
        string admittedClient = Guid.NewGuid().ToString("N");
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Keycloak:PermittedClients:0"] = admittedClient }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var output = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:PermittedClients:0"] = Guid.NewGuid().ToString("N"),
            ["Keycloak:PermittedClients:1"] = Guid.NewGuid().ToString("N")
        });
        await output.AddPreparedApiConfigurationAsync(owner);
        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published["Keycloak:PermittedClients:0"]).IsEqualTo(admittedClient);
        await Assert.That(published["Keycloak:PermittedClients:1"]).IsNull();
    }

    [Test]
    public async Task LocalIntegrationBootstrapMetadataSurvivesSelectedAuthorityProjection()
    {
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["LISTMONK_ENABLED"] = "true",
                ["LISTMONK_INSTANCE_URL"] = "https://newsletter.operator.example",
                ["LISTMONK_DEFAULT_LIST_ID"] = "7"
            }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var output = new ConfigurationBuilder();
        await output.AddPreparedApiConfigurationAsync(owner);
        using var published = (ConfigurationRoot)output.Build();
        await Assert.That(published[Explore.Domain.Constants.GovernanceSettingKeys.Integrations.Listmonk.Enabled])
            .IsEqualTo("true");
        await Assert.That(published[Explore.Domain.Constants.GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl])
            .IsEqualTo("https://newsletter.operator.example");
        await Assert.That(published[Explore.Domain.Constants.GovernanceSettingKeys.Integrations.Listmonk.DefaultListId])
            .IsEqualTo("7");
    }

    [Test]
    public async Task CanceledApiPreparationPublishesNoPartialMapping()
    {
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["AUTHENTICATION_PROVIDER"] = "Local" }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var output = new ConfigurationBuilder();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Func<Task> prepare = async () => await output.AddPreparedApiConfigurationAsync(owner, canceled.Token);
        await Assert.That(prepare).Throws<OperationCanceledException>();
        await Assert.That(output.Sources.Count).IsEqualTo(0);
    }

    private static async Task<SecretAuthorityAcquisition> NativeOwner(SecretKitHttpAuthority authority)
    {
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            [new("/database", "DATABASE_PROVIDER", "Database:Provider")],
            new(authority.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)),
            bootstrap, new());
        if (!opened.IsSuccess)
            throw new InvalidOperationException("test_authority_unavailable");
        return opened.Value;
    }
}
