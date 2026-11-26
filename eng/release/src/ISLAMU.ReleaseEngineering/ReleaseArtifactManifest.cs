using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record ReleaseArtifactSealRequest(
    string ArtifactDirectory,
    string Version,
    string TagName,
    string SourceCommitOid,
    string? OutputDirectory = null);

public sealed record SealedArtifactFileEntry(
    string RelativePath,
    string Sha256,
    long Bytes);

public sealed record ReleaseArtifactManifestDocument(
    int SchemaVersion,
    string Version,
    string Tag,
    string SourceCommitOid,
    IReadOnlyList<SealedArtifactFileEntry> Files,
    IReadOnlyList<EnrichedImageReference> Images);

public sealed record ReleaseArtifactSealResult(
    bool IsValid,
    ReleaseArtifactManifestDocument? Manifest,
    string? ManifestJson,
    string? ChecksumsContent,
    string? Diagnostic);

public sealed record ReleaseArtifactVerificationResult(
    bool IsValid,
    ReleaseArtifactManifestDocument? Manifest,
    byte[]? ContextBytes,
    byte[]? NotesBytes,
    string? ManifestSha256,
    string? Diagnostic);

/// <summary>
/// Validates and seals the exact five release artifacts (<c>contract-diff.v1.json</c>,
/// <c>release-context.v1.json</c>, <c>release-images.v1.json</c>, <c>release-notes.md</c>,
/// and <c>release-rendering-context.v1.json</c>) into canonical, deterministic
/// <c>release-artifact-manifest.v1.json</c> and <c>SHA256SUMS</c> outputs without clobbering.
/// </summary>
public static class ReleaseArtifactManifest
{
    public const string ChecksumsFileName = "SHA256SUMS";
    public const string ChecksumsSignatureFileName = "SHA256SUMS.sig";
    public const string SigstoreBundleFileName = "SHA256SUMS.sigstore.json";
    public const string ManifestFileName = "release-artifact-manifest.v1.json";
    public const string ContractDiffFileName = "contract-diff.v1.json";
    public const string ReleaseContextFileName = "release-context.v1.json";
    public const string ReleaseImagesFileName = "release-images.v1.json";
    public const string ReleaseNotesFileName = "release-notes.md";
    public const string RenderingContextFileName = "release-rendering-context.v1.json";

    private const int ExpectedSchemaVersion = 1;
    private const long MaximumArtifactFileBytes = 16L * 1024 * 1024;

    private static class Diagnostics
    {
        public const string VersionInvalid = "artifact_manifest_version_invalid";
        public const string TagMismatch = "artifact_manifest_tag_mismatch";
        public const string CommitInvalid = "artifact_manifest_commit_invalid";
        public const string DirectoryInvalid = "artifact_manifest_directory_invalid";
        public const string OutputInvalid = "artifact_manifest_output_invalid";
        public const string OutputExists = "artifact_manifest_output_exists";
        public const string SymlinkRejected = "artifact_manifest_symlink_rejected";
        public const string UnexpectedDirectory = "artifact_manifest_unexpected_directory";
        public const string PathInvalid = "artifact_manifest_path_invalid";
        public const string FileSizeInvalid = "artifact_manifest_file_size_invalid";
        public const string ContextInvalid = "artifact_manifest_context_invalid";
        public const string JsonInvalid = "artifact_manifest_json_invalid";
        public const string NormalizationFailed = "artifact_manifest_normalization_failed";
        public const string WriteFailed = "artifact_manifest_write_failed";
        public const string VerificationMismatch = "artifact_manifest_verification_mismatch";
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private static readonly Regex SemVerPattern = new(
        "^(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-(?:alpha|beta|rc)\\.(?:[1-9][0-9]*))?\\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<string> RequiredArtifactFiles { get; } =
    [
        ContractDiffFileName,
        ReleaseContextFileName,
        ReleaseImagesFileName,
        ReleaseNotesFileName,
        RenderingContextFileName,
    ];

    private static readonly HashSet<string> RequiredFileSet = new(RequiredArtifactFiles, StringComparer.Ordinal);

    public static bool IsSafeRelativeArtifactPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Contains('/', StringComparison.Ordinal) ||
            relativePath.Contains('\\', StringComparison.Ordinal) ||
            relativePath.Contains(':', StringComparison.Ordinal) ||
            relativePath is "." or ".." ||
            relativePath.Any(char.IsControl) ||
            !relativePath.IsNormalized(NormalizationForm.FormC))
        {
            return false;
        }

