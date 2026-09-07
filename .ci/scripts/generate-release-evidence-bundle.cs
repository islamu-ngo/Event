#:property RestorePackagesWithLockFile=false
#pragma warning disable CA1050 // File-based CI scripts intentionally keep helper records in the script file.

using System.Security.Cryptography;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

var artifactRoot = args.Length > 0 ? args[0] : "artifacts";
var outputDirectory = args.Length > 1 ? args[1] : "release-evidence";
const int MaximumCanonicalManifestBytes = 1_048_576;
const int MaximumArtifactCount = 4_096;
const int MaximumArtifactPathBytes = 4_096;
const int MaximumArtifactPathDepth = 32;
const long MaximumArtifactBytes = 1_073_741_824;
const long MaximumArtifactTreeBytes = 8_589_934_592;
var fullOidPattern = new Regex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
var sha256Pattern = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
var versionPattern = new Regex("^(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

if (!Directory.Exists(artifactRoot))
{
    Console.Error.WriteLine($"Artifact root does not exist: {artifactRoot}");
    return 1;
}

var artifactRootFullPath = Path.GetFullPath(artifactRoot);
var outputDirectoryFullPath = Path.GetFullPath(outputDirectory);

try
{
    if ((File.GetAttributes(artifactRootFullPath) & FileAttributes.ReparsePoint) != 0)
    {
        Console.Error.WriteLine("release_bundle_artifact_path_alias");
        return 1;
    }
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
{
    Console.Error.WriteLine("release_bundle_artifact_path_invalid");
    return 1;
}

if (StringComparer.Ordinal.Equals(artifactRootFullPath, outputDirectoryFullPath)
    || IsInsideDirectory(artifactRootFullPath, outputDirectoryFullPath))
{
    Console.Error.WriteLine("release_bundle_output_path_invalid");
    return 1;
}

if (File.Exists(outputDirectoryFullPath) || Directory.Exists(outputDirectoryFullPath))
{
    Console.Error.WriteLine("release_bundle_output_destination_invalid");
    return 1;
}

IReadOnlyList<string> sourceFiles;

try
{
    sourceFiles = EnumerateArtifactFiles(artifactRootFullPath, outputDirectoryFullPath, "release_bundle");
}
catch (InvalidOperationException exception) when (exception.Message.StartsWith("release_bundle_", StringComparison.Ordinal))
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
{
    Console.Error.WriteLine("release_bundle_artifact_path_invalid");
    return 1;
}

if (sourceFiles.Count == 0)
{
    Console.Error.WriteLine($"No evidence files found under: {artifactRoot}");
    return 1;
}

var artifacts = sourceFiles
    .Select(path => CreateEvidence(artifactRootFullPath, path))
    .ToList();
var artifactByPath = artifacts.ToDictionary(artifact => artifact.RelativePath, StringComparer.Ordinal);
var finalManifestArtifacts = artifacts
    .Where(artifact => Path.GetFileName(artifact.RelativePath).Equals("release-evidence.v1.json", StringComparison.Ordinal))
    .ToList();

if (finalManifestArtifacts.Count == 0)
{
    Console.Error.WriteLine("release_bundle_final_manifest_missing: expected exactly one release-evidence.v1.json under artifact root");
    return 1;
}

if (finalManifestArtifacts.Count > 1)
{
    Console.Error.WriteLine("release_bundle_final_manifest_duplicate: expected exactly one release-evidence.v1.json under artifact root");
    return 1;
}

ReleaseIdentity releaseIdentity;
try
{
    releaseIdentity = ReadReleaseIdentity(
        Path.Combine(artifactRootFullPath, finalManifestArtifacts[0].RelativePath.Replace('/', Path.DirectorySeparatorChar)),
        finalManifestArtifacts[0],
        artifactByPath,
        fullOidPattern,
        sha256Pattern,
        versionPattern);
    VerifyExplicitInput("RELEASE_VERSION", releaseIdentity.Version, "release_bundle_version_mismatch");
    VerifyExplicitInput("GITHUB_SHA", releaseIdentity.TargetOid, "release_bundle_commit_mismatch");
    VerifyExplicitInput("GITHUB_REF", $"refs/tags/{releaseIdentity.TagName}", "release_bundle_ref_mismatch");
    VerifyExplicitInput("RELEASE_TAG_OBJECT_ID", releaseIdentity.TagObjectId, "release_bundle_tag_object_mismatch");
}
catch (InvalidOperationException exception) when (exception.Message.StartsWith("release_bundle_", StringComparison.Ordinal))
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
catch (JsonException)
{
    Console.Error.WriteLine("release_bundle_final_manifest_malformed");
    return 1;
}
catch (DecoderFallbackException)
{
    Console.Error.WriteLine("release_bundle_final_manifest_canonical_invalid");
    return 1;
}

string repository;
string runId;
string runAttempt;
string claStatus;
try
{
    repository = GetMetadataEnv("GITHUB_REPOSITORY");
    runId = GetMetadataEnv("GITHUB_RUN_ID");
    runAttempt = GetMetadataEnv("GITHUB_RUN_ATTEMPT");
    claStatus = GetMetadataEnv("CLA_STATUS");
}
catch (InvalidOperationException exception) when (exception.Message.StartsWith("release_bundle_", StringComparison.Ordinal))
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

var bundle = new ReleaseEvidenceBundle(
    GeneratedAtUtc: DateTimeOffset.UtcNow,
    ArtifactRoot: artifactRoot,
    Repository: repository,
    Ref: GetEnv("GITHUB_REF"),
    CommitSha: releaseIdentity.TargetOid,
    RunId: runId,
    RunAttempt: runAttempt,
    ReleaseVersion: releaseIdentity.Version,
    ClaStatus: claStatus,
    ReleaseIdentity: releaseIdentity,
    Notes: "Generated from retained CI/CD artifacts. Copy or attach this bundle to long-lived release evidence before GitHub Actions artifacts expire.",
    Artifacts: artifacts);

string stagingDirectory = outputDirectoryFullPath + $".tmp-{Guid.NewGuid():N}";
string jsonPath = Path.Combine(outputDirectoryFullPath, "release-evidence.json");
string markdownPath = Path.Combine(outputDirectoryFullPath, "release-evidence.md");
string releaseNotesPath = Path.Combine(outputDirectoryFullPath, "release-evidence-release-notes.md");
string checksumPath = Path.Combine(outputDirectoryFullPath, "release-evidence-checksums.sha256");
try
{
    Directory.CreateDirectory(stagingDirectory);
    WriteJson(Path.Combine(stagingDirectory, Path.GetFileName(jsonPath)), bundle);
    File.WriteAllText(Path.Combine(stagingDirectory, Path.GetFileName(markdownPath)), BuildMarkdown(bundle), Encoding.UTF8);
    File.WriteAllText(Path.Combine(stagingDirectory, Path.GetFileName(releaseNotesPath)), BuildReleaseNotesEvidence(bundle), Encoding.UTF8);
    if (RunChecksumWriter(artifactRootFullPath, Path.Combine(stagingDirectory, Path.GetFileName(checksumPath))) != 0) return 1;
    if (!PublishBundle(stagingDirectory, outputDirectoryFullPath)) return 1;
}
finally
{
    if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
}

Console.WriteLine($"Release evidence bundle generated from {artifacts.Count} artifact file(s).");
Console.WriteLine($"JSON: {jsonPath}");
Console.WriteLine($"Markdown: {markdownPath}");
Console.WriteLine($"Release notes evidence: {releaseNotesPath}");
Console.WriteLine($"Checksums: {checksumPath}");

return 0;

static ArtifactEvidence CreateEvidence(string artifactRootFullPath, string path)
{
    using var stream = File.OpenRead(path);
    var hash = SHA256.HashData(stream);
    var relativePath = ToManifestPath(artifactRootFullPath, path);

    return new ArtifactEvidence(
        RelativePath: relativePath,
        Category: ClassifyArtifact(relativePath),
        SizeBytes: new FileInfo(path).Length,
        Sha256: Convert.ToHexString(hash).ToLowerInvariant());
}

static string BuildMarkdown(ReleaseEvidenceBundle bundle)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Release Evidence Bundle");
    builder.AppendLine();
    AppendInvariantLine(builder, $"- Generated at UTC: `{bundle.GeneratedAtUtc:O}`");
    AppendInvariantLine(builder, $"- Repository: `{ValueOrUnknown(bundle.Repository)}`");
    AppendInvariantLine(builder, $"- Ref: `{ValueOrUnknown(bundle.Ref)}`");
    AppendInvariantLine(builder, $"- Commit SHA: `{ValueOrUnknown(bundle.CommitSha)}`");
    AppendInvariantLine(builder, $"- Workflow run: `{ValueOrUnknown(bundle.RunId)}` attempt `{ValueOrUnknown(bundle.RunAttempt)}`");
    AppendInvariantLine(builder, $"- Release version: `{ValueOrUnknown(bundle.ReleaseVersion)}`");
    AppendInvariantLine(builder, $"- CLA status: `{ValueOrUnknown(bundle.ClaStatus)}`");
    AppendInvariantLine(builder, $"- Canonical final manifest: `{bundle.ReleaseIdentity.ManifestPath}` `{bundle.ReleaseIdentity.ManifestSha256}`");
    AppendInvariantLine(builder, $"- Canonical tag: `{bundle.ReleaseIdentity.TagName}` object `{bundle.ReleaseIdentity.TagObjectId}` target `{bundle.ReleaseIdentity.TargetOid}`");
    builder.AppendLine();
    builder.AppendLine("## Evidence Categories");
    builder.AppendLine();
    builder.AppendLine("| Category | Files |");
    builder.AppendLine("|---|---:|");

    foreach (var group in bundle.Artifacts.GroupBy(artifact => artifact.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
    {
        AppendInvariantLine(builder, $"| {group.Key} | {group.Count()} |");
    }

    builder.AppendLine();
    builder.AppendLine("## Release Checklist Mapping");
    builder.AppendLine();
    builder.AppendLine("- `container`: image digests, immutable promotion evidence, SBOM/provenance, Trivy, attestation, and checksum evidence.");
    builder.AppendLine("- `deployment`: environment, expected image tag/digest, webhook, smoke, freeze, override, and rollback evidence.");
    builder.AppendLine("- `openapi`: OpenAPI drift and advisory breaking-change evidence.");
    builder.AppendLine("- `test-results`: TRX test results and build/analyzer logs.");
    builder.AppendLine("- `dependency`: NuGet vulnerability summaries and dependency review evidence.");
    builder.AppendLine("- `workflow-security`, `secret-scanning`, and `scorecard`: supply-chain and workflow posture evidence.");
    builder.AppendLine();
    builder.AppendLine("## Artifact Manifest");
    builder.AppendLine();
    builder.AppendLine("| Category | Path | SHA-256 | Bytes |");
    builder.AppendLine("|---|---|---|---:|");

    foreach (var artifact in bundle.Artifacts.OrderBy(artifact => artifact.Category, StringComparer.Ordinal).ThenBy(artifact => artifact.RelativePath, StringComparer.Ordinal))
    {
        AppendInvariantLine(builder, $"| {artifact.Category} | `{artifact.RelativePath}` | `{artifact.Sha256}` | {artifact.SizeBytes} |");
    }

    return builder.ToString();
}

static void WriteJson(string jsonPath, ReleaseEvidenceBundle bundle)
{
    using var stream = File.Create(jsonPath);
    using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

    writer.WriteStartObject();
    writer.WriteString("generatedAtUtc", bundle.GeneratedAtUtc);
    writer.WriteString("artifactRoot", bundle.ArtifactRoot);
    writer.WriteString("repository", bundle.Repository);
    writer.WriteString("ref", bundle.Ref);
    writer.WriteString("commitSha", bundle.CommitSha);
    writer.WriteString("runId", bundle.RunId);
    writer.WriteString("runAttempt", bundle.RunAttempt);
    writer.WriteString("releaseVersion", bundle.ReleaseVersion);
    writer.WriteString("claStatus", bundle.ClaStatus);
    writer.WritePropertyName("releaseIdentity");
    writer.WriteStartObject();
    writer.WriteString("schemaVersion", bundle.ReleaseIdentity.SchemaVersion);
    writer.WriteString("manifestPath", bundle.ReleaseIdentity.ManifestPath);
    writer.WriteString("manifestSha256", bundle.ReleaseIdentity.ManifestSha256);
    writer.WriteString("version", bundle.ReleaseIdentity.Version);
    writer.WriteString("line", bundle.ReleaseIdentity.Line);
    writer.WriteString("tagName", bundle.ReleaseIdentity.TagName);
    writer.WriteString("tagObjectId", bundle.ReleaseIdentity.TagObjectId);
    writer.WriteString("targetOid", bundle.ReleaseIdentity.TargetOid);
    writer.WriteString("candidateOid", bundle.ReleaseIdentity.CandidateOid);
    writer.WriteString("candidateManifestSha256", bundle.ReleaseIdentity.CandidateManifestSha256);
    writer.WriteString("releaseDescriptorSha256", bundle.ReleaseIdentity.ReleaseDescriptorSha256);
    writer.WriteString("releaseSummarySha256", bundle.ReleaseIdentity.ReleaseSummarySha256);
    writer.WriteString("releaseContextSha256", bundle.ReleaseIdentity.ReleaseContextSha256);
    writer.WriteString("releaseNotesSha256", bundle.ReleaseIdentity.ReleaseNotesSha256);
    writer.WriteString("trustedBundleManifestSha256", bundle.ReleaseIdentity.TrustedBundleManifestSha256);
    writer.WriteString("trustedBundlePolicySha256", bundle.ReleaseIdentity.TrustedBundlePolicySha256);
    writer.WriteString("trustedBundleConfigSha256", bundle.ReleaseIdentity.TrustedBundleConfigSha256);
    writer.WriteString("trustedBundleTrustSha256", bundle.ReleaseIdentity.TrustedBundleTrustSha256);
    writer.WriteString("trustedBundleToolchainSha256", bundle.ReleaseIdentity.TrustedBundleToolchainSha256);
    writer.WriteString("trustedBundleGitCliffSha256", bundle.ReleaseIdentity.TrustedBundleGitCliffSha256);
    writer.WriteEndObject();
    writer.WriteString("notes", bundle.Notes);
    writer.WritePropertyName("artifacts");
    writer.WriteStartArray();

    foreach (var artifact in bundle.Artifacts)
    {
        writer.WriteStartObject();
        writer.WriteString("relativePath", artifact.RelativePath);
        writer.WriteString("category", artifact.Category);
        writer.WriteNumber("sizeBytes", artifact.SizeBytes);
        writer.WriteString("sha256", artifact.Sha256);
        writer.WriteEndObject();
    }

    writer.WriteEndArray();
    writer.WriteEndObject();
}

static string BuildReleaseNotesEvidence(ReleaseEvidenceBundle bundle)
{
    var builder = new StringBuilder();
    builder.AppendLine("### CI/CD Release Evidence");
    builder.AppendLine();
    AppendInvariantLine(builder, $"- Release version: `{ValueOrUnknown(bundle.ReleaseVersion)}`");
    AppendInvariantLine(builder, $"- Commit SHA: `{ValueOrUnknown(bundle.CommitSha)}`");
    AppendInvariantLine(builder, $"- Canonical final manifest: `{bundle.ReleaseIdentity.ManifestPath}` `{bundle.ReleaseIdentity.ManifestSha256}`");
    AppendInvariantLine(builder, $"- Canonical tag object: `{bundle.ReleaseIdentity.TagObjectId}`");
    AppendInvariantLine(builder, $"- Source ref: `{ValueOrUnknown(bundle.Ref)}`");
    AppendInvariantLine(builder, $"- Workflow run: `{ValueOrUnknown(bundle.RunId)}` attempt `{ValueOrUnknown(bundle.RunAttempt)}`");
    AppendInvariantLine(builder, $"- CLA status: `{ValueOrUnknown(bundle.ClaStatus)}`");
    builder.AppendLine("- Attached evidence bundle files:");
    builder.AppendLine("  - `release-evidence.json`");
    builder.AppendLine("  - `release-evidence.md`");
    builder.AppendLine("  - `release-evidence-checksums.sha256`");
    builder.AppendLine("  - `release-evidence-release-notes.md`");
    builder.AppendLine();
    builder.AppendLine("Evidence categories included in the bundle:");
    builder.AppendLine();
    builder.AppendLine("| Category | Files | Required release review | ");
    builder.AppendLine("|---|---:|---|");

    foreach (var group in bundle.Artifacts.GroupBy(artifact => artifact.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
    {
        AppendInvariantLine(builder, $"| {group.Key} | {group.Count()} | {ReleaseReviewHint(group.Key)} |");
    }

    builder.AppendLine();
    builder.AppendLine("The attached checksum manifest is authoritative for copied evidence files. Do not rely on expiring GitHub Actions artifact links as the only release evidence source.");

    return builder.ToString();
}

static string ReleaseReviewHint(string category)
{
    return category switch
    {
        "container" => "Image digest, SBOM/provenance, scan, attestation, promotion, and checksum evidence agree.",
        "deployment" => "Environment, expected tag/digest, webhook result, smoke result, freeze state, and rollback note are present.",
        "release-identity" => "Canonical final release-evidence.v1.json owns version, tag, target, and source hashes.",
        "release-governance" => "Release descriptor, summary, context, notes, and candidate evidence hashes agree.",
        "trusted-tooling" => "Trusted bundle manifest, promotion receipt, signature, policy, config, trust, and tool evidence agree.",
        "signer-verification" => "Local SSH signer and tag verification evidence was reviewed.",
        "openapi" => "OpenAPI drift and advisory breaking-change evidence were reviewed.",
        "test-results" => "TRX and build/analyzer logs were reviewed.",
        "dependency" => "NuGet vulnerability and dependency policy evidence were reviewed.",
        "workflow-security" => "Action pin, actionlint, zizmor, cache, and deploy contract evidence were reviewed.",
        "secret-scanning" => "Secret scan SARIF/text evidence was reviewed.",
        "scorecard" => "OpenSSF Scorecard evidence was reviewed.",
        "security-tests" => "Security/Cerbos test evidence was reviewed.",
        _ => "Review and classify before publishing the release."
    };
}

static string ClassifyArtifact(string relativePath)
{
    var normalized = relativePath.ToLowerInvariant();
    var fileName = Path.GetFileName(normalized);

    if (fileName.Equals("release-evidence.v1.json", StringComparison.Ordinal))
    {
        return "release-identity";
    }

    if (fileName.Equals("release.yaml", StringComparison.Ordinal)
        || fileName.Equals("summary.md", StringComparison.Ordinal)
        || fileName.Equals("release-context.v1.json", StringComparison.Ordinal)
        || fileName.Equals("release-notes.md", StringComparison.Ordinal)
        || fileName.Equals("release-candidate.v1.json", StringComparison.Ordinal))
    {
        return "release-governance";
    }

    if (normalized.Contains("trusted-bundle/", StringComparison.Ordinal)
        || fileName.Contains("promotion-receipt", StringComparison.Ordinal)
        || fileName.Equals("toolchain.lock.json", StringComparison.Ordinal)
        || fileName.Equals("cliff.toml", StringComparison.Ordinal))
    {
        return "trusted-tooling";
    }

    if (normalized.Contains("signer", StringComparison.Ordinal)
        || normalized.Contains("trust/", StringComparison.Ordinal)
        || fileName.Contains("allowed-signers", StringComparison.Ordinal)
        || fileName.Contains("release-signing-policy", StringComparison.Ordinal)
        || fileName.Contains("tag-verification", StringComparison.Ordinal))
    {
        return "signer-verification";
    }

    if (normalized.Contains("deployment", StringComparison.Ordinal) || normalized.Contains("artifacts/deploy", StringComparison.Ordinal))
    {
        return "deployment";
    }

    if (fileName.Contains("digest", StringComparison.Ordinal)
        || fileName.Contains("promotion", StringComparison.Ordinal)
        || fileName.Contains("trivy", StringComparison.Ordinal)
        || fileName.Contains("attestation", StringComparison.Ordinal)
        || fileName.Contains("oci-", StringComparison.Ordinal)
        || fileName.Contains("checksums", StringComparison.Ordinal))
    {
        return "container";
    }

    if (normalized.Contains("openapi", StringComparison.Ordinal) || normalized.Contains("oasdiff", StringComparison.Ordinal))
    {
        return "openapi";
    }

    if (normalized.Contains("workflow-security", StringComparison.Ordinal)
        || normalized.Contains("actionlint", StringComparison.Ordinal)
        || normalized.Contains("zizmor", StringComparison.Ordinal))
    {
        return "workflow-security";
    }

    if (normalized.Contains("secret-scanning", StringComparison.Ordinal) || normalized.Contains("gitleaks", StringComparison.Ordinal))
    {
        return "secret-scanning";
    }

    if (normalized.Contains("scorecard", StringComparison.Ordinal))
    {
        return "scorecard";
    }

    if (normalized.Contains("nuget-vulnerabilit", StringComparison.Ordinal) || normalized.Contains("dependency", StringComparison.Ordinal))
    {
        return "dependency";
    }

    if (normalized.Contains("security-test", StringComparison.Ordinal) || normalized.Contains("cerbos", StringComparison.Ordinal))
    {
        return "security-tests";
    }

    if (normalized.Contains("screenshot", StringComparison.Ordinal)
        || normalized.Contains("video", StringComparison.Ordinal))
    {
        return "test-results";
    }

    if (fileName.EndsWith(".trx", StringComparison.Ordinal)
        || fileName.Contains("build", StringComparison.Ordinal)
        || normalized.Contains("test-results", StringComparison.Ordinal))
    {
        return "test-results";
    }

    return "uncategorized";
}

static bool IsInsideDirectory(string path, string directory)
{
    var relativePath = Path.GetRelativePath(directory, path);
    return !relativePath.StartsWith("..", StringComparison.Ordinal)
        && !Path.IsPathRooted(relativePath);
}

static IReadOnlyList<string> EnumerateArtifactFiles(string rootFullPath, string outputDirectoryFullPath, string diagnosticPrefix)
{
    var files = new List<string>();
    var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var stack = new Stack<(string Directory, int Depth)>();
    stack.Push((rootFullPath, 0));
    long totalBytes = 0;

    while (stack.Count > 0)
    {
        (string directory, int depth) = stack.Pop();
        if (depth > MaximumArtifactPathDepth) throw new InvalidOperationException($"{diagnosticPrefix}_artifact_path_invalid");

        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            string fullPath = Path.GetFullPath(entry);
            if (IsInsideDirectory(fullPath, outputDirectoryFullPath) || StringComparer.Ordinal.Equals(fullPath, outputDirectoryFullPath)) continue;
            string relativePath = ToManifestPath(rootFullPath, fullPath);
            ValidateRelativeArtifactPath(rootFullPath, fullPath, relativePath, aliases, diagnosticPrefix);
            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException($"{diagnosticPrefix}_artifact_path_alias");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                stack.Push((fullPath, depth + 1));
                continue;
            }

            if (Path.GetFileName(fullPath).Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)) continue;
            if (HasUnsafeLinkCount(fullPath)) throw new InvalidOperationException($"{diagnosticPrefix}_artifact_path_alias");
            long length = new FileInfo(fullPath).Length;
            if (length > MaximumArtifactBytes) throw new InvalidOperationException($"{diagnosticPrefix}_artifact_size_invalid");
            totalBytes = checked(totalBytes + length);
            if (totalBytes > MaximumArtifactTreeBytes) throw new InvalidOperationException($"{diagnosticPrefix}_artifact_tree_size_invalid");
            files.Add(fullPath);
            if (files.Count > MaximumArtifactCount) throw new InvalidOperationException($"{diagnosticPrefix}_artifact_count_invalid");
        }
    }

    return files.OrderBy(path => ToManifestPath(rootFullPath, path), StringComparer.Ordinal).ToList();
}

static void ValidateRelativeArtifactPath(string rootFullPath, string fullPath, string relativePath, HashSet<string> aliases, string diagnosticPrefix)
{
    string rootPrefix = Path.TrimEndingDirectorySeparator(rootFullPath) + Path.DirectorySeparatorChar;
    if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal)
        || relativePath.Length == 0
        || relativePath.StartsWith("../", StringComparison.Ordinal)
        || Path.IsPathRooted(relativePath))
    {
        throw new InvalidOperationException($"{diagnosticPrefix}_artifact_path_invalid");
    }

    string[] segments = relativePath.Split('/');
    if (segments.Length > MaximumArtifactPathDepth
        || segments.Any(segment => segment.Length == 0 || segment is "." or "..")
        || !relativePath.IsNormalized(NormalizationForm.FormC)
        || Encoding.UTF8.GetByteCount(relativePath) > MaximumArtifactPathBytes)
    {
        throw new InvalidOperationException($"{diagnosticPrefix}_artifact_path_invalid");
    }

    if (!aliases.Add(relativePath.Normalize(NormalizationForm.FormC)))
    {
        throw new InvalidOperationException($"{diagnosticPrefix}_artifact_path_alias");
    }
}

