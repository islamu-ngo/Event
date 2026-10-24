using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.UnitTests.Configuration;

[NotInParallel]
public sealed class SecretAuthorityConfigurationTests
{
    [Test]
    public async Task Build_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        IConfiguration configuration = ProviderConfiguration(SecretProviderType.UserSecrets);

        Action act = () => SecretAuthorityConfiguration.Build(configuration, "Production", "/api");

        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("secret_authority_user_secrets_environment_invalid");
    }

    [Test]
    public async Task Build_WhenUserSecretsIsSelected_DoesNotReadEnvironmentFallback()
    {
        string key = $"AUTHORITY_CANARY_{Guid.CreateVersion7():N}";
        string canary = Guid.CreateVersion7().ToString("N");
        Environment.SetEnvironmentVariable(key, canary);
        try
        {
            IConfiguration configuration = ProviderConfiguration(SecretProviderType.UserSecrets);

            IConfiguration authority = SecretAuthorityConfiguration.Build(configuration, "Testing", "/api");

            await Assert.That(authority[key]).IsNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Test]
    public async Task PreserveProviderSelection_WhenAuthorityContainsConflictingSelectors_KeepsUserSecrets()
    {
        string instancePassword = Guid.CreateVersion7().ToString("N");
        string tenantPassword = Guid.CreateVersion7().ToString("N");
        using var conflictingAuthority = new ConfigurationManager();
        conflictingAuthority.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Environment",
            ["SECRET_PROVIDER"] = "Infisical",
            ["Database:EmailPolicyInstanceMutation:Username"] = null,
            ["Database:EmailPolicyInstanceMutation:Password"] = string.Empty,
            ["DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_PASSWORD"] = instancePassword,
            ["Database:EmailPolicyTenantMutation:Password"] = " ",
            ["DATABASE_EMAIL_POLICY_TENANT_MUTATION_PASSWORD"] = tenantPassword,
        });

        IConfiguration locked = SecretAuthorityConfiguration.PreserveProviderSelection(
            conflictingAuthority,
            SecretProviderType.UserSecrets);
        using var lockedOwner = (IDisposable)locked;

        await Assert.That(SecretAuthorityConfiguration.GetRequiredProvider(locked))
            .IsEqualTo(SecretProviderType.UserSecrets);
        await Assert.That(locked["SECRET_PROVIDER"]).IsNull();
        await Assert.That(locked["Database:EmailPolicyInstanceMutation:Username"] is null
            && locked["Database:EmailPolicyInstanceMutation:Password"] == string.Empty
            && locked["DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_PASSWORD"] == instancePassword
            && locked["Database:EmailPolicyTenantMutation:Password"] == " "
            && locked["DATABASE_EMAIL_POLICY_TENANT_MUTATION_PASSWORD"] == tenantPassword).IsTrue()
            .Because("Selected startup authority preserves declared null, empty and whitespace without borrowing flat credentials.");
    }

    private static IConfiguration ProviderConfiguration(SecretProviderType provider) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = provider.ToString(),
        }).Build();
}
