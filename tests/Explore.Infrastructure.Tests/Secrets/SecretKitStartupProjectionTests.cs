using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using SecretKit.Core;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitStartupProjectionTests
{
    [Test]
    public async Task LocalRuntimeValuesRemainBoundToTheirAdmittedOwnerGeneration()
    {
        string first = Guid.NewGuid().ToString("N");
        string replacement = Guid.NewGuid().ToString("N");
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DATABASE_PASSWORD"] = first }).Build();
        await using var original = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        selected["DATABASE_PASSWORD"] = replacement;
        var acquired = await original.PrepareGroupAsync(
            [new("/database", "DATABASE_PASSWORD", "Database:Password")]);
        await Assert.That(acquired.IsSuccess).IsTrue();
        using var originalLease = acquired.Value;
        await Assert.That(originalLease.Configuration["Database:Password"]).IsEqualTo(first);
        await using var next = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var replaced = await next.PrepareGroupAsync(
            [new("/database", "DATABASE_PASSWORD", "Database:Password")]);
        await Assert.That(replaced.IsSuccess).IsTrue();
        using var nextLease = replaced.Value;
        await Assert.That(nextLease.Configuration["Database:Password"]).IsEqualTo(replacement);
        await Assert.That(original.GenerationId == next.GenerationId).IsFalse();
    }

    [Test]
    public async Task NativeInventoryDiscoversNamesThenReadsEachSourceOnceForAllAliases()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["DATABASE_PROVIDER"] = "PostgreSql";
        authority.Values["DATABASE_NAME"] = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        var result = await owner.PrepareConfigurationGroupAsync(["/database"]);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value["Database:Provider"]).IsEqualTo("PostgreSql");
        await Assert.That(result.Value["Database:Name"]).IsEqualTo(authority.Values["DATABASE_NAME"]);
        await Assert.That(result.Value["Database:Database"]).IsEqualTo(authority.Values["DATABASE_NAME"]);
        await Assert.That(result.Value["DATABASE_NAME"]).IsEqualTo(authority.Values["DATABASE_NAME"]);
        await Assert.That(authority.Inventories).IsEqualTo(1);
        await Assert.That(authority.Reads).IsEqualTo(2);
        await Assert.That(authority.Logins).IsEqualTo(1);
    }

    [Test]
    public async Task ListedThenMissingMemberCannotBecomeSuccessfulPartialProjection()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["DATABASE_PROVIDER"] = "PostgreSql";
        authority.AdvertisedKeys = ["DATABASE_PROVIDER", "DATABASE_NAME"];
        await using var owner = await NativeOwner(authority);
        var result = await owner.PrepareConfigurationGroupAsync(["/database"]);
        await Assert.That(result.Status).IsEqualTo(SecretStatus.Unknown);
    }

    [Test]
    public async Task ForeignInventoryCannotDispatchAnyMaterialRead()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["DATABASE_PROVIDER"] = "PostgreSql";
        authority.ReturnedProject = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        var result = await owner.PrepareConfigurationGroupAsync(["/database"]);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(authority.Reads).IsEqualTo(0);
    }

    [Test]
    public async Task LocalProjectionKeepsCollectionsButCannotExportAuthorityOrSiblingCredentials()
    {
        string databaseValue = Guid.NewGuid().ToString("N");
        string audience = Guid.NewGuid().ToString("N");
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Database:Runtime:Password"] = databaseValue,
                ["Keycloak:PermittedClients:0"] = audience,
                ["AUTHENTICATION_PROVIDER"] = "Keycloak",
                ["INFISICAL_CLIENT_SECRET"] = Guid.NewGuid().ToString("N"),
                ["SecretProvider__Infisical__ClientSecret"] = Guid.NewGuid().ToString("N"),
                ["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = Guid.NewGuid().ToString("N"),
                ["EVENT_DP_API_RING"] = Guid.NewGuid().ToString("N"),
                ["Unrelated:Value"] = Guid.NewGuid().ToString("N")
            }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var result = await owner.PrepareConfigurationGroupAsync(SecretAcquisitionPolicy.Roots(SecretConsumerRole.Api));
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value["Database:Runtime:Password"]).IsEqualTo(databaseValue);
        await Assert.That(result.Value["Keycloak:PermittedClients:0"]).IsEqualTo(audience);
        await Assert.That(result.Value["AUTHENTICATION_PROVIDER"]).IsEqualTo("Keycloak");
        await Assert.That(result.Value.ContainsKey("INFISICAL_CLIENT_SECRET")).IsFalse();
        await Assert.That(result.Value.ContainsKey("SecretProvider__Infisical__ClientSecret")).IsFalse();
        await Assert.That(result.Value.ContainsKey("KEYCLOAK_BLAZOR_CLIENT_SECRET")).IsFalse();
        await Assert.That(result.Value.ContainsKey("EVENT_DP_API_RING")).IsFalse();
        await Assert.That(result.Value.ContainsKey("Unrelated:Value")).IsFalse();
    }

    [Test]
    public async Task OrdinaryEmptyFieldsDoNotBecomeCredentialFailureOrDisappear()
    {
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["MAIL_SMTP_FROM_NAME"] = string.Empty }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var result = await owner.PrepareConfigurationGroupAsync(["/smtp"]);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value["MAIL_SMTP_FROM_NAME"]).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task AbsentGroupDoesNotInventAnEmptyGeneration()
    {
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), selected);
        var result = await owner.PrepareConfigurationGroupAsync(["/smtp"]);
        await Assert.That(result.Status).IsEqualTo(SecretStatus.Absent);
    }

    [Test]
    public async Task StartupProjectionRejectsForeignConsumerRoots()
    {
        using var selected = (ConfigurationRoot)new ConfigurationBuilder().Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Bff), selected);
        Func<Task> prepare = async () => { _ = await owner.PrepareConfigurationGroupAsync(["/database"]); };
        await Assert.That(prepare).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("GOOGLE_CLIENT_SECRET")]
    [Arguments("google_client_secret")]
    [Arguments("ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS")]
    [Arguments("atproto_oauth_client_private_jwks")]
    public async Task RuntimeCredentialCaseVariantsCannotBeImportedFromAnOrdinaryFolder(string key)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[key] = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        await Assert.That(async () => await owner.PrepareConfigurationGroupAsync(["/smtp"]))
            .Throws<InvalidOperationException>();
        await Assert.That(authority.Reads).IsEqualTo(0);
    }

    [Test]
    [Arguments("/database", "DATABASE_GLOBAL_USERNAME", "Database:Global:Username")]
    [Arguments("/database", "DATABASE_GLOBAL_PASSWORD", "Database:Global:Password")]
    [Arguments("/database", "GLOBAL_USERNAME", "Database:Global:Username")]
    [Arguments("/database", "GLOBAL_PASSWORD", "Database:Global:Password")]
    [Arguments("/ai", "AI_TOOL_PROPOSALS_ENABLED", "AiProvider:ToolProposalsEnabled")]
    [Arguments("/database", "ERASURE_DATABASE_TOPOLOGY", "PrivacyErasure:Authority:Topology")]
    [Arguments("/database/erasure", "ERASURE_DATABASE_TOPOLOGY", "PrivacyErasure:Authority:Topology")]
    [Arguments("/database", "IDENTITY_DATABASE_TOPOLOGY", "IdentityDatabase:Topology")]
    [Arguments("/database/identity", "IDENTITY_DATABASE_TOPOLOGY", "IdentityDatabase:Topology")]
    [Arguments("/database/identity", "IDENTITY_DATABASE_HOST", "IdentityDatabase:Host")]
    [Arguments("/database/identity", "IDENTITY_DATABASE_RUNTIME_PASSWORD", "IdentityDatabase:Runtime:Password")]
    [Arguments("/api/operator-identity", "PUBLIC_NAME", "Instance:OperatorIdentity:PublicName")]
    [Arguments("/api/operatoridentity", "LEGAL_NAME", "Instance:OperatorIdentity:LegalName")]
    [Arguments("/api/operator-identity", "JURISDICTION_COUNTRY_CODE", "Instance:OperatorIdentity:JurisdictionCountryCode")]
    [Arguments("/api/operator-identity", "PUBLIC_CONTACT_EMAIL", "Instance:OperatorIdentity:PublicContactEmail")]
    [Arguments("/api/operator-identity", "TERMS_URL", "Instance:OperatorIdentity:TermsUrl")]
    [Arguments("/api", "INSTANCE__OPERATORIDENTITY__PUBLICNAME", "Instance:OperatorIdentity:PublicName")]
    [Arguments("/api/bootstrap", "MODE", "Instance:Bootstrap:Mode")]
    [Arguments("/api/bootstrap", "ADMIN_PROVIDER", "Instance:Bootstrap:AdminProvider")]
    [Arguments("/api/instance-bootstrap", "ADMIN_FIRST_NAME", "Instance:Bootstrap:AdminFirstName")]
    [Arguments("/api/instancebootstrap", "ADMIN_LAST_NAME", "Instance:Bootstrap:AdminLastName")]
    [Arguments("/api/bootstrap", "LOCAL_PASSWORD", "Instance:Bootstrap:LocalPassword")]
    [Arguments("/api", "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD", "Instance:Bootstrap:LocalPassword")]
    [Arguments("/api/controlplane", "ENABLED", "ManagedControlPlane:Enabled")]
    [Arguments("/api/control-plane", "MANAGED_MODE", "ManagedControlPlane:Enabled")]
    [Arguments("/api/controlplane", "URL", "ManagedControlPlane:ControlPlaneUrl")]
    [Arguments("/api/control-plane", "INSTANCE_ID", "ManagedControlPlane:ManagedInstanceId")]
    [Arguments("/api/controlplane", "REGISTRATION_TOKEN", "ManagedControlPlane:RegistrationToken")]
    [Arguments("/api/control-plane", "REGISTRATION_CREDENTIALS", "ManagedControlPlane:RegistrationCredentials")]
    [Arguments("/api/controlplane", "MAXIMUM_TENANT_COUNT", "ManagedControlPlane:MaximumTenantCount")]
    [Arguments("/api/control-plane", "TENANT_ADMINISTRATOR_SIGN_IN_URL", "ManagedControlPlane:TenantAdministratorSignInUrl")]
    [Arguments("/api/controlplane", "CONTROL_PLANE_URL", "ManagedControlPlane:ControlPlaneUrl")]
    [Arguments("/api", "CONTROL_PLANE_MANAGED_MODE", "ManagedControlPlane:Enabled")]
    [Arguments("/api/webpush", "WEB_PUSH_ENABLED", "WebPush:Enabled")]
    [Arguments("/api/web-push", "VAPID_PUBLIC_KEY", "WebPush:VapidPublicKey")]
    [Arguments("/api/webpush", "VAPID_PRIVATE_KEY", "WebPush:VapidPrivateKey")]
    [Arguments("/api/web-push", "VAPID_SUBJECT", "WebPush:VapidSubject")]
    [Arguments("/api", "VAPID_PUBLIC_KEY", "WebPush:VapidPublicKey")]
    [Arguments("/api/ratelimiting", "ANONYMOUSREGISTRATION_IPPERMITLIMIT", "RateLimiting:AnonymousRegistration:IpPermitLimit")]
    [Arguments("/api/rate-limiting", "ANONYMOUSREGISTRATION_WINDOWSECONDS", "RateLimiting:AnonymousRegistration:WindowSeconds")]
    [Arguments("/api", "RATELIMITING__ANONYMOUSREGISTRATION__IPPERMITLIMIT", "RateLimiting:AnonymousRegistration:IpPermitLimit")]
    [Arguments("/api", "MODERATION__RECONCILIATION__APIKEY", "Moderation:Reconciliation:ApiKey")]
    public async Task NativeStartupProjectionPreservesOwnedTopologyMappings(string folder, string key, string destination)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string value = Guid.NewGuid().ToString("N");
        authority.Values[key] = value;
        await using var owner = await NativeOwner(authority);
        var result = await owner.PrepareConfigurationGroupAsync([folder]);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value[destination]).IsEqualTo(value);
        await Assert.That(authority.Reads).IsEqualTo(1);
    }

    [Test]
    [Arguments("/api/webpush", "ENABLED")]
    [Arguments("/api/web-push", "PUBLIC_KEY")]
    [Arguments("/api/webpush", "PRIVATE_KEY")]
    [Arguments("/api/web-push", "SUBJECT")]
    [Arguments("/api/ratelimiting", "ANONYMOUSREGISTRATION__IPPERMITLIMIT")]
    [Arguments("/api/ratelimiting", "RATELIMITING__ANONYMOUSREGISTRATION_IPPERMITLIMIT")]
    [Arguments("/api/rate-limiting", "RATE_LIMITING__ANONYMOUSREGISTRATION_IPPERMITLIMIT")]
    public async Task UnsupportedModularAliasesCannotCreateStartupOptions(string folder, string key)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values[key] = Guid.NewGuid().ToString("N");
        await using var owner = await NativeOwner(authority);
        var result = await owner.PrepareConfigurationGroupAsync([folder]);
        await Assert.That(result.Status).IsEqualTo(SecretStatus.Absent);
        await Assert.That(authority.Reads).IsEqualTo(0);
    }

    [Test]
    [Arguments("MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM", "Moderation:MeasurementSigningPrivateKeyPem")]
    [Arguments("MODERATION_MEASUREMENT_TEST_DATABASE__USERNAME", "Moderation:MeasurementTestDatabase:USERNAME")]
    [Arguments("MODERATION_MEASUREMENT_TEST_DATABASE__PASSWORD", "Moderation:MeasurementTestDatabase:PASSWORD")]
    public async Task MeasurementProjectionUsesItsOwnAdmittedRoot(string key, string destination)
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string value = Guid.NewGuid().ToString("N");
        authority.Values[key] = value;
        await using var owner = await NativeOwner(authority, SecretConsumerRole.ModerationMeasurement);
        var result = await owner.PrepareConfigurationGroupAsync(["/moderation"]);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value[destination]).IsEqualTo(value);
        await Assert.That(authority.Reads).IsEqualTo(1);
    }

    private static async Task<SecretAuthorityAcquisition> NativeOwner(
        SecretKitHttpAuthority authority, SecretConsumerRole consumer = SecretConsumerRole.Api)
    {
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", consumer),
            [new("/database", "DATABASE_PROVIDER", "Database:Provider")],
            new(authority.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
                SecretKit.Infisical.InfisicalTokenLifetime.Finite, null,
                TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)), bootstrap, new());
        if (!opened.IsSuccess)
            throw new InvalidOperationException("test_authority_unavailable");
        return opened.Value;
    }
}