static bool HasUnsafeLinkCount(string path)
{
    if (OperatingSystem.IsWindows()) return WindowsFileLinkCount.HasUnsafe(path);
    if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return true;
    string stat = OperatingSystem.IsLinux() ? "/usr/bin/stat" : "/usr/bin/stat";
    if (!File.Exists(stat)) return true;
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo(stat)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        },
    };
    if (OperatingSystem.IsLinux())
    {
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("%h");
    }
    else
    {
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("%l");
    }

    process.StartInfo.ArgumentList.Add(path);
    try
    {
        if (!process.Start()) return true;
        string output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return true;
        }

        return process.ExitCode != 0 || !int.TryParse(output.Trim(), CultureInfo.InvariantCulture, out int count) || count != 1;
    }
    catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
    {
        return true;
    }
}

static string ToManifestPath(string rootFullPath, string fileFullPath)
{
    return Path.GetRelativePath(rootFullPath, fileFullPath)
        .Replace(Path.DirectorySeparatorChar, '/')
        .Replace(Path.AltDirectorySeparatorChar, '/');
}

static string GetEnv(string name)
{
    return Environment.GetEnvironmentVariable(name) ?? string.Empty;
}

static string GetMetadataEnv(string name)
{
    string value = GetEnv(name);
    if (Encoding.UTF8.GetByteCount(value) > 4_096
        || !value.IsNormalized(NormalizationForm.FormC)
        || value.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format))
    {
        throw new InvalidOperationException($"release_bundle_metadata_invalid:{name}");
    }

    return value;
}

