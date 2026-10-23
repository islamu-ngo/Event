using ISLAMU.Event.Setup.Core.Environment;

namespace ISLAMU.Setup.Core.EnvironmentTests;

public sealed class ReservedFingerprintEnvironmentTests
{
    [Test]
    [Arguments("PRIVACY_ERASURE_IDENTITY_FENCE_KEY", EnvironmentVariableSensitivity.Secret)]
    [Arguments("PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION_MEASUREMENT_SIGNING_PRIVATE_KEY_PEM", EnvironmentVariableSensitivity.Secret)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__PROVIDER", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__HOST", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__PORT", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__DATABASE", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__SCHEMA", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__USERNAME", EnvironmentVariableSensitivity.Sensitive)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__PASSWORD", EnvironmentVariableSensitivity.Secret)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__TLSMODE", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__TRUSTSERVERCERTIFICATE", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__SERVERFLAVOR", EnvironmentVariableSensitivity.Public)]
    [Arguments("MODERATION__MEASUREMENTTESTDATABASE__SERVERVERSION", EnvironmentVariableSensitivity.Public)]
    public async Task ReservedFingerprintConfigurationIsOptionalAndNotStartupOwned(
        string name, EnvironmentVariableSensitivity sensitivity)
    {
        EnvironmentVariableDefinition definition = PlatformEnvironmentCatalogue.Catalogue.Lookup(name)!;
        await Assert.That(definition.Requirement).IsEqualTo(EnvironmentVariableRequirement.Optional);
        await Assert.That(definition.Sensitivity).IsEqualTo(sensitivity);
        await Assert.That(definition.SafeDefault).IsNull();
        await Assert.That(definition.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Startup)).IsFalse();
        await Assert.That(definition.RestartBehavior).IsEqualTo(EnvironmentRestartBehavior.Process);

        DotenvCompositionResult ordinary = DotenvComposer.ComposeNoSecrets(
            PlatformEnvironmentCatalogue.Catalogue,
            new EnvironmentActivationContext("standalone", ["platform", "identity"], ["local"]), []);
        await Assert.That(ordinary.Diagnostics.Any(item => item.Key == name)).IsFalse();
        DotenvReadinessResult readiness = DotenvReadiness.Evaluate(
            PlatformEnvironmentCatalogue.Catalogue,
            new EnvironmentActivationContext("standalone", ["platform", "identity"], ["local"]),
            ordinary.Document);
        await Assert.That(readiness.Missing.Contains(name)).IsFalse();
        await Assert.That(readiness.Blocked.Contains(name)).IsFalse();
    }
}
