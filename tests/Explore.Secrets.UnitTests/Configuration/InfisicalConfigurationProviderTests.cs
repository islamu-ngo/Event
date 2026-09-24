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
    public async Task AddInfisical_WhenCredentialsInBootstrapConfiguration_InDevelopmentEnvironment_Succeeds()
    {
        string clientSecret = SecretsTestValues.CreateSecret();
        var previous = CaptureBootstrapEnvironment();
        ClearBootstrapEnvironment();
        var bootstrapConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Infisical:Url"] = "https://secrets.dev.example.test",
                ["SecretProvider:Infisical:ProjectId"] = "dev-project-id",
                ["SecretProvider:Infisical:ClientId"] = "dev-client-id",
                ["SecretProvider:Infisical:ClientSecret"] = clientSecret,
                ["SecretProvider:Infisical:Environment"] = "development",
            })
            .Build();
        var builder = new ConfigurationBuilder();

        try
        {
            builder.AddInfisical(bootstrapConfiguration, environmentName: "Development");

            await Assert.That(builder.Sources).Count().IsEqualTo(1);
            var source = builder.Sources.Single();
            await Assert.That(source).IsTypeOf<InfisicalConfigurationSource>();
            var infisicalSource = (InfisicalConfigurationSource)source;
            await Assert.That(infisicalSource.Url).IsEqualTo("https://secrets.dev.example.test");
            await Assert.That(infisicalSource.ProjectId).IsEqualTo("dev-project-id");
            await Assert.That(infisicalSource.ClientId).IsEqualTo("dev-client-id");
            await Assert.That(infisicalSource.ClientSecret).IsEqualTo(clientSecret);
            await Assert.That(infisicalSource.Environment).IsEqualTo("development");
        }
        finally
        {
            RestoreBootstrapEnvironment(previous);
        }
    }

    [Test]
    public async Task AddInfisical_WhenCredentialsInBootstrapConfiguration_InProductionEnvironment_FailsClosed()
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
                ["SecretProvider:Infisical:Environment"] = "production",
            })
            .Build();
        var builder = new ConfigurationBuilder();

        try
        {
            Action act = () => builder.AddInfisical(bootstrapConfiguration, environmentName: "Production");

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

    [Test]
    public async Task ConvertToConfigurationKey_WhenOperatorIdentityFolderSecretsProvided_MapsToInstanceOperatorIdentity()
    {
        var publicNameKebab = await ConvertToConfigurationKey("PUBLIC_NAME", "/api/operator-identity");
        var legalNameCompact = await ConvertToConfigurationKey("LEGAL_NAME", "/api/operatoridentity");
        var countryCode = await ConvertToConfigurationKey("JURISDICTION_COUNTRY_CODE", "/api/operator-identity");
        var email = await ConvertToConfigurationKey("PUBLIC_CONTACT_EMAIL", "/api/operator-identity");
        var terms = await ConvertToConfigurationKey("TERMS_URL", "/api/operator-identity");
        var legacyInApi = await ConvertToConfigurationKey("INSTANCE__OPERATORIDENTITY__PUBLICNAME", "/api");

        await Assert.That(publicNameKebab).IsEqualTo("Instance:OperatorIdentity:PublicName");
        await Assert.That(legalNameCompact).IsEqualTo("Instance:OperatorIdentity:LegalName");
        await Assert.That(countryCode).IsEqualTo("Instance:OperatorIdentity:JurisdictionCountryCode");
        await Assert.That(email).IsEqualTo("Instance:OperatorIdentity:PublicContactEmail");
        await Assert.That(terms).IsEqualTo("Instance:OperatorIdentity:TermsUrl");
        await Assert.That(legacyInApi).IsEqualTo("Instance:OperatorIdentity:PublicName");
    }

    [Test]
    public async Task ConvertToConfigurationKey_WhenBootstrapFolderSecretsProvided_MapsToInstanceBootstrap()
    {
        var mode = await ConvertToConfigurationKey("MODE", "/api/bootstrap");
        var adminProvider = await ConvertToConfigurationKey("ADMIN_PROVIDER", "/api/bootstrap");
        var firstName = await ConvertToConfigurationKey("ADMIN_FIRST_NAME", "/api/instance-bootstrap");
        var lastName = await ConvertToConfigurationKey("ADMIN_LAST_NAME", "/api/instancebootstrap");
        var password = await ConvertToConfigurationKey("LOCAL_PASSWORD", "/api/bootstrap");
        var legacyInApi = await ConvertToConfigurationKey("INSTANCE_BOOTSTRAP_LOCAL_PASSWORD", "/api");

        await Assert.That(mode).IsEqualTo("Instance:Bootstrap:Mode");
        await Assert.That(adminProvider).IsEqualTo("Instance:Bootstrap:AdminProvider");
        await Assert.That(firstName).IsEqualTo("Instance:Bootstrap:AdminFirstName");
        await Assert.That(lastName).IsEqualTo("Instance:Bootstrap:AdminLastName");
        await Assert.That(password).IsEqualTo("Instance:Bootstrap:LocalPassword");
        await Assert.That(legacyInApi).IsEqualTo("Instance:Bootstrap:LocalPassword");
    }

    [Test]
    public async Task ConvertToConfigurationKey_WhenControlPlaneFolderSecretsProvided_MapsToManagedControlPlane()
    {
        var enabled = await ConvertToConfigurationKey("ENABLED", "/api/controlplane");
        var managedMode = await ConvertToConfigurationKey("MANAGED_MODE", "/api/control-plane");
        var url = await ConvertToConfigurationKey("URL", "/api/controlplane");
        var instanceId = await ConvertToConfigurationKey("INSTANCE_ID", "/api/control-plane");
        var token = await ConvertToConfigurationKey("REGISTRATION_TOKEN", "/api/controlplane");
        var creds = await ConvertToConfigurationKey("REGISTRATION_CREDENTIALS", "/api/control-plane");
        var maxTenants = await ConvertToConfigurationKey("MAXIMUM_TENANT_COUNT", "/api/controlplane");
        var signInUrl = await ConvertToConfigurationKey("TENANT_ADMINISTRATOR_SIGN_IN_URL", "/api/control-plane");
        var prefixed = await ConvertToConfigurationKey("CONTROL_PLANE_URL", "/api/controlplane");
        var legacyInApi = await ConvertToConfigurationKey("CONTROL_PLANE_MANAGED_MODE", "/api");

        await Assert.That(enabled).IsEqualTo("ManagedControlPlane:Enabled");
        await Assert.That(managedMode).IsEqualTo("ManagedControlPlane:Enabled");
        await Assert.That(url).IsEqualTo("ManagedControlPlane:ControlPlaneUrl");
        await Assert.That(instanceId).IsEqualTo("ManagedControlPlane:ManagedInstanceId");
        await Assert.That(token).IsEqualTo("ManagedControlPlane:RegistrationToken");
        await Assert.That(creds).IsEqualTo("ManagedControlPlane:RegistrationCredentials");
        await Assert.That(maxTenants).IsEqualTo("ManagedControlPlane:MaximumTenantCount");
        await Assert.That(signInUrl).IsEqualTo("ManagedControlPlane:TenantAdministratorSignInUrl");
        await Assert.That(prefixed).IsEqualTo("ManagedControlPlane:ControlPlaneUrl");
        await Assert.That(legacyInApi).IsEqualTo("ManagedControlPlane:Enabled");
    }

    [Test]
    public async Task ConvertToConfigurationKey_WhenWebPushFolderLegacySecretsProvided_MapsToWebPush()
    {
        var enabled = await ConvertToConfigurationKey("WEB_PUSH_ENABLED", "/api/webpush");
        var pub = await ConvertToConfigurationKey("VAPID_PUBLIC_KEY", "/api/web-push");
        var priv = await ConvertToConfigurationKey("VAPID_PRIVATE_KEY", "/api/webpush");
        var subject = await ConvertToConfigurationKey("VAPID_SUBJECT", "/api/web-push");
        var legacyInApi = await ConvertToConfigurationKey("VAPID_PUBLIC_KEY", "/api");
        var unsupportedEnabled = await ConvertToConfigurationKey("ENABLED", "/api/webpush");
        var unsupportedPublicKey = await ConvertToConfigurationKey("PUBLIC_KEY", "/api/web-push");
        var unsupportedPrivateKey = await ConvertToConfigurationKey("PRIVATE_KEY", "/api/webpush");
        var unsupportedSubject = await ConvertToConfigurationKey("SUBJECT", "/api/web-push");

        await Assert.That(enabled).IsEqualTo("WebPush:Enabled");
        await Assert.That(pub).IsEqualTo("WebPush:VapidPublicKey");
        await Assert.That(priv).IsEqualTo("WebPush:VapidPrivateKey");
        await Assert.That(subject).IsEqualTo("WebPush:VapidSubject");
        await Assert.That(legacyInApi).IsEqualTo("WebPush:VapidPublicKey");
        await Assert.That(unsupportedEnabled).IsNull();
        await Assert.That(unsupportedPublicKey).IsNull();
        await Assert.That(unsupportedPrivateKey).IsNull();
        await Assert.That(unsupportedSubject).IsNull();
    }

    [Test]
    public async Task ConvertToConfigurationKey_WhenRateLimitingFolderSecretsProvided_MapsToRateLimiting()
    {
        var ipLimit = await ConvertToConfigurationKey("ANONYMOUSREGISTRATION_IPPERMITLIMIT", "/api/ratelimiting");
        var window = await ConvertToConfigurationKey("ANONYMOUSREGISTRATION_WINDOWSECONDS", "/api/rate-limiting");
        var legacyInApi = await ConvertToConfigurationKey("RATELIMITING__ANONYMOUSREGISTRATION__IPPERMITLIMIT", "/api");
        var unsupportedDoubleSeparator = await ConvertToConfigurationKey("ANONYMOUSREGISTRATION__IPPERMITLIMIT", "/api/ratelimiting");
        var unsupportedRateLimitingPrefix = await ConvertToConfigurationKey("RATELIMITING__ANONYMOUSREGISTRATION_IPPERMITLIMIT", "/api/ratelimiting");
        var unsupportedRateLimitingAliasPrefix = await ConvertToConfigurationKey("RATE_LIMITING__ANONYMOUSREGISTRATION_IPPERMITLIMIT", "/api/rate-limiting");

        await Assert.That(ipLimit).IsEqualTo("RateLimiting:AnonymousRegistration:IpPermitLimit");
        await Assert.That(window).IsEqualTo("RateLimiting:AnonymousRegistration:WindowSeconds");
        await Assert.That(legacyInApi).IsEqualTo("RateLimiting:AnonymousRegistration:IpPermitLimit");
        await Assert.That(unsupportedDoubleSeparator).IsNull();
        await Assert.That(unsupportedRateLimitingPrefix).IsNull();
        await Assert.That(unsupportedRateLimitingAliasPrefix).IsNull();
    }

    [Test]
    public async Task ConvertToConfigurationKey_RemovedEditionInputsDoNotCreateRuntimeLicensingOptions()
    {
        var enabled = await ConvertToConfigurationKey("USE_COMMERCIAL_LUCKYPENNY", "/licensing");
        var key = await ConvertToConfigurationKey("LUCKYPENNY_LICENSE_KEY", "/api/licensing");
        var cleanKey = await ConvertToConfigurationKey("LICENSE_KEY", "/licensing");

        await Assert.That(enabled).DoesNotStartWith("Licensing:LuckyPenny:");
        await Assert.That(key).DoesNotStartWith("Licensing:LuckyPenny:");
        await Assert.That(cleanKey).DoesNotStartWith("Licensing:LuckyPenny:");
    }

    private static async Task<string?> ConvertToConfigurationKey(string secretKey, string path)
    {
        var method = typeof(InfisicalConfigurationProvider).GetMethod(
            "ConvertToConfigurationKey",
            BindingFlags.NonPublic | BindingFlags.Static);

        await Assert.That(method).IsNotNull();
        return (string?)method!.Invoke(null, [secretKey, path]);
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
