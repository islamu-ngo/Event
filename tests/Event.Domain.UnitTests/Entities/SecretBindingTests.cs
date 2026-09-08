using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Entities;

public class SecretBindingTests
{
    [Test]
    public async Task DomainCannotRepresentInlineSecretValues()
    {
        await Assert.That(Enum.GetNames<SecretSourceType>()).DoesNotContain("InlineEncrypted");
        await Assert.That(typeof(SecretBinding).GetProperty("InlineCiphertext")).IsNull();
        await Assert.That(typeof(SecretBinding).GetProperty("InlineCiphertextVersion")).IsNull();
        await Assert.That(typeof(SecretBinding).GetMethod("CreateInlineEncrypted")).IsNull();
        await Assert.That(typeof(SecretBinding).GetMethod("SwitchToInlineEncrypted")).IsNull();
    }

    [Test]
    public async Task SecretBinding_ImplementsAuditableEntityInterface()
    {
        await Assert.That(typeof(SecretBinding).GetInterfaces().Contains(typeof(IAuditableEntity))).IsTrue();
    }

    // ==================================================================================
    // Registry gate
    // ==================================================================================

    [Test]
    public async Task CreateInfisical_UnknownKey_Throws()
    {
        await Assert.That(() => SecretBinding.CreateInfisical(
            settingKey: "definitely.not.a.real.key",
            scope: SecretScope.Instance,
            scopeId: null,
            environment: "prod",
            path: "/whatever",
            key: "WHATEVER")).Throws<ArgumentException>();
    }

    // ==================================================================================
    // Scope/ScopeId consistency
    // ==================================================================================