        return true;
    }

    public static ReleaseArtifactSealResult Seal(ReleaseArtifactSealRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? requestError = ValidateRequestAndResolvePaths(
            request,
            out string artifactDirectory,
            out string outputDirectory);
        if (requestError is not null)
        {
            return Invalid(requestError);
        }

        string? inventoryError = ValidateAndCollectArtifacts(
            artifactDirectory,
            outputDirectory,
            request,
            out SealedArtifactFileEntry[] fileEntries,
            out IReadOnlyList<EnrichedImageReference>? images);
        if (inventoryError is not null)
        {
            return Invalid(inventoryError);
        }

        var manifest = new ReleaseArtifactManifestDocument(
            ExpectedSchemaVersion,
            request.Version,
            request.TagName,
            request.SourceCommitOid,
            fileEntries,
            images!);

        string? serializeError = BuildCanonicalOutputs(
            manifest,
            out byte[] manifestBytes,
            out string manifestJson,
            out byte[] checksumBytes,
            out string checksumText);
        if (serializeError is not null)
        {
            return Invalid(serializeError);
        }

        string? writeError = WriteSealedOutputsNoClobber(outputDirectory, manifestBytes, checksumBytes);
        return writeError is not null
            ? Invalid(writeError)
            : new ReleaseArtifactSealResult(true, manifest, manifestJson, checksumText, null);
    }

    /// <summary>
    /// Verifies an existing sealed artifact directory against <c>release-artifact-manifest.v1.json</c>
    /// and <c>SHA256SUMS</c> without modifying any files. When <paramref name="allowedSignersPath"/> is
    /// provided, cryptographically authenticates the exact <c>SHA256SUMS</c> bytes via
    /// <c>SHA256SUMS.sig</c> under the <c>islamu.event.release-checksums</c> OpenSSH namespace so unsigned or
    /// re-sealed working-tree edits cannot bypass verification.
    /// </summary>
    public static ReleaseArtifactVerificationResult VerifySealedDirectory(
        string directory,
        string expectedVersion,
        string expectedTag,
        string expectedCommitOid,
        string? allowedSignersPath = null)
    {
        var request = new ReleaseArtifactSealRequest(directory, expectedVersion, expectedTag, expectedCommitOid);
        string? requestError = ValidateIdentityAndDirectory(request, out string resolvedDir);
        if (requestError is not null)
        {
            return VerificationFailed(requestError);
        }

        string? filesError = CollectSealedDirectoryFiles(resolvedDir, out Dictionary<string, string> filesByName);
        if (filesError is not null)
        {
            return VerificationFailed(filesError);
        }

        string? readError = ReadArtifactSnapshots(filesByName, out Dictionary<string, byte[]> artifactBytes);
        if (readError is not null)
        {
            return VerificationFailed(readError);
        }

        string? contentError = ValidateArtifactContents(artifactBytes, request, out IReadOnlyList<EnrichedImageReference>? images);
        if (contentError is not null)
        {
            return VerificationFailed(contentError);
        }

        SealedArtifactFileEntry[] fileEntries = HashArtifacts(artifactBytes);

        var expectedManifest = new ReleaseArtifactManifestDocument(
            ExpectedSchemaVersion,
            expectedVersion,
            expectedTag,
            expectedCommitOid,
            fileEntries,
            images!);
        string? serializeError = BuildCanonicalOutputs(
            expectedManifest,
            out byte[] expectedManifestBytes,
            out _,
            out byte[] expectedChecksumBytes,
            out _);
        if (serializeError is not null)
        {
            return VerificationFailed(serializeError);
        }

        string? matchError = VerifyPersistedSealedOutputs(
            resolvedDir,
            expectedManifestBytes,
            expectedChecksumBytes,
            allowedSignersPath);
        if (matchError is not null)
        {
            return VerificationFailed(matchError);
        }

        string manifestHash = Convert.ToHexStringLower(SHA256.HashData(expectedManifestBytes));
        return new ReleaseArtifactVerificationResult(
            true,
            expectedManifest,
            artifactBytes[ReleaseContextFileName],
            artifactBytes[ReleaseNotesFileName],
            manifestHash,
            null);
    }

