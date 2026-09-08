using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;

namespace Event.Application.UnitTests.Authorization;

public class PolicyPackageManifestTests
{
    [Test]
    public async Task ContractTypeNames_ShouldRemainProviderNeutral()
    {
        var contractTypes = new[]
        {
            typeof(PolicyArtifactKind),
            typeof(PolicyPackageArtifact),
            typeof(PolicyPackageManifest),
            typeof(PolicyPackagePublishResult),
            typeof(IPolicyPackageService)
        };

        foreach (var type in contractTypes)
        {
            await Assert.That(type.FullName).DoesNotContain("Cerbos");
            await Assert.That(type.FullName).DoesNotContain("AdminApi");
            await Assert.That(type.FullName).DoesNotContain("Zip");
        }
    }

    [Test]
    public async Task Manifest_ShouldCarryStableArtifactMetadata()
    {
        var artifact = new PolicyPackageArtifact(
            LogicalId: "policies/islamuevent_event.yaml",
            Kind: PolicyArtifactKind.Policy,
            Sha256: new string('a', 64),
            SizeInBytes: 128,
            Metadata: new Dictionary<string, string> { ["extension"] = "yaml" });

        var manifest = new PolicyPackageManifest(
            PackageId: "islamuevent-authorization-policies",
            Version: new string('b', 64),
            ContentHash: new string('b', 64),
            GeneratedAt: DateTimeOffset.UtcNow,
            Artifacts: [artifact]);

        await Assert.That(manifest.PackageId).IsEqualTo("islamuevent-authorization-policies");
        await Assert.That(manifest.Artifacts.Count).IsEqualTo(1);
        await Assert.That(manifest.Artifacts[0].LogicalId).IsEqualTo("policies/islamuevent_event.yaml");
        await Assert.That(manifest.Artifacts[0].Kind).IsEqualTo(PolicyArtifactKind.Policy);
        await Assert.That(manifest.Artifacts[0].Metadata["extension"]).IsEqualTo("yaml");
    }
}