static ReleaseIdentity ReadReleaseIdentity(string path, ArtifactEvidence manifestArtifact, IReadOnlyDictionary<string, ArtifactEvidence> artifacts, Regex fullOidPattern, Regex sha256Pattern, Regex versionPattern)
{
    byte[] bytes = File.ReadAllBytes(path);
    if (bytes.Length == 0 || bytes.Length > MaximumCanonicalManifestBytes) throw new InvalidOperationException("release_bundle_final_manifest_size_invalid");
    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) throw new InvalidOperationException("release_bundle_final_manifest_canonical_invalid");
    string text = new UTF8Encoding(false, true).GetString(bytes);
    if (text.Contains('\r', StringComparison.Ordinal)) throw new InvalidOperationException("release_bundle_final_manifest_canonical_invalid");
    using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
    JsonElement root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
    ValidateCanonicalManifest(bytes, root);
    string schemaVersion = RequiredString(root, "schemaVersion");
    if (schemaVersion != "release-evidence.v1") throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
    string objectFormat = RequiredString(root, "objectFormat");
    int oidLength = root.GetProperty("oidLength").GetInt32();
    string version = RequiredString(root, "version");
    string line = RequiredString(root, "line");
    string tagName = RequiredString(root, "tagName");
    string tagObjectId = RequiredString(root, "tagObjectId");
    string targetOid = RequiredString(root, "targetOid");
    string candidateOid = RequiredString(root, "candidateOid");
    string candidateManifestSha256 = RequiredString(root, "candidateManifestSha256");
    string releaseDescriptorSha256 = RequiredString(root, "releaseDescriptorSha256");
    string releaseSummarySha256 = RequiredString(root, "releaseSummarySha256");
    string releaseContextSha256 = RequiredString(root, "releaseContextSha256");
    string releaseNotesSha256 = RequiredString(root, "releaseNotesSha256");
    string trustedBundleManifestSha256 = RequiredString(root, "trustedBundleManifestSha256");
    string trustedBundlePolicySha256 = RequiredString(root, "trustedBundlePolicySha256");
    string trustedBundleConfigSha256 = RequiredString(root, "trustedBundleConfigSha256");
    string trustedBundleTrustSha256 = RequiredString(root, "trustedBundleTrustSha256");
    string trustedBundleToolchainSha256 = RequiredString(root, "trustedBundleToolchainSha256");
    string trustedBundleGitCliffSha256 = RequiredString(root, "trustedBundleGitCliffSha256");

    if ((objectFormat == "sha1" && oidLength != 40) || (objectFormat == "sha256" && oidLength != 64) || objectFormat is not ("sha1" or "sha256"))
        throw new InvalidOperationException("release_bundle_oid_invalid");
    if (!versionPattern.IsMatch(version)) throw new InvalidOperationException("release_bundle_version_invalid");
    string[] versionParts = version.Split(['.', '-'], 4);
    if (!line.Equals($"v{versionParts[0]}.{versionParts[1]}", StringComparison.Ordinal)) throw new InvalidOperationException("release_bundle_line_mismatch");
    if (!tagName.Equals($"v{version}", StringComparison.Ordinal)) throw new InvalidOperationException("release_bundle_tag_name_mismatch");
    string[] objectIds = [tagObjectId, targetOid, candidateOid, RequiredString(root, "previousPublishedOid"), RequiredString(root, "previousPublishedTagObjectId"), RequiredString(root, "baseStableOid"), RequiredString(root, "baseStableTagObjectId")];
    if (objectIds.Any(oid => oid.Length != oidLength || !fullOidPattern.IsMatch(oid))) throw new InvalidOperationException("release_bundle_oid_invalid");
    if (!string.Equals(targetOid, candidateOid, StringComparison.Ordinal)) throw new InvalidOperationException("release_bundle_target_mismatch");
    if (string.Equals(tagObjectId, targetOid, StringComparison.Ordinal)) throw new InvalidOperationException("release_bundle_tag_object_mismatch");
    if (RequiredString(root, "candidateManifestSchemaVersion") != "release-candidate.v1")
        throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
    foreach (string digest in root.EnumerateObject().Where(property => property.Name.EndsWith("Sha256", StringComparison.Ordinal)).Select(property => property.Value.GetString()!))
    {
        if (!sha256Pattern.IsMatch(digest)) throw new InvalidOperationException("release_bundle_digest_invalid");
    }

    string releaseDirectory = Path.GetDirectoryName(manifestArtifact.RelativePath)!.Replace('\\', '/');
    VerifyArtifactHash(artifacts, $"{releaseDirectory}/release.yaml", releaseDescriptorSha256, "release_bundle_descriptor_hash_mismatch");
    VerifyArtifactHash(artifacts, $"{releaseDirectory}/summary.md", releaseSummarySha256, "release_bundle_summary_hash_mismatch");
    VerifyArtifactHash(artifacts, $"{releaseDirectory}/release-context.v1.json", releaseContextSha256, "release_bundle_context_hash_mismatch");
    VerifyArtifactHash(artifacts, $"{releaseDirectory}/release-notes.md", releaseNotesSha256, "release_bundle_notes_hash_mismatch");
    VerifyArtifactHash(artifacts, $"{releaseDirectory}/release-candidate.v1.json", candidateManifestSha256, "release_bundle_candidate_hash_mismatch");
    VerifyAnyArtifactHash(artifacts, ["trusted-bundle/trusted-bundle.manifest.json"], trustedBundleManifestSha256, "release_bundle_trusted_manifest_hash_mismatch");
    VerifyAnyArtifactHash(artifacts, ["trusted-bundle/policy/release-policy.yaml", "policy/release-policy.yaml"], trustedBundlePolicySha256, "release_bundle_policy_hash_mismatch");
    VerifyAnyArtifactHash(artifacts, ["trusted-bundle/config/cliff.toml", "config/cliff.toml"], trustedBundleConfigSha256, "release_bundle_config_hash_mismatch");
    VerifyAnyArtifactHash(artifacts, ["trusted-bundle/trust/allowed-signers", "trust/allowed-signers"], trustedBundleTrustSha256, "release_bundle_trust_hash_mismatch");
    VerifyAnyArtifactHash(artifacts, ["trusted-bundle/toolchain.lock.json", "toolchain.lock.json"], trustedBundleToolchainSha256, "release_bundle_toolchain_hash_mismatch");
    VerifyAnyArtifactHash(artifacts, ["trusted-bundle/git-cliff", "trusted-bundle/git-cliff.exe", "git-cliff", "git-cliff.exe"], trustedBundleGitCliffSha256, "release_bundle_tool_hash_mismatch");

    return new ReleaseIdentity(
        schemaVersion,
        manifestArtifact.RelativePath,
        manifestArtifact.Sha256,
        version,
        line,
        tagName,
        tagObjectId,
        targetOid,
        candidateOid,
        candidateManifestSha256,
        releaseDescriptorSha256,
        releaseSummarySha256,
        releaseContextSha256,
        releaseNotesSha256,
        trustedBundleManifestSha256,
        trustedBundlePolicySha256,
        trustedBundleConfigSha256,
        trustedBundleTrustSha256,
        trustedBundleToolchainSha256,
        trustedBundleGitCliffSha256);
}

