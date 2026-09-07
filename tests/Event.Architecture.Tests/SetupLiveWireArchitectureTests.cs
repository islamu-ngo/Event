namespace Event.Architecture.Tests;

using System.Reflection;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class SetupLiveWireArchitectureTests
{
    [Test]
    public async Task ContractMustRemainPackageFreeAndServerAuthorityFree()
    {
        Assembly wireAssembly = typeof(ConfigurationManifestV1Alpha2).Assembly;
        const string setupNamespace = "ISLAMU.Wire.Contracts.SetupLive";
        string[] required =
        [
            "CreateSetupTargetEnrollmentRequest",
            "SetupTargetEnrollmentData",
            "SetupSecretBindingReadinessItem",
            "SetupSecretBindingOperationData",
            "SetupEnrollmentCapability",
            "SetupClientChallenge",
            "SetupLiveJsonContext"
        ];
        string[] missing = required
            .Where(name => wireAssembly.GetType($"{setupNamespace}.{name}") is null)
            .ToArray();
        string[] forbiddenReferences = wireAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null
                && !name.StartsWith("System", StringComparison.Ordinal)
                && name is not "netstandard")
            .Cast<string>()
            .ToArray();
        string[] forbiddenFragments =
        [
            "Registration", "Callback", "ManualImport", "Connection",
            "ProviderUrl", "EmbedTicket"
        ];
        string[] forbiddenTypes = wireAssembly.GetExportedTypes()
            .Where(type => string.Equals(
                type.Namespace, setupNamespace, StringComparison.Ordinal))
            .Where(type => forbiddenFragments.Any(fragment =>
                type.Name.Contains(fragment, StringComparison.Ordinal)))
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(missing).IsEmpty()
            .Because("D2-1 requires the exact package-free Setup live Wire closure: "
                + string.Join(", ", missing));
        await Assert.That(forbiddenReferences).IsEmpty()
            .Because("Event.Wire.Contracts cannot reference server/framework packages: "
                + string.Join(", ", forbiddenReferences));
        await Assert.That(forbiddenTypes).IsEmpty()
            .Because("P9-008 registration-provider surfaces are excluded from Setup: "
                + string.Join(", ", forbiddenTypes));
    }
}
