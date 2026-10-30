using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Secrets;

public sealed class SecretBindingAdmissionTests
{
    [Test]
    public async Task TenantCoordinatesRemainAvailableWithinTheirConsumerRoot()
    {
        var binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Smtp.Password, SecretScope.Tenant,
            Guid.CreateVersion7(), "TENANT_MAIL_PASSWORD");
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, ["/smtp"], null)).IsTrue();
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, ["/database"], null)).IsFalse();
    }

    [Test]
    [Arguments("unknown-key")]
    [Arguments("unknown-source")]
    [Arguments("unknown-scope")]
    [Arguments("tenant-without-id")]
    [Arguments("tenant-empty-id")]
    [Arguments("instance-with-id")]
    [Arguments("mixed-source")]
    [Arguments("interpolation")]
    [Arguments("authority-input")]
    [Arguments("authority-alias")]
    [Arguments("foreign-credential")]
    [Arguments("foreign-credential-case")]
    [Arguments("foreign-credential-alias")]
    [Arguments("foreign-private-jwks-alias")]
    [Arguments("blank-qualifier")]
    public async Task MalformedOrRetargetedLocalBindingIsRejected(string change)
    {
        var binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Smtp.Password, SecretScope.Instance, null, "MAIL_SMTP_PASSWORD");
        switch (change)
        {
            case "unknown-key": binding.SettingKey = "not.registered"; break;
            case "unknown-source": binding.SecretSourceTypeId = int.MaxValue; break;
            case "unknown-scope": binding.SettingScopeId = int.MaxValue; break;
            case "tenant-without-id": binding.Scope = SecretScope.Tenant; break;
            case "tenant-empty-id": binding.Scope = SecretScope.Tenant; binding.ScopeId = Guid.Empty; break;
            case "instance-with-id": binding.ScopeId = Guid.CreateVersion7(); break;
            case "mixed-source": binding.InfisicalKey = "OTHER"; break;
            case "interpolation": binding.EnvironmentVariableName = "${OTHER}"; break;
            case "authority-input": binding.EnvironmentVariableName = "SECRET_PROVIDER"; break;
            case "authority-alias": binding.EnvironmentVariableName = "DataProtection:KeyEncryptionKey"; break;
            case "foreign-credential": binding.EnvironmentVariableName = "KEYCLOAK_API_CLIENT_SECRET"; break;
            case "foreign-credential-case": binding.EnvironmentVariableName = "google_client_secret"; break;
            case "foreign-credential-alias": binding.EnvironmentVariableName = "Google__ClientSecret"; break;
            case "foreign-private-jwks-alias": binding.EnvironmentVariableName = "Atproto:OauthClientPrivateJwks"; break;
            case "blank-qualifier": binding.Qualifier = " "; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, ["/smtp"], "testing")).IsFalse();
    }

    [Test]
    [Arguments("environment")]
    [Arguments("folder")]
    [Arguments("interpolation")]
    [Arguments("path-key")]
    [Arguments("mixed-source")]
    [Arguments("protected-key")]
    public async Task NativeCoordinatesCannotEscapeSelectedEnvironmentOrLogicalRoot(string change)
    {
        var binding = SecretBinding.CreateInfisical(
            SecretDefinitionRegistry.Keys.Smtp.Password, SecretScope.Instance, null,
            "testing", "/smtp", "MAIL_SMTP_PASSWORD");
        switch (change)
        {
            case "environment": binding.InfisicalEnvironment = "production"; break;
            case "folder": binding.InfisicalPath = "/smtp/child"; break;
            case "interpolation": binding.InfisicalKey = "${OTHER}"; break;
            case "path-key": binding.InfisicalKey = "../MAIL_SMTP_PASSWORD"; break;
            case "mixed-source": binding.EnvironmentVariableName = "MAIL_SMTP_PASSWORD"; break;
            case "protected-key": binding.InfisicalKey = "CERBOS_ADMIN_PASSWORD"; break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, ["/smtp"], "testing")).IsFalse();
    }

    [Test]
    public async Task NativeTenantSelectionRetainsExplicitKeyWithoutHierarchyFallback()
    {
        var binding = SecretBinding.CreateInfisical(
            SecretDefinitionRegistry.Keys.Smtp.Password, SecretScope.Tenant, Guid.CreateVersion7(),
            "testing", "/smtp", "TENANT_MAIL_PASSWORD");
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, ["/smtp"], "testing")).IsTrue();
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, ["/smtp/child"], "testing")).IsFalse();
    }

    [Test]
    [Arguments(SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret)]
    [Arguments(SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret)]
    [Arguments(SecretDefinitionRegistry.Keys.Keycloak.LifecycleClientSecret)]
    [Arguments(SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks)]
    public async Task RuntimeCredentialsRequireCanonicalUnqualifiedInstanceBinding(string settingKey)
    {
        var definition = SecretDefinitionRegistry.GetRequired(settingKey);
        var binding = SecretBinding.CreateInfisical(settingKey, SecretScope.Instance, null,
            "testing", definition.DefaultInfisicalPath, definition.DefaultInfisicalKey);
        string[] roots = [definition.DefaultInfisicalPath];
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, roots, "testing")).IsTrue();
        binding.Qualifier = "another-purpose";
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, roots, "testing")).IsFalse();
        binding.Qualifier = string.Empty;
        binding.InfisicalKey += "_OTHER";
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, roots, "testing")).IsFalse();
        binding = SecretBinding.CreateEnvironmentVariable(settingKey, SecretScope.Instance, null,
            definition.DefaultEnvironmentVariableName);
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, roots, null)).IsTrue();
        binding.EnvironmentVariableName += "_OTHER";
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, roots, null)).IsFalse();
        binding.EnvironmentVariableName = definition.DefaultEnvironmentVariableName;
        binding.SettingKey = settingKey.ToUpperInvariant();
        await Assert.That(SecretBindingAdmission.AllowsBinding(binding, roots, null)).IsFalse();
    }
}