    public static int RunCli(string[] args, TextWriter output, string defaultBaseDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (!TryParseCliArguments(args, defaultBaseDirectory, output, out ReleaseArtifactSealRequest? request))
        {
            return Program.UsageError;
        }

        ReleaseArtifactSealResult result = Seal(request!);
        if (!result.IsValid || result.Manifest is null)
        {
            output.WriteLine($"seal_artifacts_failed: {result.Diagnostic}");
            return Program.ToolchainRejected;
        }

        int referenceCount = result.Manifest.Images.Sum(image => image.CanonicalReferences.Count);
        string checksumsPath = Path.Combine(
            request!.OutputDirectory ?? request.ArtifactDirectory,
            ChecksumsFileName);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"sealed_artifacts: version={result.Manifest.Version} tag={result.Manifest.Tag} files={result.Manifest.Files.Count} images={result.Manifest.Images.Count} references={referenceCount} checksums={checksumsPath}"));
        return Program.Success;
    }

    private static string? ValidateRequestAndResolvePaths(
        ReleaseArtifactSealRequest request,
        out string artifactDirectory,
        out string outputDirectory)
    {
        outputDirectory = string.Empty;
        string? identityError = ValidateIdentityAndDirectory(request, out artifactDirectory);
        if (identityError is not null)
        {
            return identityError;
        }

        return ResolveOutputDirectory(request.OutputDirectory, artifactDirectory, out outputDirectory);
    }

    private static string? ValidateIdentityAndDirectory(
        ReleaseArtifactSealRequest request,
        out string artifactDirectory)
    {
        artifactDirectory = string.Empty;

        if (string.IsNullOrWhiteSpace(request.Version) || !SemVerPattern.IsMatch(request.Version))
        {
            return Diagnostics.VersionInvalid;
        }

        if (!string.Equals(request.TagName, $"v{request.Version}", StringComparison.Ordinal))
        {
            return Diagnostics.TagMismatch;
        }

        if (!GitProcessObjectReader.IsFullOid(request.SourceCommitOid))
        {
            return Diagnostics.CommitInvalid;
        }

        if (!TryResolveFullPath(request.ArtifactDirectory, out artifactDirectory) ||
            !Directory.Exists(artifactDirectory) ||
            HasReparsePointInExistingPath(artifactDirectory))
        {
            return Diagnostics.DirectoryInvalid;
        }

        return null;
    }

    private static string? CollectSealedDirectoryFiles(string directory, out Dictionary<string, string> filesByName)
    {
        filesByName = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (string required in RequiredArtifactFiles)
            {
                string fullPath = Path.Combine(directory, required);
                if (!File.Exists(fullPath))
                {
                    return $"artifact_manifest_missing_file:{required}";
                }

                FileAttributes attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return Diagnostics.SymlinkRejected;
                }

                if (new FileInfo(fullPath).Length > MaximumArtifactFileBytes)
                {
                    return Diagnostics.FileSizeInvalid;
                }

                filesByName[required] = fullPath;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Diagnostics.DirectoryInvalid;
        }

        return null;
    }

    private static string? VerifyPersistedSealedOutputs(
        string directory,
        byte[] expectedManifestBytes,
        byte[] expectedChecksumBytes,
        string? allowedSignersPath)
    {
        string manifestPath = Path.Combine(directory, ManifestFileName);
        string checksumsPath = Path.Combine(directory, ChecksumsFileName);
        if (!File.Exists(manifestPath))
        {
            return $"artifact_manifest_missing_file:{ManifestFileName}";
        }

        if (!File.Exists(checksumsPath))
        {
            return $"artifact_manifest_missing_file:{ChecksumsFileName}";
        }

        if (HasReparsePointInExistingPath(manifestPath) || HasReparsePointInExistingPath(checksumsPath))
        {
            return Diagnostics.SymlinkRejected;
        }

        try
        {
            if (new FileInfo(manifestPath).Length > MaximumArtifactFileBytes ||
                new FileInfo(checksumsPath).Length > MaximumArtifactFileBytes)
            {
                return Diagnostics.FileSizeInvalid;
            }

            byte[] actualManifestBytes = File.ReadAllBytes(manifestPath);
            byte[] actualChecksumBytes = File.ReadAllBytes(checksumsPath);
            if (!actualManifestBytes.AsSpan().SequenceEqual(expectedManifestBytes) ||
                !actualChecksumBytes.AsSpan().SequenceEqual(expectedChecksumBytes))
            {
                return Diagnostics.VerificationMismatch;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Diagnostics.DirectoryInvalid;
        }

        if (allowedSignersPath is null)
        {
            return null;
        }

        string signaturePath = Path.Combine(directory, ChecksumsSignatureFileName);
        SshBlobSignatureVerificationResult signatureResult = SshBlobSignatureVerifier.Verify(
            expectedChecksumBytes,
            signaturePath,
            allowedSignersPath,
            SshBlobSignatureVerifier.ReleaseChecksumsNamespace);
        return signatureResult.IsValid ? null : signatureResult.Diagnostic ?? "artifact_manifest_checksums_signature_invalid";
    }

    private static string? ResolveOutputDirectory(
        string? rawOutputDirectory,
        string artifactDirectory,
        out string outputDirectory)
    {
        if (rawOutputDirectory is null)
        {
            outputDirectory = artifactDirectory;
            return null;
        }

        if (!TryResolveFullPath(rawOutputDirectory, out outputDirectory) ||
            File.Exists(outputDirectory) ||
            HasReparsePointInExistingPath(outputDirectory))
        {
            return Diagnostics.OutputInvalid;
        }

        bool isSameDirectory = string.Equals(outputDirectory, artifactDirectory, PathComparison);
        if (!isSameDirectory && IsStrictDescendant(artifactDirectory, outputDirectory))
        {
            return Diagnostics.OutputInvalid;
        }

        return null;
    }

    private static string? ValidateAndCollectArtifacts(
        string artifactDirectory,
        string outputDirectory,
        ReleaseArtifactSealRequest request,
        out SealedArtifactFileEntry[] fileEntries,
        out IReadOnlyList<EnrichedImageReference>? images)
    {
        fileEntries = [];
        images = null;

        if (ExistingSealedOutputPresent(outputDirectory))
        {
            return Diagnostics.OutputExists;
        }

        string? scanError = EnumerateExactInventory(artifactDirectory, out Dictionary<string, string> filesByName);
        if (scanError is not null)
        {
            return scanError;
        }

        string? readError = ReadArtifactSnapshots(filesByName, out Dictionary<string, byte[]> artifactBytes);
        if (readError is not null)
        {
            return readError;
        }

        string? contentError = ValidateArtifactContents(artifactBytes, request, out images);
        if (contentError is not null)
        {
            return contentError;
        }

        fileEntries = HashArtifacts(artifactBytes);
        return null;
    }

    private static bool ExistingSealedOutputPresent(string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return false;
        }

        return File.Exists(Path.Combine(outputDirectory, ChecksumsFileName)) ||
            File.Exists(Path.Combine(outputDirectory, ManifestFileName));
    }

    private static string? EnumerateExactInventory(string artifactDirectory, out Dictionary<string, string> filesByName)
    {
        filesByName = new Dictionary<string, string>(StringComparer.Ordinal);
        var caseInsensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string rootPrefix = Path.TrimEndingDirectorySeparator(artifactDirectory) + Path.DirectorySeparatorChar;

        try
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(artifactDirectory))
            {
                string? entryError = ValidateSingleDirectoryEntry(entry, rootPrefix, filesByName, caseInsensitiveNames);
                if (entryError is not null)
                {
                    return entryError;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Diagnostics.DirectoryInvalid;
        }

        Dictionary<string, string> discovered = filesByName;
        string? missingRequired = RequiredArtifactFiles.FirstOrDefault(required => !discovered.ContainsKey(required));
        return missingRequired is not null ? $"artifact_manifest_missing_file:{missingRequired}" : null;
    }

    private static string? ValidateSingleDirectoryEntry(
        string entry,
        string rootPrefix,
        Dictionary<string, string> filesByName,
        HashSet<string> caseInsensitiveNames)
    {
        string fullPath = Path.GetFullPath(entry);
        string fileName = Path.GetFileName(fullPath);

        if (!fullPath.StartsWith(rootPrefix, PathComparison) ||
            !IsSafeRelativeArtifactPath(fileName) ||
            !caseInsensitiveNames.Add(fileName))
        {
            return Diagnostics.PathInvalid;
        }

        FileAttributes attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return Diagnostics.SymlinkRejected;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            return Diagnostics.UnexpectedDirectory;
        }

        if (!RequiredFileSet.Contains(fileName))
        {
            return $"artifact_manifest_unexpected_file:{fileName}";
        }

        filesByName[fileName] = fullPath;
        return null;
    }

    private static string? ReadArtifactSnapshots(
        Dictionary<string, string> filesByName,
        out Dictionary<string, byte[]> artifactBytes)
    {
        artifactBytes = new Dictionary<string, byte[]>(RequiredArtifactFiles.Count, StringComparer.Ordinal);
        try
        {
            foreach (string required in RequiredArtifactFiles)
            {
                string fullPath = filesByName[required];
                var info = new FileInfo(fullPath);
                if (info.Length <= 0 || info.Length > MaximumArtifactFileBytes)
                {
                    return Diagnostics.FileSizeInvalid;
                }

                byte[] bytes = File.ReadAllBytes(fullPath);
                if (bytes.Length == 0 || bytes.LongLength > MaximumArtifactFileBytes)
                {
                    return Diagnostics.FileSizeInvalid;
                }

                artifactBytes[required] = bytes;
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Diagnostics.DirectoryInvalid;
        }
    }

    private static string? ValidateArtifactContents(
        Dictionary<string, byte[]> artifactBytes,
        ReleaseArtifactSealRequest request,
        out IReadOnlyList<EnrichedImageReference>? images)
    {
        images = null;

        ReleaseImageManifestLoadResult imageResult = ParseImageManifestBytes(
            artifactBytes[ReleaseImagesFileName],
            request.Version,
            request.SourceCommitOid);
        if (!imageResult.IsValid || imageResult.Manifest is null)
        {
            return imageResult.Diagnostic ?? "image_manifest_invalid";
        }

        string? notesError = ValidateNotesBytes(artifactBytes[ReleaseNotesFileName]);
        if (notesError is not null)
        {
            return notesError;
        }

        string? contextError = ValidateReleaseContextBytes(artifactBytes[ReleaseContextFileName], request.Version);
        if (contextError is not null)
        {
            return contextError;
        }

        if (!IsValidJsonBytes(artifactBytes[ContractDiffFileName], JsonValueKind.Object) ||
            !IsValidJsonBytes(artifactBytes[RenderingContextFileName], JsonValueKind.Array))
        {
            return Diagnostics.JsonInvalid;
        }

        images = imageResult.Manifest.Images;
        return null;
    }

    private static ReleaseImageManifestLoadResult ParseImageManifestBytes(
        byte[] bytes,
        string expectedVersion,
        string expectedCommitOid)
    {
        try
        {
            string json = StrictUtf8.GetString(bytes);
            return ReleaseImageManifest.Parse(json, expectedVersion, expectedCommitOid: expectedCommitOid);
        }
        catch (DecoderFallbackException)
        {
            return new ReleaseImageManifestLoadResult(false, null, "image_manifest_invalid");
        }
    }

    private static string? ValidateNotesBytes(byte[] notesBytes)
    {
        try
        {
            string markdown = StrictUtf8.GetString(notesBytes);
            ArtifactPolicyResult validation = ReleaseArtifactPolicy.ValidateRenderedReleaseNotes(markdown);
            if (!validation.IsValid)
            {
                return validation.Diagnostics.Count > 0 ? validation.Diagnostics[0] : "rendered_notes_invalid";
            }

            return null;
        }
        catch (DecoderFallbackException)
        {
            return "rendered_notes_invalid_unicode";
        }
    }

    private static string? ValidateReleaseContextBytes(byte[] contextBytes, string expectedVersion)
    {
        try
        {
            string json = StrictUtf8.GetString(contextBytes);
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("release", out JsonElement releaseEl) ||
                releaseEl.ValueKind != JsonValueKind.Object ||
                !releaseEl.TryGetProperty("version", out JsonElement versionEl) ||
                versionEl.ValueKind != JsonValueKind.String ||
                !string.Equals(versionEl.GetString(), expectedVersion, StringComparison.Ordinal))
            {
                return Diagnostics.ContextInvalid;
            }

            return null;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            return Diagnostics.ContextInvalid;
        }
    }

    private static bool IsValidJsonBytes(byte[] fileBytes, JsonValueKind expectedKind)
    {
        try
        {
            string json = StrictUtf8.GetString(fileBytes);
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            return doc.RootElement.ValueKind == expectedKind;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static SealedArtifactFileEntry[] HashArtifacts(Dictionary<string, byte[]> artifactBytes)
    {
        return RequiredArtifactFiles
            .Select(name =>
            {
                byte[] bytes = artifactBytes[name];
                string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                return new SealedArtifactFileEntry(name, hash, bytes.LongLength);
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? BuildCanonicalOutputs(
        ReleaseArtifactManifestDocument manifest,
        out byte[] manifestBytes,
        out string manifestJson,
        out byte[] checksumBytes,
        out string checksumText)
    {
        manifestBytes = [];
        manifestJson = string.Empty;
        checksumBytes = [];
        checksumText = string.Empty;

        string rawJson = JsonSerializer.Serialize(manifest, SerializerOptions);
        ArtifactPolicyResult normalized = ReleaseArtifactPolicy.NormalizeJson(rawJson);
        if (!normalized.IsValid || normalized.Bytes is null)
        {
            return normalized.Diagnostics.Count > 0 ? normalized.Diagnostics[0] : Diagnostics.NormalizationFailed;
        }

        manifestBytes = normalized.Bytes;
        manifestJson = StrictUtf8.GetString(manifestBytes);
        string manifestHash = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));

        (string RelativePath, string Sha256)[] checksumRows = manifest.Files
            .Select(file => (RelativePath: file.RelativePath, Sha256: file.Sha256))
            .Append((RelativePath: ManifestFileName, Sha256: manifestHash))
            .OrderBy(row => row.RelativePath, StringComparer.Ordinal)
            .ToArray();

        checksumText = string.Join('\n', checksumRows.Select(row => $"{row.Sha256}  {row.RelativePath}")) + "\n";
        checksumBytes = StrictUtf8.GetBytes(checksumText);
        return null;
    }

    private static string? WriteSealedOutputsNoClobber(
        string outputDirectory,
        byte[] manifestBytes,
        byte[] checksumBytes)
    {
        string manifestPath = Path.Combine(outputDirectory, ManifestFileName);
        string checksumsPath = Path.Combine(outputDirectory, ChecksumsFileName);
        string manifestTemp = Path.Combine(outputDirectory, $".{ManifestFileName}.{Guid.NewGuid():N}.tmp");
        string checksumsTemp = Path.Combine(outputDirectory, $".{ChecksumsFileName}.{Guid.NewGuid():N}.tmp");
        bool manifestMoved = false;

        try
        {
            Directory.CreateDirectory(outputDirectory);
            if (HasReparsePointInExistingPath(outputDirectory))
            {
                return Diagnostics.OutputInvalid;
            }

            if (File.Exists(manifestPath) || File.Exists(checksumsPath))
            {
                return Diagnostics.OutputExists;
            }

            File.WriteAllBytes(manifestTemp, manifestBytes);
            File.WriteAllBytes(checksumsTemp, checksumBytes);
            File.Move(manifestTemp, manifestPath, overwrite: false);
            manifestMoved = true;
            File.Move(checksumsTemp, checksumsPath, overwrite: false);
            return null;
        }
        catch (IOException)
        {
            RollbackPartialManifest(manifestMoved, manifestPath);
            return File.Exists(manifestPath) || File.Exists(checksumsPath)
                ? Diagnostics.OutputExists
                : Diagnostics.WriteFailed;
        }
        catch (UnauthorizedAccessException)
        {
            RollbackPartialManifest(manifestMoved, manifestPath);
            return Diagnostics.WriteFailed;
        }
        finally
        {
            DeleteIfExists(manifestTemp);
            DeleteIfExists(checksumsTemp);
        }
    }

    private static void RollbackPartialManifest(bool manifestMoved, string manifestPath)
    {
        if (manifestMoved)
        {
            DeleteIfExists(manifestPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of temporary staging files.
        }
    }

    private sealed class CliOptionValues
    {
        public string? Directory { get; set; }
        public string? Version { get; set; }
        public string? TagName { get; set; }
        public string? CommitOid { get; set; }
        public string? OutputDir { get; set; }

        public bool HasRequiredValues =>
            !string.IsNullOrWhiteSpace(Directory) &&
            !string.IsNullOrWhiteSpace(Version) &&
            !string.IsNullOrWhiteSpace(TagName) &&
            !string.IsNullOrWhiteSpace(CommitOid);
    }

    private static bool TryParseCliArguments(
        string[] args,
        string defaultBaseDirectory,
        TextWriter output,
        out ReleaseArtifactSealRequest? request)
    {
        request = null;
        var values = new CliOptionValues();
        var reader = new CliArgumentReader(args);
        while (reader.TryReadNext(out string token))
        {
            if (!TryApplyCliOption(token, ref reader, values))
            {
                return WriteCliUsage(output);
            }
        }

        if (!values.HasRequiredValues)
        {
            return WriteCliUsage(output);
        }

        string resolvedDir = ResolveRelativeToBase(defaultBaseDirectory, values.Directory!);
        string? resolvedOut = values.OutputDir is null ? null : ResolveRelativeToBase(defaultBaseDirectory, values.OutputDir);

        request = new ReleaseArtifactSealRequest(resolvedDir, values.Version!, values.TagName!, values.CommitOid!, resolvedOut);
        return true;
    }

    private static bool TryApplyCliOption(string token, ref CliArgumentReader reader, CliOptionValues values)
    {
        switch (token)
        {
            case "--directory":
                if (!reader.TryReadOptionValue(values.Directory, out string dirVal)) return false;
                values.Directory = dirVal;
                return true;
            case "--version":
                if (!reader.TryReadOptionValue(values.Version, out string verVal)) return false;
                values.Version = verVal;
                return true;
            case "--tag":
                if (!reader.TryReadOptionValue(values.TagName, out string tagVal)) return false;
                values.TagName = tagVal;
                return true;
            case "--commit":
                if (!reader.TryReadOptionValue(values.CommitOid, out string commitVal)) return false;
                values.CommitOid = commitVal;
                return true;
            case "--output-dir":
                if (!reader.TryReadOptionValue(values.OutputDir, out string outVal)) return false;
                values.OutputDir = outVal;
                return true;
            default:
                return false;
        }
    }

    private static string ResolveRelativeToBase(string baseDirectory, string path) =>
        Path.IsPathFullyQualified(path) ? path : Path.Combine(baseDirectory, path);

    private static bool WriteCliUsage(TextWriter output)
    {
        output.WriteLine("invalid_arguments: seal-artifacts requires --directory <path> --version <version> --tag <tag-name> --commit <oid> [--output-dir <path>]");
        return false;
    }

    private static bool TryResolveFullPath(string rawPath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Any(char.IsControl))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(rawPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.IsPathFullyQualified(fullPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsStrictDescendant(string parentDirectory, string candidatePath)
    {
        string relative = Path.GetRelativePath(parentDirectory, candidatePath);
        bool escapesParent = relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
        return relative != "." && !escapesParent && !Path.IsPathRooted(relative);
    }

    private static bool HasReparsePointInExistingPath(string fullPath)
    {
        string? current = fullPath;
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return false;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static ReleaseArtifactSealResult Invalid(string diagnostic) =>
        new(false, null, null, null, diagnostic);

    private static ReleaseArtifactVerificationResult VerificationFailed(string diagnostic) =>
        new(false, null, null, null, null, diagnostic);
}
