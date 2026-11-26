using System.Text;
using ISLAMU.ReleaseEngineering;
using TUnit.Assertions.Enums;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ReleaseArtifactManifestTests
{
    private const string SampleVersion = "1.1.0";
    private const string SampleTag = "v1.1.0";
    private static readonly string SampleCommitOid = new('a', 40);

    // Independently computed with GNU sha256sum against ArtifactSealFixture's fixed UTF-8 LF fixture bytes.
    private const string ExpectedContractDiffSha256 = "1fdccf02cfd6ba6705f4055eaa700b6d4ab75c7ad06968558251e6a301eb2020";
    private const string ExpectedReleaseContextSha256 = "6209e265e18d3f4de6244e4d039b5298433a4698d328e1c050451c16e3ad0a40";
    private const string ExpectedReleaseImagesSha256 = "074ce75e29578501033433d557b54f3512eb1319c6001bd29581dc2939002170";
    private const string ExpectedReleaseNotesSha256 = "1fc166c3489b0d072de3a5957b7f8e54e5dcd91b2b6b0bd8a138db31ef07dbfb";
    private const string ExpectedRenderingContextSha256 = "c1b5eb41bd936be5c9936a54cfcb6de7618ce09e623e68785dcc4172e9a4a354";

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
                ReleaseArtifactManifest.ContractDiffFileName,
                ReleaseArtifactManifest.ReleaseContextFileName,
                ReleaseArtifactManifest.ReleaseImagesFileName,
                ReleaseArtifactManifest.ReleaseNotesFileName,
                ReleaseArtifactManifest.RenderingContextFileName,
            ],
            CollectionOrdering.Matching);
        await Assert.That(manifest.Images).Count().IsEqualTo(4);
        await Assert.That(manifest.Images.Select(i => i.ImageName).ToArray())
            .IsEquivalentTo(["api", "blazor", "migration", "standalone"], CollectionOrdering.Matching);
        await Assert.That(manifest.Images.Sum(i => i.CanonicalReferences.Count)).IsEqualTo(8);
        foreach (IReadOnlyList<string> references in manifest.Images.Select(image => image.CanonicalReferences))
        {
            await Assert.That(references.Any(r => r.StartsWith("atcr.io/", StringComparison.Ordinal))).IsTrue();
            await Assert.That(references.Any(r => r.StartsWith("ghcr.io/", StringComparison.Ordinal))).IsTrue();
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
        await Assert.That(lines[0]).IsEqualTo($"{ExpectedContractDiffSha256}  {ReleaseArtifactManifest.ContractDiffFileName}");
        await Assert.That(lines[1]).EndsWith($"  {ReleaseArtifactManifest.ManifestFileName}");
        await Assert.That(lines[1].Split("  ", StringSplitOptions.None)[0]).Length().IsEqualTo(64);
        await Assert.That(lines[2]).IsEqualTo($"{ExpectedReleaseContextSha256}  {ReleaseArtifactManifest.ReleaseContextFileName}");
        await Assert.That(lines[3]).IsEqualTo($"{ExpectedReleaseImagesSha256}  {ReleaseArtifactManifest.ReleaseImagesFileName}");
        await Assert.That(lines[4]).IsEqualTo($"{ExpectedReleaseNotesSha256}  {ReleaseArtifactManifest.ReleaseNotesFileName}");
        await Assert.That(lines[5]).IsEqualTo($"{ExpectedRenderingContextSha256}  {ReleaseArtifactManifest.RenderingContextFileName}");
    }

    [Test]
    public async Task SealsSingleRegistryGhcrOnlyManifestDeterministically()
    {
        using var fixture = new ArtifactSealFixture();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ReleaseImagesFileName),
            ReleaseImageManifestTests.ValidSingleRegistryManifestJson("ghcr.io", "islamu-ngo", SampleVersion, SampleCommitOid) + "\n");

        ReleaseArtifactSealResult result = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "valid ghcr-only seal");
        await Assert.That(result.Manifest!.Images).Count().IsEqualTo(4);
        await Assert.That(result.Manifest.Images.Sum(i => i.CanonicalReferences.Count)).IsEqualTo(4);
        foreach (IReadOnlyList<string> references in result.Manifest.Images.Select(image => image.CanonicalReferences))
        {
            await Assert.That(references).Count().IsEqualTo(1);
            await Assert.That(references[0]).StartsWith("ghcr.io/");
        }
    }

    [Test]
    public async Task SealsOciDoubleUnderscoreAndConsecutiveHyphenNamespacesAndVerifiesSealedDirectory()
    {
        using var fixture = new ArtifactSealFixture();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ReleaseImagesFileName),
            ReleaseImageManifestTests.ValidManifestJson(
                SampleVersion,
                SampleCommitOid,
                secondaryRegistry: "registry.example.com",
                secondaryOwner: "team__events/my--org") + "\n");

        ReleaseArtifactSealResult sealedResult = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(sealedResult.IsValid).IsTrue().Because(sealedResult.Diagnostic ?? "valid OCI separators");

        ReleaseArtifactVerificationResult verifiedWithoutBundle = ReleaseArtifactManifest.VerifySealedDirectory(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            requireSigstoreBundle: false);
        await Assert.That(verifiedWithoutBundle.IsValid).IsTrue().Because(verifiedWithoutBundle.Diagnostic ?? "valid sealed directory");

        ReleaseArtifactVerificationResult missingBundle = ReleaseArtifactManifest.VerifySealedDirectory(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            requireSigstoreBundle: true);
        await Assert.That(missingBundle.IsValid).IsFalse();
        await Assert.That(missingBundle.Diagnostic).IsEqualTo($"artifact_manifest_missing_file:{ReleaseArtifactManifest.SigstoreBundleFileName}");

        await File.WriteAllTextAsync(
            Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.SigstoreBundleFileName),
            """{"mediaType":"application/vnd.dev.sigstore.bundle.v0.3+json"}""" + "\n");
        ReleaseArtifactVerificationResult verifiedWithBundle = ReleaseArtifactManifest.VerifySealedDirectory(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            requireSigstoreBundle: true);
        await Assert.That(verifiedWithBundle.IsValid).IsTrue().Because(verifiedWithBundle.Diagnostic ?? "valid with bundle");

        await File.AppendAllTextAsync(
            Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ReleaseNotesFileName),
            "\nTampered line.\n");
        ReleaseArtifactVerificationResult tampered = ReleaseArtifactManifest.VerifySealedDirectory(
            fixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            requireSigstoreBundle: true);
        await Assert.That(tampered.IsValid).IsFalse();
        await Assert.That(tampered.Diagnostic).IsEqualTo("artifact_manifest_verification_mismatch");
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
            .IsEquivalentTo(await File.ReadAllBytesAsync(Path.Combine(outputB, ReleaseArtifactManifest.ChecksumsFileName)), CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(outputA, ReleaseArtifactManifest.ManifestFileName)))
            .IsEquivalentTo(await File.ReadAllBytesAsync(Path.Combine(outputB, ReleaseArtifactManifest.ManifestFileName)), CollectionOrdering.Matching);
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
        await Assert.That(await File.ReadAllBytesAsync(existingChecksums)).IsEquivalentTo(sentinelBytes, CollectionOrdering.Matching);
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
            .IsEquivalentTo(sealedManifestBytes, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(ReleaseArtifactManifest.ContractDiffFileName)]
    [Arguments(ReleaseArtifactManifest.ReleaseContextFileName)]
    [Arguments(ReleaseArtifactManifest.ReleaseImagesFileName)]
    [Arguments(ReleaseArtifactManifest.ReleaseNotesFileName)]
    [Arguments(ReleaseArtifactManifest.RenderingContextFileName)]
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
    public async Task RejectsSymlinksPathTraversalAndNestedOutputDirectoriesIncludingDotPrefixedChildren()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new ArtifactSealFixture();
        string outsideFile = Path.Combine(fixture.Root, "outside-notes.md");
        await File.WriteAllTextAsync(outsideFile, "# Release 1.1.0\n\nValid notes.\n");
        string notesPath = Path.Combine(fixture.ArtifactDirectory, ReleaseArtifactManifest.ReleaseNotesFileName);
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

        string doubleDotNestedOutput = Path.Combine(dirLinkFixture.ArtifactDirectory, "..sealed");
        ReleaseArtifactSealResult doubleDotNestedOut = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            dirLinkFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            doubleDotNestedOutput));
        await Assert.That(doubleDotNestedOut.IsValid).IsFalse();
        await Assert.That(doubleDotNestedOut.Diagnostic).IsEqualTo("artifact_manifest_output_invalid");

        string doubleDotSiblingOutput = Path.Combine(dirLinkFixture.Root, "..sealed");
        ReleaseArtifactSealResult doubleDotSiblingOut = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            dirLinkFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid,
            doubleDotSiblingOutput));
        await Assert.That(doubleDotSiblingOut.IsValid).IsTrue().Because(doubleDotSiblingOut.Diagnostic ?? "valid sibling output");

        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("../escape.json")).IsFalse();
        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("/etc/passwd")).IsFalse();
        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("nested\\file.json")).IsFalse();
        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath("nested/file.json")).IsFalse();
        await Assert.That(ReleaseArtifactManifest.IsSafeRelativeArtifactPath(ReleaseArtifactManifest.ReleaseNotesFileName)).IsTrue();
    }

    [Test]
    public async Task RejectsTaggedImageReferencesSingleRegistryCoverageAndMismatchedMetadata()
    {
        using var taggedFixture = new ArtifactSealFixture();
        string badTaggedJson = ReleaseImageManifestTests.ValidManifestJson(SampleVersion, SampleCommitOid)
            .Replace($"@sha256:{new string('1', 64)}", ":v1.1.0", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(taggedFixture.ArtifactDirectory, ReleaseArtifactManifest.ReleaseImagesFileName), badTaggedJson);

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
        await File.WriteAllTextAsync(Path.Combine(singleRegistryFixture.ArtifactDirectory, ReleaseArtifactManifest.ReleaseImagesFileName), singleRegistryJson);

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
            Path.Combine(leakFixture.ArtifactDirectory, ReleaseArtifactManifest.ReleaseNotesFileName),
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
            Path.Combine(contextMismatchFixture.ArtifactDirectory, ReleaseArtifactManifest.ReleaseContextFileName),
            """{"schemaVersion":1,"release":{"version":"9.9.9"}}""" + "\n");

        ReleaseArtifactSealResult mismatchedContext = ReleaseArtifactManifest.Seal(new ReleaseArtifactSealRequest(
            contextMismatchFixture.ArtifactDirectory,
            SampleVersion,
            SampleTag,
            SampleCommitOid));
        await Assert.That(mismatchedContext.IsValid).IsFalse();
        await Assert.That(mismatchedContext.Diagnostic).IsEqualTo("artifact_manifest_context_invalid");

        using var emptyFixture = new ArtifactSealFixture();
        await File.WriteAllBytesAsync(Path.Combine(emptyFixture.ArtifactDirectory, ReleaseArtifactManifest.ContractDiffFileName), []);

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
                Path.Combine(ArtifactDirectory, ReleaseArtifactManifest.ContractDiffFileName),
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
                Path.Combine(ArtifactDirectory, ReleaseArtifactManifest.ReleaseContextFileName),
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
                Path.Combine(ArtifactDirectory, ReleaseArtifactManifest.ReleaseImagesFileName),
                ReleaseImageManifestTests.ValidManifestJson(version, oid) + "\n",
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(ArtifactDirectory, ReleaseArtifactManifest.ReleaseNotesFileName),
                $"# Release {version}\n\n## Community Admin\n\n- **events**: Add schedule export (`{oid[..12]}`)\n",
                new UTF8Encoding(false));

            File.WriteAllText(
                Path.Combine(ArtifactDirectory, ReleaseArtifactManifest.RenderingContextFileName),
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