static void ValidateCanonicalManifest(byte[] bytes, JsonElement root)
{
    string[] expectedProperties =
    [
        "baseStableOid", "baseStableTag", "baseStableTagObjectId", "candidateManifestSchemaVersion",
        "candidateManifestSha256", "candidateOid", "line", "objectFormat", "oidLength",
        "previousPublishedOid", "previousPublishedTag", "previousPublishedTagObjectId",
        "releaseContextSha256", "releaseDate", "releaseDescriptorSha256", "releaseFragmentsSha256",
        "releaseNotesSha256", "releaseSummarySha256", "schemaVersion", "signerAlgorithm",
        "signerKeyFingerprint", "signerPrincipal", "signerRole", "signerValidFrom", "signerValidUntil", "tagName",
        "tagObjectId", "targetOid", "trustedBundleConfigSha256", "trustedBundleGitCliffSha256",
        "trustedBundleManifestSha256", "trustedBundlePolicySha256", "trustedBundleToolchainSha256",
        "trustedBundleTrustSha256", "version",
    ];
    JsonProperty[] properties = root.EnumerateObject().ToArray();
    string[] names = properties.Select(property => property.Name.Normalize(NormalizationForm.FormC)).Order(StringComparer.Ordinal).ToArray();
    if (names.Length != expectedProperties.Length || !names.SequenceEqual(expectedProperties, StringComparer.Ordinal))
    {
        throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
    }

    if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
    {
        throw new InvalidOperationException("release_bundle_final_manifest_duplicate_property");
    }

    foreach (JsonProperty property in properties)
    {
        if (!property.Name.IsNormalized(NormalizationForm.FormC)) throw new InvalidOperationException("release_bundle_final_manifest_canonical_invalid");
        if (property.Name == "oidLength")
        {
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out int oidLength) || oidLength is not (40 or 64))
                throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
            continue;
        }

        if (property.Value.ValueKind != JsonValueKind.String) throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
        string value = property.Value.GetString()!;
        if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > 4_096 || !value.IsNormalized(NormalizationForm.FormC))
            throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
    }

    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true }))
    {
        writer.WriteStartObject();
        foreach (JsonProperty property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
    stream.WriteByte((byte)'\n');
    if (!bytes.AsSpan().SequenceEqual(stream.ToArray())) throw new InvalidOperationException("release_bundle_final_manifest_canonical_invalid");
}

static void VerifyExplicitInput(string name, string expected, string diagnostic)
{
    string value = GetEnv(name);
    if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"release_bundle_input_missing:{name}");
    if (!string.Equals(value, expected, StringComparison.Ordinal)) throw new InvalidOperationException(diagnostic);
}

