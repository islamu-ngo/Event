using ISLAMU.Event.Setup.Core.Environment;

namespace ISLAMU.Setup.Core.EnvironmentTests;

public sealed class PlatformCatalogueInitializationTests
{
    [Test]
    public async Task LifecycleSenderInputsHaveProtectedCredentialAndPublicRestartBoundControls()
    {
        var catalogue = PlatformEnvironmentCatalogue.Catalogue;
        var credential = catalogue.Lookup("KEYCLOAK_LIFECYCLE_CLIENT_SECRET")!;
        await Assert.That(credential.Sensitivity).IsEqualTo(EnvironmentVariableSensitivity.Secret);
        await Assert.That(credential.SafeDefault).IsNull();

        foreach (string suffix in new[]
                 {
                     "ENABLED", "BASEURL", "REALM", "CLIENTID", "DEFAULTCLIENTID",
                     "PROVIDERVERSION", "ALLOWLOCALURLS"
                 })
        {
            var control = catalogue.Lookup($"KEYCLOAKLIFECYCLEEMAIL__{suffix}")!;
            await Assert.That(control.Sensitivity).IsEqualTo(EnvironmentVariableSensitivity.Public);
            await Assert.That(control.RestartBehavior).IsEqualTo(EnvironmentRestartBehavior.Process);
            await Assert.That(control.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Startup)).IsTrue();
        }
    }

    [Test]
    [Arguments("API")]
    [Arguments("BFF")]
    [Arguments("COMBINED")]
    public async Task DataProtectionAdmissionRetainsSecretBindingClassification(string partition)
    {
        string key = $"EVENT_DP_{partition}_ADMISSION";
        var definition = PlatformEnvironmentCatalogue.Catalogue.Lookup(key)!;
        await Assert.That(definition.Sensitivity).IsEqualTo(EnvironmentVariableSensitivity.Secret);
        await Assert.That(definition.SafeDefault).IsNull();
        await Assert.That(PlatformEnvironmentCatalogue.SecretBindingEnvironmentKeys.Contains(key)).IsTrue();
    }

    [Test]
    [Arguments("INFISICAL_COMBINED_CLIENT_ID", EnvironmentVariableSensitivity.Public)]
    [Arguments("INFISICAL_COMBINED_CLIENT_SECRET", EnvironmentVariableSensitivity.Sensitive)]
    public async Task CombinedBootstrapIdentityHasOperatorSurfaceWithoutRuntimeOrSplitComposeProjection(
        string key, EnvironmentVariableSensitivity sensitivity)
    {
        var definition = PlatformEnvironmentCatalogue.Catalogue.Lookup(key)!;
        await Assert.That(definition.Sensitivity).IsEqualTo(sensitivity);
        await Assert.That(definition.Generation.Surfaces).IsEqualTo(EnvironmentGenerationSurface.Dotenv);
        await Assert.That(PlatformEnvironmentCatalogue.SecretBindingEnvironmentKeys.Contains(key)).IsFalse();
    }
}
