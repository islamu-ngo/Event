using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ReleaseImageManifestTests
{
    private const string SampleVersion = "1.1.0";
    private static readonly string SourceOid = new('a', 40);

    [Test]
    public async Task ParsesValidFourImageEightReferenceManifestDeterministically()
    {
        string json = ValidManifestJson();

        ReleaseImageManifestLoadResult result = ReleaseImageManifest.Parse(json, expectedVersion: SampleVersion, expectedCommitOid: SourceOid);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "valid manifest");
        await Assert.That(result.Manifest).IsNotNull();
        await Assert.That(result.Manifest!.Images).Count().IsEqualTo(4);
        await Assert.That(result.Manifest.Images.Select(i => i.ImageName).ToArray())
            .IsEquivalentTo(["api", "blazor", "migration", "standalone"]);
        await Assert.That(result.Manifest.Images.Sum(i => i.CanonicalReferences.Count)).IsEqualTo(8);
    }

    [Test]
    public async Task RejectsIncompleteImagesMissingRegistryTagReferencesAndMismatchedVersionOrCommit()
    {
        ReleaseImageManifestLoadResult wrongVersion = ReleaseImageManifest.Parse(ValidManifestJson(), "1.2.0", SourceOid);
        ReleaseImageManifestLoadResult wrongCommit = ReleaseImageManifest.Parse(ValidManifestJson(), SampleVersion, new string('b', 40));
        ReleaseImageManifestLoadResult taggedRef = ReleaseImageManifest.Parse(
            ValidManifestJson().Replace($"@sha256:{new string('1', 64)}", ":v1.1.0", StringComparison.Ordinal),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult mismatchedDigestRef = ReleaseImageManifest.Parse(
            ValidManifestJson().Replace($"ghcr.io/islamu-ngo/event-api@sha256:{new string('1', 64)}", $"ghcr.io/islamu-ngo/event-api@sha256:{new string('9', 64)}", StringComparison.Ordinal),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult singleRegistry = ReleaseImageManifest.Parse(
            ValidManifestJson().Replace($"\"atcr.io/islamu-ngo/event-api@sha256:{new string('1', 64)}\",", string.Empty, StringComparison.Ordinal),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult malformed = ReleaseImageManifest.Parse("{bad", SampleVersion, SourceOid);

        await Assert.That(wrongVersion.Diagnostic).IsEqualTo("image_manifest_version_mismatch");
        await Assert.That(wrongCommit.Diagnostic).IsEqualTo("image_manifest_commit_mismatch");
        await Assert.That(taggedRef.Diagnostic).IsEqualTo("image_manifest_reference_invalid");
        await Assert.That(mismatchedDigestRef.Diagnostic).IsEqualTo("image_manifest_reference_invalid");
        await Assert.That(singleRegistry.Diagnostic).IsEqualTo("image_manifest_reference_invalid");
        await Assert.That(malformed.Diagnostic).IsEqualTo("image_manifest_invalid");
    }

    [Test]
    public async Task LoadFromFileRejectsMissingAndSymlinkedManifestFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"islamu-img-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string realPath = Path.Combine(tempRoot, "release-images.v1.json");
            string linkedPath = Path.Combine(tempRoot, "linked-images.json");
            await File.WriteAllTextAsync(realPath, ValidManifestJson());
            File.CreateSymbolicLink(linkedPath, realPath);

            ReleaseImageManifestLoadResult valid = ReleaseImageManifest.LoadFromFile(realPath, SampleVersion, SourceOid);
            ReleaseImageManifestLoadResult missing = ReleaseImageManifest.LoadFromFile(Path.Combine(tempRoot, "missing.json"), SampleVersion, SourceOid);
            ReleaseImageManifestLoadResult linked = ReleaseImageManifest.LoadFromFile(linkedPath, SampleVersion, SourceOid);

            await Assert.That(valid.IsValid).IsTrue();
            await Assert.That(missing.Diagnostic).IsEqualTo("image_manifest_invalid");
            await Assert.That(linked.Diagnostic).IsEqualTo("image_manifest_invalid");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    internal static string ValidManifestJson(string version = SampleVersion, string? commitOid = null)
    {
        string oid = commitOid ?? SourceOid;
        string d1 = new('1', 64);
        string d2 = new('2', 64);
        string d3 = new('3', 64);
        string d4 = new('4', 64);
        return $$"""
            {
              "schemaVersion": 1,
              "version": "{{version}}",
              "sourceCommitOid": "{{oid}}",
              "images": [
                {
                  "name": "api",
                  "digest": "sha256:{{d1}}",
                  "references": [
                    "atcr.io/islamu-ngo/event-api@sha256:{{d1}}",
                    "ghcr.io/islamu-ngo/event-api@sha256:{{d1}}"
                  ]
                },
                {
                  "name": "blazor",
                  "digest": "sha256:{{d2}}",
                  "references": [
                    "atcr.io/islamu-ngo/event-blazor@sha256:{{d2}}",
                    "ghcr.io/islamu-ngo/event-blazor@sha256:{{d2}}"
                  ]
                },
                {
                  "name": "standalone",
                  "digest": "sha256:{{d3}}",
                  "references": [
                    "atcr.io/islamu-ngo/event-standalone@sha256:{{d3}}",
                    "ghcr.io/islamu-ngo/event-standalone@sha256:{{d3}}"
                  ]
                },
                {
                  "name": "migration",
                  "digest": "sha256:{{d4}}",
                  "references": [
                    "atcr.io/islamu-ngo/event-migration@sha256:{{d4}}",
                    "ghcr.io/islamu-ngo/event-migration@sha256:{{d4}}"
                  ]
                }
              ]
            }
            """;
    }
}
