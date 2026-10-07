using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Secrets;

public sealed class AuthenticationCredentialCustodyPolicyTests
{
    [Test]
    [Arguments("auth.google_client_secret", false)]
    [Arguments("auth.google_client_secret", true)]
    [Arguments("keycloak.blazor_client_secret", false)]
    [Arguments("keycloak.blazor_client_secret", true)]
    public async Task ExactInstanceReferencePreservesItsRuntimeConsumer(string key, bool infisical)
    {
        var definition = SecretDefinitionRegistry.GetRequired(key);
        var binding = infisical
            ? SecretBinding.CreateInfisical(key, SecretScope.Instance, null, "testing",
                definition.DefaultInfisicalPath!, definition.DefaultInfisicalKey!)
            : SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null,
                definition.DefaultEnvironmentVariableName!);
        await Assert.That(AuthenticationCredentialCustodyPolicy.HasCanonicalRuntimeReference(binding)).IsTrue();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("scope-id")]
    [Arguments("qualified")]
    [Arguments("foreign-key")]
    [Arguments("key-case")]
    [Arguments("variable")]
    [Arguments("source")]
    [Arguments("root")]
    public async Task ChangedOwnershipOrReferenceCannotBecomeCanonical(string change)
    {
        const string key = SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret;
        var definition = SecretDefinitionRegistry.GetRequired(key);
        var binding = SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null,
            definition.DefaultEnvironmentVariableName!);
        switch (change)
        {
            case "tenant": binding.Scope = SecretScope.Tenant; binding.ScopeId = Guid.CreateVersion7(); break;
            case "scope-id": binding.ScopeId = Guid.CreateVersion7(); break;
            case "qualified": binding.Qualifier = "other-purpose"; break;
            case "foreign-key": binding.SettingKey = SecretDefinitionRegistry.Keys.Keycloak.DbPassword; break;
            case "key-case": binding.SettingKey = key.ToUpperInvariant(); break;
            case "variable": binding.EnvironmentVariableName = "OTHER_CLIENT_SECRET_REFERENCE"; break;
            case "source": binding.SecretSourceTypeId = int.MaxValue; break;
            case "root":
                binding = SecretBinding.CreateInfisical(key, SecretScope.Instance, null,
                    "testing", "/google", definition.DefaultInfisicalKey!);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        await Assert.That(AuthenticationCredentialCustodyPolicy.HasCanonicalRuntimeReference(binding)).IsFalse();
    }
}