static string RequiredString(JsonElement root, string name)
{
    if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
    return value.GetString() ?? throw new InvalidOperationException("release_bundle_final_manifest_schema_invalid");
}

static void VerifyArtifactHash(IReadOnlyDictionary<string, ArtifactEvidence> artifacts, string path, string expectedSha256, string diagnostic)
{
    if (!artifacts.TryGetValue(path, out ArtifactEvidence? artifact)) throw new InvalidOperationException(diagnostic);
    if (!string.Equals(artifact.Sha256, expectedSha256, StringComparison.Ordinal)) throw new InvalidOperationException(diagnostic);
}

static void VerifyAnyArtifactHash(IReadOnlyDictionary<string, ArtifactEvidence> artifacts, IReadOnlyList<string> paths, string expectedSha256, string diagnostic)
{
    foreach (string path in paths)
    {
        if (artifacts.TryGetValue(path, out ArtifactEvidence? artifact))
        {
            if (!string.Equals(artifact.Sha256, expectedSha256, StringComparison.Ordinal)) throw new InvalidOperationException(diagnostic);
            return;
        }
    }

    throw new InvalidOperationException(diagnostic);
}

static int RunChecksumWriter(string artifactRootFullPath, string checksumPath)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = Directory.GetCurrentDirectory(),
    };
    startInfo.ArgumentList.Add("run");
    startInfo.ArgumentList.Add(Path.Combine(".ci", "scripts", "write-artifact-checksums.cs"));
    startInfo.ArgumentList.Add("--");
    startInfo.ArgumentList.Add(artifactRootFullPath);
    startInfo.ArgumentList.Add(checksumPath);
    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine("release_bundle_checksum_writer_failed: dotnet did not start");
        return 1;
    }

    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
    Task<string> errorTask = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        Console.Error.WriteLine("release_bundle_checksum_writer_timeout");
        return 1;
    }

    string output = outputTask.GetAwaiter().GetResult();
    string error = errorTask.GetAwaiter().GetResult();
    if (process.ExitCode != 0)
    {
        Console.Error.WriteLine("release_bundle_checksum_writer_failed");
        Console.Error.Write(error);
        return process.ExitCode;
    }

    Console.Write(output);
    return 0;
}

