using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ReleaseArtifactManifestTests
{
    private const string SampleVersion = "1.1.0";
    private const string SampleTag = "v1.1.0";
    private static readonly string SampleCommitOid = new('a', 40);

    [Test]
    public async Task SealsExactArtifactInventoryWithSortedChecksumsAndEightCanonicalRegistryReferences()
    {
        using var fixture = new ArtifactSealFixture();

        ReleaseArtifactSealResult result = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "valid seal");
        await Assert.That(result.Manifest).IsNotNull();
        await Assert.That(result.ManifestJson).IsNotNull();
        await Assert.That(result.ChecksumsContent).IsNotNull();

        ReleaseArtifactManifestDocument manifest = result.Manifest!;
        await Assert.That(manifest.SchemaVersion).IsEqualTo(1);
        await Assert.That(manifest.Version).IsEqualTo(SampleVersion);
        await Assert.That(manifest.Tag).IsEqualTo(SampleTag);
        await Assert.That(manifest.SourceCommitOid).IsEqualTo(SampleCommitOid);
        await Assert.That(manifest.Files.Select(f => f.RelativePath).ToArray())
            .IsEquivalentTo(
            [
                "contract-diff.v1.json",
                "release-context.v1.json",
                "release-images.v1.json",
                "release-notes.md",
                "release-rendering-context.v1.json",
            ]);
        await Assert.That(manifest.Images).Count().IsEqualTo(4);
        await Assert.That(manifest.Images.Select(i => i.ImageName).ToArray())
            .IsEquivalentTo(["api", "blazor", "migration", "standalone"]);
        await Assert.That(manifest.Images.Sum(i => i.CanonicalReferences.Count)).IsEqualTo(8);
        foreach (EnrichedImageReference image in manifest.Images)
        {
            await Assert.That(image.CanonicalReferences.Any(r => r.StartsWith("atcr.io/", StringComparison.Ordinal))).IsTrue();
            await Assert.That(image.CanonicalReferences.Any(r => r.StartsWith("ghcr.io/", StringComparison.Ordinal))).IsTrue();
        }

        string checksumsPath = Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ChecksumsFileName);
        string manifestPath = Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ManifestFileName);
        await Assert.That(File.Exists(checksumsPath)).IsTrue();
        await Assert.That(File.Exists(manifestPath)).IsTrue();

        byte[] checksumBytes = await File.ReadAllBytesAsync(checksumsPath);
        await Assert.That(checksumBytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble())).IsFalse();
        string checksumText = Encoding.UTF8.GetString(checksumBytes);
        await Assert.That(checksumText).DoesNotContain("\r");
        await Assert.That(checksumText).EndsWith("\n");

        string[] lines = checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(lines).Count().IsEqualTo(6);
        string[] expectedOrder =
        [
            "contract-diff.v1.json",
            "release-artifact-manifest.v1.json",
            "release-context.v1.json",
            "release-images.v1.json",
            "release-notes.md",
            "release-rendering-context.v1.json",
        ];
        for (int index = 0; index < expectedOrder.Length; index++)
        {
            string filePath = Path.Combine(fixture.ArtifactDirectory, expectedOrder[index]);
            string expectedHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(filePath)));
            await Assert.That(lines[index]).IsEqualTo($"{expectedHash}  {expectedOrder[index]}");
        }
    }

    [Test]
    public async Task RepeatedSealIntoFreshOutputDirectoriesProducesByteIdenticalManifestAndChecksums()
    {
        using var fixture = new ArtifactSealFixture();
        string outputA = Path.Combine(fixture.Root, "sealed-a");
        string outputB = Path.Combine(fixture.Root, "sealed-b");

        ReleaseArtifactSealResult first = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            outputA));
        ReleaseArtifactSealResult second = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            outputB));

        await Assert.That(first.IsValid).IsTrue().Because(first.Diagnostic ?? "first seal");
        await Assert.That(second.IsValid).IsTrue().Because(second.Diagnostic ?? "second seal");
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(outputA, ReleaseArtifactManifest.ChecksumsFileName)))
            .IsEquivalentTo(await File.ReadAllBytesAsync(Path.Combine(outputB, ReleaseArtifactManifest.ChecksumsFileName)));
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(outputA, ReleaseArtifactManifest.ManifestFileName)))
            .IsEquivalentTo(await File.ReadAllBytesAsync(Path.Combine(outputB, ReleaseArtifactManifest.ManifestFileName)));
    }

    [Test]
    public async Task RejectsClobberingExistingChecksumsOrManifestWithoutModifyingOriginalBytes()
    {
        using var fixture = new ArtifactSealFixture();
        string outputDir = Path.Combine(fixture.Root, "out");
        Directory.CreateDirectory(outputDir);
        string existingChecksums = Path.Combine(outputDir, ReleaseArtifactManifest.ChecksumsFileName);
        byte[] sentinelBytes = "original-sealed-checksums\n"u8.ToArray();
        await File.WriteAllBytesAsync(existingChecksums, sentinelBytes);

        ReleaseArtifactSealResult clobberChecksums = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            outputDir));

        await Assert.That(clobberChecksums.IsValid).IsFalse();
        await Assert.That(clobberChecksums.Diagnostic).IsEqualTo("artifact_manifest_output_exists");
        await Assert.That(await File.ReadAllBytesAsync(existingChecksums)).IsEquivalentTo(sentinelBytes);
        await Assert.That(File.Exists(Path.Combine(outputDir, ReleaseArtifactManifest.ManifestFileName))).IsFalse();

        ReleaseArtifactSealResult initialInPlace = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(initialInPlace.IsValid).IsTrue().Because(initialInPlace.Diagnostic ?? "initial seal");

        byte[] sealedManifestBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ManifestFileName));
        ReleaseArtifactSealResult secondInPlace = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(secondInPlace.IsValid).IsFalse();
        await Assert.That(secondInPlace.Diagnostic).IsEqualTo("artifact_manifest_output_exists");
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ManifestFileName)))
            .IsEquivalentTo(sealedManifestBytes);
    }

    [Test]
    [Arguments("contract-diff.v1.json")]
    [Arguments("release-context.v1.json")]
    [Arguments("release-images.v1.json")]
    [Arguments("release-notes.md")]
    [Arguments("release-rendering-context.v1.json")]
    public async Task RejectsMissingRequiredArtifactFile(string missingFileName)
    {
        using var fixture = new ArtifactSealFixture();
        File.Delete(Path.Combine(fixture.ArtifactDirectory, missingFileName));

        ReleaseArtifactSealResult result = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostic).IsEqualTo($"artifact_manifest_missing_file:{missingFileName}");
        await Assert.That(File.Exists(Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ChecksumsFileName))).IsFalse();
    }

    [Test]
    public async Task RejectsExtraUnexpectedFilesAndSubdirectories()
    {
        using var extraFixture = new ArtifactSealFixture();
        await File.WriteAllTextAsync(Path.Combine(extraFixture.ArtifactDirectory, "raw-context.json"), "{}\n");

        ReleaseArtifactSealResult extraResult = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            extraFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(extraResult.IsValid).IsFalse();
        await Assert.That(extraResult.Diagnostic).IsEqualTo("artifact_manifest_unexpected_file:raw-context.json");

        using var dirFixture = new ArtifactSealFixture();
        Directory.CreateDirectory(Path.Combine(dirFixture.ArtifactDirectory, "nested"));

        ReleaseArtifactSealResult dirResult = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            dirFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(dirResult.IsValid).IsFalse();
        await Assert.That(dirResult.Diagnostic).IsEqualTo("artifact_manifest_unexpected_directory");
    }

    [Test]
    public async Task RejectsSymlinksPathTraversalAndNestedOutputDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ArtifactSealFixture();
        string outsideFile = Path.Combine(fixture.Root, "outside-notes.md");
        await File.WriteAllTextAsync(outsideFile, "# Release 1.1.0\n\nValid notes.\n");
        string notesPath = Path.Combine(fixture.ArtifactDirectory, "release-notes.md");
        File.Delete(notesPath);
        File.CreateSymbolicLink(notesPath, outsideFile);

        ReleaseArtifactSealResult symlinkFile = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(symlinkFile.IsValid).IsFalse();
        await Assert.That(symlinkFile.Diagnostic).IsEqualTo("artifact_manifest_symlink_rejected");

        using var dirLinkFixture = new ArtifactSealFixture();
        string linkedArtifactDir = Path.Combine(dirLinkFixture.Root, "linked-artifacts");
        Directory.CreateSymbolicLink(linkedArtifactDir, dirLinkFixture.ArtifactDirectory);
        ReleaseArtifactSealResult symlinkDir = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            linkedArtifactDir,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(symlinkDir.IsValid).IsFalse();
        await Assert.That(symlinkDir.Diagnostic).IsEqualTo("artifact_manifest_directory_invalid");

        string realOutput = Path.Combine(dirLinkFixture.Root, "real-output");
        Directory.CreateDirectory(realOutput);
        string linkedOutput = Path.Combine(dirLinkFixture.Root, "linked-output");
        Directory.CreateSymbolicLink(linkedOutput, realOutput);
        ReleaseArtifactSealResult symlinkOut = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            dirLinkFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            linkedOutput));
        await Assert.That(symlinkOut.IsValid).IsFalse();
        await Assert.That(symlinkOut.Diagnostic).IsEqualTo("artifact_manifest_output_invalid");

        string nestedOutput = Path.Combine(dirLinkFixture.ArtifactDirectory, "sub-output");
        ReleaseArtifactSealResult nestedOut = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            dirLinkFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            nestedOutput));
        await Assert.That(nestedOut.IsValid).IsFalse();
        await Assert.That(nestedOut.Diagnostic).IsEqualTo("artifact_manifest_output_invalid");

        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("../escape.json")).IsFalse();
        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("/etc/passwd")).IsFalse();
        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("nested\\file.json")).IsFalse();
        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("nested/file.json")).IsFalse();
        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("release-notes.md")).IsTrue();
    }

    [Test]
    public async Task RejectsTaggedImageReferencesSingleRegistryCoverageAndMismatchedMetadata()
    {
        using var taggedFixture = new ArtifactSealFixture();
        string badTaggedJson = ReleaseImageManifestTests.ValidManifestJson(SampleVersion, SampleCommitOid)
            .Replace($"@sha256:{new string('1', 64)}", ":v1.1.0", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(taggedFixture.ArtifactDirectory, "release-images.v1.json"), badTaggedJson);

        ReleaseArtifactSealResult taggedResult = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            taggedFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(taggedResult.IsValid).IsFalse();
        await Assert.That(taggedResult.Diagnostic).IsEqualTo("image_manifest_reference_invalid");

        using var singleRegistryFixture = new ArtifactSealFixture();
        string singleRegistryJson = ReleaseImageManifestTests.ValidManifestJson(SampleVersion, SampleCommitOid)
            .Replace($"\"atcr.io/islamu-ngo/event-api@sha256:{new string('1', 64)}\",", string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(singleRegistryFixture.ArtifactDirectory, "release-images.v1.json"), singleRegistryJson);

        ReleaseArtifactSealResult singleRegistryResult = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            singleRegistryFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(singleRegistryResult.IsValid).IsFalse();
        await Assert.That(singleRegistryResult.Diagnostic).IsEqualTo("image_manifest_reference_invalid");

        using var tagMismatchFixture = new ArtifactSealFixture();
        ReleaseArtifactSealResult tagMismatch = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            tagMismatchFixture.ArtifactDirectory,
            SampleVersion,
            "v1.2.0",
            SampleCommitOid));
        await Assert.That(tagMismatch.IsValid).IsFalse();
        await Assert.That(tagMismatch.Diagnostic).IsEqualTo("artifact_manifest_tag_mismatch");

        ReleaseArtifactSealResult shortCommit = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            tagMismatchFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid[..12]));
        await Assert.That(shortCommit.IsValid).IsFalse();
        await Assert.That(shortCommit.Diagnostic).IsEqualTo("artifact_manifest_commit_invalid");
    }

    [Test]
    public async Task RejectsLeakedIdentityInNotesContextVersionMismatchAndEmptyFiles()
    {
        using var leakFixture = new ArtifactSealFixture();
        await File.WriteAllTextAsync(
            Path.Combine(leakFixture.ArtifactDirectory, "release-notes.md"),
            "# Release 1.1.0\n\nContact maintainer@example.org for details.\n");

        ReleaseArtifactSealResult leaked = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            leakFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(leaked.IsValid).IsFalse();
        await Assert.That(leaked.Diagnostic).IsEqualTo("rendered_notes_identity_or_provider");

        using var contextMismatchFixture = new ArtifactSealFixture();
        await File.WriteAllTextAsync(
            Path.Combine(contextMismatchFixture.ArtifactDirectory, "release-context.v1.json"),
            """{"schemaVersion":1,"release":{"version":"9.9.9"}}""" + "\n");

        ReleaseArtifactSealResult mismatchedContext = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            contextMismatchFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(mismatchedContext.IsValid).IsFalse();
        await Assert.That(mismatchedContext.Diagnostic).IsEqualTo("artifact_manifest_context_invalid");

        using var emptyFixture = new ArtifactSealFixture();
        await File.WriteAllBytesAsync(Path.Combine(emptyFixture.ArtifactDirectory, "contract-diff.v1.json"), []);

        ReleaseArtifactSealResult emptyResult = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            emptyFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(emptyResult.IsValid).IsFalse();
        await Assert.That(emptyResult.Diagnostic).IsEqualTo("artifact_manifest_file_size_invalid");
    }

    internal sealed class ArtifactSealFixture : IDisposable
    {
        public ArtifactSealFixture(string version = SampleVersion, string? commitOid = null)
        {
            string oid = commitOid ?? SampleCommitOid;
            Root = Path.Combine(Path.GetTempPath(), $"islamu-seal-{Guid.NewGuid():N}");
            ArtifactDirectory = Path.Combine(Root, "artifacts");
            Directory.CreateDirectory(ArtifactDirectory);

            File.WriteAllText(
                Path.Combine(ArtifactDirectory, "contract-diff.v1.json"),
                $$"""
                {
                  "baseOid": "{{new string('0', 40)}}",
                  "changes": [],
                  "evaluatedFamilies": ["configuration", "migrations", "openapi"],
                  "headOid": "{{oid}}",
                  "schemaVersion": 1,
                  "summary": {
                    "additive": 0,
                    "breaking": 0,
                    "reviewRequired": 0,
                    "total": 0
                  }
                }
                """ + "\n",
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(ArtifactDirectory, "release-context.v1.json"),
                $$"""
                {
                  "changes": [],
                  "evidence": {
                    "baseStableOid": "{{new string('0', 40)}}",
                    "objects": [],
                    "previousPublishedOid": "{{new string('0', 40)}}"
                  },
                  "release": {
                    "version": "{{version}}"
                  },
                  "schemaVersion": 1
                }
                """ + "\n",
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(ArtifactDirectory, "release-images.v1.json"),
                ReleaseImageManifestTests.ValidManifestJson(version, oid) + "\n",
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(ArtifactDirectory, "release-notes.md"),
                $"# Release {version}\n\n## Community Admin\n\n- **events**: Add schedule export (`{oid[..12]}`)\n",
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(ArtifactDirectory, "release-rendering-context.v1.json"),
                $$"""
                [
                  {
                    "commits": [],
                    "extra": {
                      "version": "{{version}}"
                    },
                    "version": "v{{version}}"
                  }
                ]
                """ + "\n",
                new UTF8Encoding(false));
        }

        public string Root { get; }
        public string ArtifactDirectory { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
