using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Secrets;

public sealed class GoogleSecretAuthorityTests
{
    [Test]
    public async Task GoogleRuntimeCredentialHasOnlyInstanceScopedExternalAuthority()
    {
        SecretDefinition? definition = SecretDefinitionRegistry.TryGet(
            InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.AllowedScopes).Contains(SecretScope.Instance);
        await Assert.That(definition.AllowedScopes).DoesNotContain(SecretScope.Tenant);
        await Assert.That(definition.Requirement).IsEqualTo(SecretRequirement.OptionalCapability);
    }
}
