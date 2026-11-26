using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ReleaseImageManifestTests
{
    private const string SampleVersion = "1.1.0";
    private const string GhcrRegistry = "ghcr.io";
    private const string DefaultOwner = "islamu-ngo";
    private const string ReferenceInvalidDiagnostic = "image_manifest_reference_invalid";
    private static readonly string SourceOid = new('a', 40);

    [Test]
    public async Task ParsesValidFourImageEightReferenceDualRegistryManifestDeterministically()
    {
        string json = ValidManifestJson();

        ReleaseImageManifestLoadResult result = ReleaseImageManifest.Parse(json, expectedVersion: SampleVersion, expectedCommitOid: SourceOid);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "valid dual-registry manifest");
        await Assert.That(result.Manifest).IsNotNull();
        await Assert.That(result.Manifest!.Images).Count().IsEqualTo(4);
        await Assert.That(result.Manifest.Images.Select(i => i.ImageName).ToArray())
            .IsEquivalentTo(["api", "blazor", "migration", "standalone"]);
        await Assert.That(result.Manifest.Images.Sum(i => i.CanonicalReferences.Count)).IsEqualTo(8);
    }

    [Test]
    public async Task ParsesValidSingleRegistryGhcrOnlyAndAlternativeProviderManifests()
    {
        string ghcrOnlyJson = ValidSingleRegistryManifestJson(GhcrRegistry, DefaultOwner);
        ReleaseImageManifestLoadResult ghcrOnly = ReleaseImageManifest.Parse(ghcrOnlyJson, SampleVersion, SourceOid);

        await Assert.That(ghcrOnly.IsValid).IsTrue().Because(ghcrOnly.Diagnostic ?? "valid ghcr-only manifest");
        await Assert.That(ghcrOnly.Manifest!.Images).Count().IsEqualTo(4);
        await Assert.That(ghcrOnly.Manifest.Images.Sum(i => i.CanonicalReferences.Count)).IsEqualTo(4);

        string customMirrorJson = ValidManifestJson(secondaryRegistry: "quay.io", secondaryOwner: "team__events");
        ReleaseImageManifestLoadResult customMirror = ReleaseImageManifest.Parse(
            customMirrorJson,
            SampleVersion,
            SourceOid,
            requiredRegistries: [GhcrRegistry, "quay.io"]);

        await Assert.That(customMirror.IsValid).IsTrue().Because(customMirror.Diagnostic ?? "valid custom secondary registry with double underscore");
        await Assert.That(customMirror.Manifest!.Images.Sum(i => i.CanonicalReferences.Count)).IsEqualTo(8);

        string dockerHubMirrorJson = ValidManifestJson(secondaryRegistry: "docker.io", secondaryOwner: "my--org");
        ReleaseImageManifestLoadResult dockerHubMirror = ReleaseImageManifest.Parse(
            dockerHubMirrorJson,
            SampleVersion,
            SourceOid,
            requiredRegistries: [GhcrRegistry, "docker.io"]);

        await Assert.That(dockerHubMirror.IsValid).IsTrue().Because(dockerHubMirror.Diagnostic ?? "valid docker hub mirror with consecutive hyphens");
        await Assert.That(dockerHubMirror.Manifest!.Images.Sum(i => i.CanonicalReferences.Count)).IsEqualTo(8);

        ReleaseImageManifestLoadResult missingRequired = ReleaseImageManifest.Parse(
            ghcrOnlyJson,
            SampleVersion,
            SourceOid,
            requiredRegistries: [GhcrRegistry, "quay.io"]);
        await Assert.That(missingRequired.IsValid).IsFalse();
        await Assert.That(missingRequired.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
    }

    [Test]
    public async Task RejectsAsymmetricRegistriesTaggedReferencesInvalidHostsAndMismatchedVersionOrCommit()
    {
        ReleaseImageManifestLoadResult wrongVersion = ReleaseImageManifest.Parse(ValidManifestJson(), "1.2.0", SourceOid);
        ReleaseImageManifestLoadResult wrongCommit = ReleaseImageManifest.Parse(ValidManifestJson(), SampleVersion, new string('b', 40));
        ReleaseImageManifestLoadResult taggedRef = ReleaseImageManifest.Parse(
            ValidManifestJson().Replace($"@sha256:{new string('1', 64)}", ":v1.1.0", StringComparison.Ordinal),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult mismatchedDigestRef = ReleaseImageManifest.Parse(
            ValidManifestJson().Replace($"{GhcrRegistry}/{DefaultOwner}/event-api@sha256:{new string('1', 64)}", $"{GhcrRegistry}/{DefaultOwner}/event-api@sha256:{new string('9', 64)}", StringComparison.Ordinal),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult asymmetricRegistry = ReleaseImageManifest.Parse(
            ValidManifestJson().Replace($"\"atcr.io/{DefaultOwner}/event-api@sha256:{new string('1', 64)}\",", string.Empty, StringComparison.Ordinal),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult badHost = ReleaseImageManifest.Parse(
            ValidSingleRegistryManifestJson("localhost", DefaultOwner),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult tripleUnderscoreNamespace = ReleaseImageManifest.Parse(
            ValidSingleRegistryManifestJson("registry.example.com", "team___events"),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult doubleDotNamespace = ReleaseImageManifest.Parse(
            ValidSingleRegistryManifestJson("registry.example.com", "team..events"),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult emptyRefs = ReleaseImageManifest.Parse(
            ValidSingleRegistryManifestJson(GhcrRegistry, DefaultOwner).Replace($"\"{GhcrRegistry}/{DefaultOwner}/event-api@sha256:{new string('1', 64)}\"", string.Empty, StringComparison.Ordinal),
            SampleVersion,
            SourceOid);
        ReleaseImageManifestLoadResult malformed = ReleaseImageManifest.Parse("{bad", SampleVersion, SourceOid);

        await Assert.That(wrongVersion.Diagnostic).IsEqualTo("image_manifest_version_mismatch");
        await Assert.That(wrongCommit.Diagnostic).IsEqualTo("image_manifest_commit_mismatch");
        await Assert.That(taggedRef.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
        await Assert.That(mismatchedDigestRef.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
        await Assert.That(asymmetricRegistry.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
        await Assert.That(badHost.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
        await Assert.That(tripleUnderscoreNamespace.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
        await Assert.That(doubleDotNamespace.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
        await Assert.That(emptyRefs.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
        await Assert.That(malformed.Diagnostic).IsEqualTo("image_manifest_invalid");
    }

    [Test]
    public async Task ParseRejectsTrailingNewlineInCanonicalReferenceAndDigest()
    {
        string validJson = ValidSingleRegistryManifestJson();
        string d1 = new('1', 64);
        string reference = $"ghcr.io/{DefaultOwner}/event-api@sha256:{d1}";
        string trailingNewlineRefJson = validJson.Replace(reference, reference + "\\n", StringComparison.Ordinal);
        string trailingNewlineDigestJson = validJson.Replace($"\"sha256:{d1}\"", $"\"sha256:{d1}\\n\"", StringComparison.Ordinal);

        ReleaseImageManifestLoadResult valid = ReleaseImageManifest.Parse(validJson, SampleVersion, SourceOid);
        ReleaseImageManifestLoadResult refWithNewline = ReleaseImageManifest.Parse(trailingNewlineRefJson, SampleVersion, SourceOid);
        ReleaseImageManifestLoadResult digestWithNewline = ReleaseImageManifest.Parse(trailingNewlineDigestJson, SampleVersion, SourceOid);

        await Assert.That(valid.IsValid).IsTrue().Because(valid.Diagnostic ?? "valid baseline manifest");
        await Assert.That(refWithNewline.IsValid).IsFalse();
        await Assert.That(refWithNewline.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
        await Assert.That(digestWithNewline.IsValid).IsFalse();
        await Assert.That(digestWithNewline.Diagnostic).IsEqualTo(ReferenceInvalidDiagnostic);
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

    internal static string ValidSingleRegistryManifestJson(
        string registry = GhcrRegistry,
        string owner = DefaultOwner,
        string version = SampleVersion,
        string? commitOid = null)
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
                    "{{registry}}/{{owner}}/event-api@sha256:{{d1}}"
                  ]
                },
                {
                  "name": "blazor",
                  "digest": "sha256:{{d2}}",
                  "references": [
                    "{{registry}}/{{owner}}/event-blazor@sha256:{{d2}}"
                  ]
                },
                {
                  "name": "standalone",
                  "digest": "sha256:{{d3}}",
                  "references": [
                    "{{registry}}/{{owner}}/event-standalone@sha256:{{d3}}"
                  ]
                },
                {
                  "name": "migration",
                  "digest": "sha256:{{d4}}",
                  "references": [
                    "{{registry}}/{{owner}}/event-migration@sha256:{{d4}}"
                  ]
                }
              ]
            }
            """;
    }

    internal static string ValidManifestJson(
        string version = SampleVersion,
        string? commitOid = null,
        string secondaryRegistry = "atcr.io",
        string secondaryOwner = DefaultOwner)
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
                    "{{secondaryRegistry}}/{{secondaryOwner}}/event-api@sha256:{{d1}}",
                    "ghcr.io/islamu-ngo/event-api@sha256:{{d1}}"
                  ]
                },
                {
                  "name": "blazor",
                  "digest": "sha256:{{d2}}",
                  "references": [
                    "{{secondaryRegistry}}/{{secondaryOwner}}/event-blazor@sha256:{{d2}}",
                    "ghcr.io/islamu-ngo/event-blazor@sha256:{{d2}}"
                  ]
                },
                {
                  "name": "standalone",
                  "digest": "sha256:{{d3}}",
                  "references": [
                    "{{secondaryRegistry}}/{{secondaryOwner}}/event-standalone@sha256:{{d3}}",
                    "ghcr.io/islamu-ngo/event-standalone@sha256:{{d3}}"
                  ]
                },
                {
                  "name": "migration",
                  "digest": "sha256:{{d4}}",
                  "references": [
                    "{{secondaryRegistry}}/{{secondaryOwner}}/event-migration@sha256:{{d4}}",
                    "ghcr.io/islamu-ngo/event-migration@sha256:{{d4}}"
                  ]
                }
              ]
            }
            """;
    }
}