static bool PublishBundle(string stagingDirectory, string outputDirectory)
{
    try
    {
        string? parent = Path.GetDirectoryName(outputDirectory);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        Directory.Move(stagingDirectory, outputDirectory);
        return true;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
    {
        Console.Error.WriteLine("release_bundle_output_publish_failed");
        return false;
    }
}

static string ValueOrUnknown(string value)
{
    return string.IsNullOrWhiteSpace(value) ? "not provided" : value;
}

static void AppendInvariantLine(StringBuilder builder, FormattableString value)
{
    builder.Append(value.ToString(CultureInfo.InvariantCulture));
    builder.AppendLine();
}

public sealed record ReleaseEvidenceBundle(
    DateTimeOffset GeneratedAtUtc,
    string ArtifactRoot,
    string Repository,
    string Ref,
    string CommitSha,
    string RunId,
    string RunAttempt,
    string ReleaseVersion,
    string ClaStatus,
    ReleaseIdentity ReleaseIdentity,
    string Notes,
    IReadOnlyList<ArtifactEvidence> Artifacts);

public sealed record ReleaseIdentity(
    string SchemaVersion,
    string ManifestPath,
    string ManifestSha256,
    string Version,
    string Line,
    string TagName,
    string TagObjectId,
    string TargetOid,
    string CandidateOid,
    string CandidateManifestSha256,
    string ReleaseDescriptorSha256,
    string ReleaseSummarySha256,
    string ReleaseContextSha256,
    string ReleaseNotesSha256,
    string TrustedBundleManifestSha256,
    string TrustedBundlePolicySha256,
    string TrustedBundleConfigSha256,
    string TrustedBundleTrustSha256,
    string TrustedBundleToolchainSha256,
    string TrustedBundleGitCliffSha256);

public sealed record ArtifactEvidence(
    string RelativePath,
    string Category,
    long SizeBytes,
    string Sha256);

static class WindowsFileLinkCount
{
    public static bool HasUnsafe(string path)
    {
        const uint fileShareReadWriteDelete = 0x00000007;
        const uint openExisting = 3;
        const uint fileAttributeNormal = 0x00000080;
        try
        {
            using SafeFileHandle handle = CreateFileW(path, 0, fileShareReadWriteDelete, IntPtr.Zero, openExisting, fileAttributeNormal, IntPtr.Zero);
            if (handle.IsInvalid) return true;
            return !GetFileInformationByHandle(handle, out ByHandleFileInformation information) || information.NumberOfLinks != 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle fileHandle, out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }
}