    [Test]
    public async Task CreateInfisical_InstanceScopeWithScopeId_Throws()
    {
        await Assert.That(() => SecretBinding.CreateInfisical(
            settingKey: SecretDefinitionRegistry.Keys.Smtp.Host,
            scope: SecretScope.Instance,
            scopeId: Guid.NewGuid(),
            environment: "prod",
            path: "/smtp",
            key: "MAIL_SMTP_HOST")).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateInfisical_TenantScopeWithoutScopeId_Throws()
    {
        await Assert.That(() => SecretBinding.CreateInfisical(
            settingKey: SecretDefinitionRegistry.Keys.Smtp.Host,
            scope: SecretScope.Tenant,
            scopeId: null,
            environment: "prod",
            path: "/smtp",
            key: "MAIL_SMTP_HOST")).Throws<ArgumentException>();
    }

    [Test]
    public async Task CreateInfisical_BootstrapSecretAtTenantScope_Throws()
    {
        // postgresql.host is Instance-only per registry.
        await Assert.That(() => SecretBinding.CreateInfisical(
            settingKey: SecretDefinitionRegistry.Keys.Postgresql.Host,
            scope: SecretScope.Tenant,
            scopeId: Guid.NewGuid(),
            environment: "prod",
            path: "/postgresql",
            key: "POSTGRESQL_HOST")).Throws<ArgumentException>();
    }

    // ==================================================================================
    // Metadata population (normalized, exactly-one group)
    // ==================================================================================

    [Test]
    public async Task CreateInfisical_PopulatesOnlyInfisicalMetadata()
    {
        var binding = SecretBinding.CreateInfisical(
            settingKey: SecretDefinitionRegistry.Keys.Storage.AccessKeyId,
            scope: SecretScope.Instance,
            scopeId: null,
            environment: "prod",
            path: "/storage",
            key: "STORAGE_S3_ACCESS_KEY_ID");

        await Assert.That(binding.InfisicalEnvironment).IsEqualTo("prod");
        await Assert.That(binding.InfisicalPath).IsEqualTo("/storage");
        await Assert.That(binding.InfisicalKey).IsEqualTo("STORAGE_S3_ACCESS_KEY_ID");
        await Assert.That(binding.EnvironmentVariableName).IsNull();
    }

    [Test]
    public async Task CreateEnvironmentVariable_PopulatesOnlyEnvironmentMetadata()
    {
        var binding = SecretBinding.CreateEnvironmentVariable(
            settingKey: SecretDefinitionRegistry.Keys.Analytics.PosthogPublicKey,
            scope: SecretScope.Instance,
            scopeId: null,
            variableName: "ANALYTICS_POSTHOG_PUBLIC_KEY");

        await Assert.That(binding.EnvironmentVariableName).IsEqualTo("ANALYTICS_POSTHOG_PUBLIC_KEY");
        await Assert.That(binding.InfisicalEnvironment).IsNull();
        await Assert.That(binding.InfisicalPath).IsNull();
        await Assert.That(binding.InfisicalKey).IsNull();
    }

    // ==================================================================================
    // Switch methods reset validation state
    // ==================================================================================

    [Test]
    public async Task SwitchToEnvironmentVariable_ResetsValidationAndMetadata()
    {
        var binding = SecretBinding.CreateInfisical(
            settingKey: SecretDefinitionRegistry.Keys.Smtp.Password,
            scope: SecretScope.Instance,
            scopeId: null,
            environment: "prod",
            path: "/smtp",
            key: "MAIL_SMTP_PASSWORD");

        binding.RecordValidation(SecretValidationResult.Success, Guid.NewGuid());
        await Assert.That(binding.LastValidationResult).IsEqualTo(SecretValidationResult.Success);

        binding.SwitchToEnvironmentVariable("MAIL_SMTP_PASSWORD");

        await Assert.That(binding.SourceType).IsEqualTo(SecretSourceType.EnvironmentVariable);
        await Assert.That(binding.EnvironmentVariableName).IsEqualTo("MAIL_SMTP_PASSWORD");
        await Assert.That(binding.InfisicalEnvironment).IsNull();
        await Assert.That(binding.InfisicalPath).IsNull();
        await Assert.That(binding.InfisicalKey).IsNull();
        await Assert.That(binding.LastValidationResult).IsEqualTo(SecretValidationResult.NotValidated);
        await Assert.That(binding.LastValidatedAt).IsNull();
        await Assert.That(binding.LastValidationError).IsNull();
    }

    // ==================================================================================
    // RecordValidation contract
    // ==================================================================================

    [Test]
    public async Task RecordValidation_Failure_WithoutError_Throws()
    {
        var binding = SecretBinding.CreateEnvironmentVariable(
            settingKey: SecretDefinitionRegistry.Keys.Smtp.Host,
            scope: SecretScope.Instance,
            scopeId: null,
            variableName: "MAIL_SMTP_HOST");

        await Assert.That(() => binding.RecordValidation(SecretValidationResult.Failure, Guid.NewGuid(), sanitisedError: null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RecordValidation_Success_SetsValidatedAtAndBy()
    {
        var binding = SecretBinding.CreateEnvironmentVariable(
            settingKey: SecretDefinitionRegistry.Keys.Smtp.Host,
            scope: SecretScope.Instance,
            scopeId: null,
            variableName: "MAIL_SMTP_HOST");

        var userId = Guid.NewGuid();
        binding.RecordValidation(SecretValidationResult.Success, userId);

        await Assert.That(binding.LastValidationResult).IsEqualTo(SecretValidationResult.Success);
        await Assert.That(binding.LastValidatedBy).IsEqualTo(userId);
        await Assert.That(binding.LastValidatedAt).IsNotNull();
        await Assert.That(binding.LastValidationError).IsNull();
    }

    // ==================================================================================
    // Registry integrity
    // ==================================================================================


    [Test]
    public async Task Registry_CerbosAdminCredentials_AreNonBootstrapSecretDefinitions()
    {
        var username = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Cerbos.CustomAdminUsername);
        var password = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Cerbos.CustomAdminPassword);

        await Assert.That(username.DefaultInfisicalPath).IsEqualTo("/cerbos");
        await Assert.That(username.DefaultEnvironmentVariableName).IsEqualTo("CERBOS_ADMIN_USERNAME");
        await Assert.That(username.IsBootstrapSecret).IsFalse();
        await Assert.That(username.AllowedSources).IsEquivalentTo([
            SecretSourceType.Infisical,
            SecretSourceType.EnvironmentVariable]);

        await Assert.That(password.DefaultInfisicalPath).IsEqualTo("/cerbos");
        await Assert.That(password.DefaultEnvironmentVariableName).IsEqualTo("CERBOS_ADMIN_PASSWORD");
        await Assert.That(password.IsBootstrapSecret).IsFalse();
        await Assert.That(password.AllowedSources).IsEquivalentTo([
            SecretSourceType.Infisical,
            SecretSourceType.EnvironmentVariable]);
    }

    [Test]
    public async Task Registry_PostgresqlAndSetupSecret_AreBootstrap()
    {
        await Assert.That(SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.SetupSecret).IsBootstrapSecret).IsTrue();
        await Assert.That(SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Postgresql.Host).IsBootstrapSecret).IsTrue();
        await Assert.That(SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Postgresql.Port).IsBootstrapSecret).IsTrue();
        await Assert.That(SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Postgresql.Database).IsBootstrapSecret).IsTrue();
        await Assert.That(SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Postgresql.Username).IsBootstrapSecret).IsTrue();
        await Assert.That(SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.Postgresql.Password).IsBootstrapSecret).IsTrue();
    }
}
