using System.Security.Cryptography;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record TrustedBundleVerificationRequest(
    string BundleRoot,
    string CandidateCheckoutRoot,
    PromotionAuthorityInput PromotionAuthority,
    string ExpectedBundleId,
    string ExpectedBundleVersion,
    string ExpectedPolicyVersion,
    string ExpectedConfigVersion,
    string ExpectedTrustVersion)
{
    public string? ExpectedManifestDigest { get; init; }
}

public sealed record PromotionAuthorityInput(
    string ReceiptPath,
    string SignaturePath,
    string Principal);

public sealed record TrustedBundleResult(bool IsValid, string? ManifestDigest, string? Diagnostic, VerifiedTrustedBundle? Bundle = null);

public sealed class VerifiedTrustedBundle
{
    internal VerifiedTrustedBundle(string root, string manifestDigest, string configDigest, string toolchainLockDigest)
    {
        Root = root;
        ManifestDigest = manifestDigest;
        ConfigDigest = configDigest;
        ToolchainLockDigest = toolchainLockDigest;
        ConfigPath = Path.Combine(root, "config", "cliff.toml");
        ToolchainLockPath = Path.Combine(root, "toolchain.lock.json");
    }

    public string Root { get; }
    public string ManifestDigest { get; }
    public string ConfigDigest { get; }
    public string ToolchainLockDigest { get; }
    public string ConfigPath { get; }
    public string ToolchainLockPath { get; }
}

public sealed record TrustedSshSigner(
    string Principal,
    string Role,
    string KeyFingerprint,
    string Algorithm,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    DateOnly? RevokedOn);

public sealed record SshTagAuthorizationRequest(
    bool IsAnnotatedTag,
    bool CryptographicSignatureVerified,
    string Principal,
    string RequiredRole,
    string KeyFingerprint,
    string Algorithm,
    DateOnly VerificationDate,
    string ExpectedTagObjectId,
    string ObservedTagObjectId,
    string? PreviouslyRecordedTagObjectId);

public sealed record TrustPolicyResult(bool IsValid, string? Diagnostic);

public sealed record RestrictedSecurityInput(
    string RestrictedDetails,
    string Secret,
    string Identity,
    string StoragePath,
    string ProviderMetadata,
    string ApprovedPublicReference,
    string ApprovedPublicDisposition);

public sealed record PublicSecurityDisposition(string Reference, string Disposition);

public sealed record PublicSecurityDispositionResult(bool IsValid, PublicSecurityDisposition? Disposition, string? Diagnostic);

public static class TrustedBundlePolicy
{
    private const string ManifestName = "trusted-bundle.manifest.json";
    private const string PromotionNamespace = "islamu-release-promotion";
    private const string PromotionTrustRootName = "ISLAMU.ReleaseEngineering.promotion-allowed-signers";
    private static readonly string[] RequiredManifestPaths =
    [
        "bin/ISLAMU.ReleaseEngineering.dll",
        "config/cliff.toml",
        "policy/context-version.txt",
        "policy/release-policy.yaml",
        "policy/schema-version.txt",
        "toolchain.lock.json",
        "trust/allowed-signers",
        "trust/release-signing-policy.yaml",
    ];
    public const int MaximumManifestBytes = 1_048_576;
    public const int MaximumPromotionReceiptBytes = 65_536;
    public const int MaximumPromotionSignatureBytes = 16_384;
    public const int MaximumPromotionTrustRootBytes = 65_536;
    public const int MaximumBundleFiles = 256;
    public const long MaximumFileBytes = 16 * 1_024 * 1_024;
    public const long MaximumTotalBytes = 64 * 1_024 * 1_024;
    public const int MaximumPathUtf8Bytes = 512;
    public const int MaximumPathDepth = 12;
    public const int MaximumEnumeratedEntries = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex PrincipalPattern = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ReceiptIdPattern = new("^promotion-[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static TrustedBundleResult Verify(TrustedBundleVerificationRequest request)
    {
        if (request is null)
        {
            return Invalid("trusted_bundle_request_missing");
        }

        RootResolution bundleRoot = ResolveDirectoryRoot(request.BundleRoot);
        RootResolution candidateRoot = ResolveDirectoryRoot(request.CandidateCheckoutRoot);
        if (bundleRoot.Diagnostic is not null || candidateRoot.Diagnostic is not null)
        {
            return Invalid("trusted_bundle_invalid_root");
        }

        if (PathsOverlap(bundleRoot.ResolvedPath!, candidateRoot.ResolvedPath!))
        {
            return Invalid("trusted_bundle_candidate_overlap");
        }

        if (bundleRoot.IsAlias || candidateRoot.IsAlias)
        {
            return Invalid("trusted_bundle_invalid_root");
        }

        PromotionResult promotion = VerifyPromotionAuthority(request.PromotionAuthority, bundleRoot.ResolvedPath!, candidateRoot.ResolvedPath!);
        if (promotion.Diagnostic is not null)
        {
            return Invalid(promotion.Diagnostic);
        }

        string manifestPath = Path.Combine(bundleRoot.ResolvedPath!, ManifestName);
        BoundedReadResult manifest;
        try
        {
            if (!File.Exists(manifestPath) || IsSymbolicLink(manifestPath))
            {
                return Invalid("trusted_bundle_manifest_missing");
            }

            manifest = ReadBounded(manifestPath, MaximumManifestBytes, "trusted_bundle_manifest_too_large");
            if (manifest.Diagnostic is not null)
            {
                return Invalid(manifest.Diagnostic);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("trusted_bundle_unreadable");
        }

        byte[] manifestBytes = manifest.Bytes!;
        string digest = Sha256(manifestBytes);
        if (!FixedDigestEquals(digest, request.ExpectedManifestDigest ?? string.Empty))
        {
            return Invalid("trusted_bundle_expected_digest_mismatch");
        }

        if (!FixedDigestEquals(digest, promotion.ManifestDigest!))
        {
            return Invalid("trusted_bundle_promotion_mismatch");
        }

        string manifestText;
        try
        {
            manifestText = StrictUtf8.GetString(manifestBytes);
        }
        catch (DecoderFallbackException)
        {
            return Invalid("trusted_bundle_manifest_invalid_utf8");
        }

        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(manifestText);
        if (!canonical.IsValid || !manifestBytes.AsSpan().SequenceEqual(canonical.Bytes))
        {
            return Invalid("trusted_bundle_manifest_not_canonical");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(manifestBytes);
            JsonElement root = document.RootElement;
            if (!HasExactProperties(root, "schemaVersion", "bundleId", "bundleVersion", "policyVersion", "configVersion", "trustVersion", "policyDigest", "configDigest", "trustDigest", "files") ||
                root.GetProperty("schemaVersion").GetString() != "trusted-bundle.v1")
            {
                return Invalid("trusted_bundle_manifest_schema_invalid");
            }

            string? mismatch = VersionMismatch(root, request);
            if (mismatch is not null)
            {
                return Invalid(mismatch);
            }

            mismatch = PromotionMismatch(root, promotion);
            if (mismatch is not null)
            {
                return Invalid(mismatch);
            }

            var declaredFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var normalizedPaths = new HashSet<string>(StringComparer.Ordinal);
            var portablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int declaredCount = 0;
            foreach (JsonElement item in root.GetProperty("files").EnumerateArray())
            {
                if (++declaredCount > MaximumBundleFiles)
                {
                    return Invalid("trusted_bundle_file_limit_exceeded");
                }

                if (!HasExactProperties(item, "path", "sha256"))
                {
                    return Invalid("trusted_bundle_manifest_schema_invalid");
                }

                string path = item.GetProperty("path").GetString() ?? string.Empty;
                string hash = item.GetProperty("sha256").GetString() ?? string.Empty;
                string normalizedPath = path.Normalize(NormalizationForm.FormC);
                if (path != normalizedPath && normalizedPaths.Contains(normalizedPath))
                {
                    return Invalid("trusted_bundle_path_normalization_collision");
                }

                if (!IsSafeRelativePath(path, bundleRoot.ResolvedPath!) || !Sha256Pattern.IsMatch(hash))
                {
                    return Invalid("trusted_bundle_unsafe_path");
                }

                if (!declaredFiles.TryAdd(path, hash))
                {
                    return Invalid("trusted_bundle_duplicate_path");
                }

                if (!portablePaths.Add(path))
                {
                    return Invalid("trusted_bundle_path_case_collision");
                }

                normalizedPaths.Add(normalizedPath);
            }

            if (RequiredManifestPaths.Any(path => !declaredFiles.ContainsKey(path)))
            {
                return Invalid("trusted_bundle_required_file_missing");
            }

            if (root.GetProperty("policyDigest").GetString() != declaredFiles["policy/release-policy.yaml"] ||
                root.GetProperty("configDigest").GetString() != declaredFiles["config/cliff.toml"] ||
                root.GetProperty("trustDigest").GetString() != declaredFiles["trust/release-signing-policy.yaml"])
            {
                return Invalid("trusted_bundle_component_digest_mismatch");
            }

            FileSetResult actual = EnumerateFiles(bundleRoot.ResolvedPath!);
            if (actual.Diagnostic is not null)
            {
                return Invalid(actual.Diagnostic);
            }

            if (!actual.Paths!.SetEquals(declaredFiles.Keys))
            {
                return Invalid("trusted_bundle_file_set_mismatch");
            }

            long totalBytes = 0;
            foreach (string path in declaredFiles.Keys)
            {
                string fullPath = Path.Combine(bundleRoot.ResolvedPath!, path.Replace('/', Path.DirectorySeparatorChar));
                LinkSafetyResult linkSafety = VerifyLinkSafety(fullPath);
                if (linkSafety.Diagnostic is not null)
                {
                    return Invalid(linkSafety.Diagnostic);
                }

                long length = new FileInfo(fullPath).Length;
                if (length > MaximumFileBytes)
                {
                    return Invalid("trusted_bundle_file_too_large");
                }

                totalBytes = checked(totalBytes + length);
                if (totalBytes > MaximumTotalBytes)
                {
                    return Invalid("trusted_bundle_total_size_exceeded");
                }
            }

            totalBytes = 0;
            foreach ((string path, string hash) in declaredFiles)
            {
                FileHashResult file = HashFileBounded(Path.Combine(bundleRoot.ResolvedPath!, path.Replace('/', Path.DirectorySeparatorChar)));
                if (file.Diagnostic is not null)
                {
                    return Invalid(file.Diagnostic);
                }

                totalBytes = checked(totalBytes + file.Length);
                if (totalBytes > MaximumTotalBytes)
                {
                    return Invalid("trusted_bundle_total_size_exceeded");
                }

                if (!FixedDigestEquals(file.Digest!, hash))
                {
                    return Invalid("trusted_bundle_hash_mismatch");
                }
            }

            return new TrustedBundleResult(true, digest, null, new VerifiedTrustedBundle(
                bundleRoot.ResolvedPath!,
                digest,
                declaredFiles["config/cliff.toml"],
                declaredFiles["toolchain.lock.json"]));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException or OverflowException)
        {
            return Invalid("trusted_bundle_verification_failed");
        }
    }

    private static PromotionResult VerifyPromotionAuthority(PromotionAuthorityInput authority, string bundleRoot, string candidateRoot)
    {
        if (authority is null || string.IsNullOrWhiteSpace(authority.ReceiptPath) || string.IsNullOrWhiteSpace(authority.SignaturePath))
        {
            return PromotionInvalid("trusted_bundle_promotion_receipt_missing");
        }

        string receiptPath;
        string signaturePath;
        string allowedSignersPath;
        try
        {
            receiptPath = Path.GetFullPath(authority.ReceiptPath);
            signaturePath = Path.GetFullPath(authority.SignaturePath);
            allowedSignersPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PromotionTrustRootName));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return PromotionInvalid("trusted_bundle_promotion_authority_invalid");
        }

        RootResolution allowedSigners = ResolveFileRoot(allowedSignersPath);
        if (allowedSigners.Diagnostic is not null)
        {
            return PromotionInvalid(allowedSigners.Diagnostic == "missing"
                ? "trusted_bundle_promotion_authority_not_configured"
                : "trusted_bundle_promotion_authority_invalid");
        }

        if (PathsOverlap(candidateRoot, receiptPath) || PathsOverlap(candidateRoot, signaturePath) || PathsOverlap(candidateRoot, allowedSigners.ResolvedPath!) ||
            PathsOverlap(bundleRoot, receiptPath) || PathsOverlap(bundleRoot, signaturePath) || PathsOverlap(bundleRoot, allowedSigners.ResolvedPath!))
        {
            return PromotionInvalid("trusted_bundle_promotion_authority_overlap");
        }

        if (allowedSigners.IsAlias)
        {
            return PromotionInvalid("trusted_bundle_promotion_authority_invalid");
        }

        try
        {
            if (!File.Exists(receiptPath) || !File.Exists(signaturePath) || IsSymbolicLink(receiptPath) || IsSymbolicLink(signaturePath))
            {
                return PromotionInvalid("trusted_bundle_promotion_receipt_missing");
            }

            if (!File.Exists(allowedSigners.ResolvedPath!) || IsSymbolicLink(allowedSigners.ResolvedPath!))
            {
                return PromotionInvalid("trusted_bundle_promotion_authority_not_configured");
            }

            BoundedReadResult receipt = ReadBounded(receiptPath, MaximumPromotionReceiptBytes, "trusted_bundle_promotion_receipt_too_large");
            BoundedReadResult signature = ReadBounded(signaturePath, MaximumPromotionSignatureBytes, "trusted_bundle_promotion_signature_too_large");
            BoundedReadResult trustRoot = ReadBounded(allowedSigners.ResolvedPath!, MaximumPromotionTrustRootBytes, "trusted_bundle_promotion_trust_root_too_large");
            if (receipt.Diagnostic is not null) return PromotionInvalid(receipt.Diagnostic);
            if (signature.Diagnostic is not null) return PromotionInvalid(signature.Diagnostic);
            if (trustRoot.Diagnostic is not null) return PromotionInvalid(trustRoot.Diagnostic);
            string trustRootText = StrictUtf8.GetString(trustRoot.Bytes!);
            if (!trustRootText.Split('\n', StringSplitOptions.TrimEntries).Any(line => line.Length != 0 && !line.StartsWith('#')))
            {
                return PromotionInvalid("trusted_bundle_promotion_authority_not_configured");
            }
            if (!PrincipalPattern.IsMatch(authority.Principal)) return PromotionInvalid("trusted_bundle_promotion_principal_invalid");
            if (!VerifySshSignature(receipt.Bytes!, signaturePath, authority.Principal))
            {
                return PromotionInvalid("trusted_bundle_promotion_signature_invalid");
            }

            string receiptText = StrictUtf8.GetString(receipt.Bytes!);
            CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(receiptText);
            if (!canonical.IsValid || !receipt.Bytes.AsSpan().SequenceEqual(canonical.Bytes))
            {
                return PromotionInvalid("trusted_bundle_promotion_receipt_not_canonical");
            }

            using JsonDocument document = JsonDocument.Parse(receipt.Bytes!);
            JsonElement root = document.RootElement;
            if (!HasExactProperties(root, "schemaVersion", "receiptId", "bundleManifestSha256", "bundleId", "bundleVersion", "policyVersion", "configVersion", "trustVersion", "policyDigest", "configDigest", "trustDigest", "promotionPrincipal") ||
                root.GetProperty("schemaVersion").GetString() != "trusted-bundle-promotion.v1")
            {
                return PromotionInvalid("trusted_bundle_promotion_receipt_schema_invalid");
            }

            string receiptId = root.GetProperty("receiptId").GetString() ?? string.Empty;
            if (!ReceiptIdPattern.IsMatch(receiptId)) return PromotionInvalid("trusted_bundle_promotion_receipt_schema_invalid");
            if (root.GetProperty("promotionPrincipal").GetString() != authority.Principal) return PromotionInvalid("trusted_bundle_promotion_principal_mismatch");

            string[] digests = ["bundleManifestSha256", "policyDigest", "configDigest", "trustDigest"];
            if (digests.Any(name => !Sha256Pattern.IsMatch(root.GetProperty(name).GetString() ?? string.Empty)))
            {
                return PromotionInvalid("trusted_bundle_promotion_receipt_schema_invalid");
            }

            return new PromotionResult(
                root.GetProperty("bundleManifestSha256").GetString(),
                root.GetProperty("bundleId").GetString(),
                root.GetProperty("bundleVersion").GetString(),
                root.GetProperty("policyVersion").GetString(),
                root.GetProperty("configVersion").GetString(),
                root.GetProperty("trustVersion").GetString(),
                root.GetProperty("policyDigest").GetString(),
                root.GetProperty("configDigest").GetString(),
                root.GetProperty("trustDigest").GetString(),
                null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or JsonException or InvalidOperationException)
        {
            return PromotionInvalid("trusted_bundle_promotion_verification_failed");
        }
    }

    private static bool VerifySshSignature(byte[] receipt, string signaturePath, string principal)
    {
        RootResolution allowedSigners = ResolveFileRoot(Path.Combine(AppContext.BaseDirectory, PromotionTrustRootName));
        if (allowedSigners.Diagnostic is not null || allowedSigners.IsAlias) return false;

        string executable = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh-keygen.exe")
            : "/usr/bin/ssh-keygen";
        if (!File.Exists(executable)) return false;

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(executable)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("-Y");
        process.StartInfo.ArgumentList.Add("verify");
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add(allowedSigners.ResolvedPath!);
        process.StartInfo.ArgumentList.Add("-I");
        process.StartInfo.ArgumentList.Add(principal);
        process.StartInfo.ArgumentList.Add("-n");
        process.StartInfo.ArgumentList.Add(PromotionNamespace);
        process.StartInfo.ArgumentList.Add("-s");
        process.StartInfo.ArgumentList.Add(signaturePath);
        try
        {
            if (!process.Start()) return false;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            process.StandardInput.BaseStream.Write(receipt);
            process.StandardInput.Close();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return false;
            }

            Task.WaitAll([output, error], TimeSpan.FromSeconds(1));
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string? VersionMismatch(JsonElement root, TrustedBundleVerificationRequest request)
    {
        if (root.GetProperty("bundleId").GetString() != request.ExpectedBundleId) return "trusted_bundle_id_mismatch";
        if (root.GetProperty("bundleVersion").GetString() != request.ExpectedBundleVersion) return "trusted_bundle_version_mismatch";
        if (root.GetProperty("policyVersion").GetString() != request.ExpectedPolicyVersion) return "trusted_bundle_policy_mismatch";
        if (root.GetProperty("configVersion").GetString() != request.ExpectedConfigVersion) return "trusted_bundle_config_mismatch";
        return root.GetProperty("trustVersion").GetString() != request.ExpectedTrustVersion ? "trusted_bundle_trust_mismatch" : null;
    }

    private static string? PromotionMismatch(JsonElement root, PromotionResult promotion)
    {
        if (root.GetProperty("bundleId").GetString() != promotion.BundleId) return "trusted_bundle_promotion_bundle_mismatch";
        if (root.GetProperty("bundleVersion").GetString() != promotion.BundleVersion) return "trusted_bundle_promotion_version_mismatch";
        if (root.GetProperty("policyVersion").GetString() != promotion.PolicyVersion) return "trusted_bundle_promotion_policy_mismatch";
        if (root.GetProperty("configVersion").GetString() != promotion.ConfigVersion) return "trusted_bundle_promotion_config_mismatch";
        if (root.GetProperty("trustVersion").GetString() != promotion.TrustVersion) return "trusted_bundle_promotion_trust_mismatch";
        if (root.GetProperty("policyDigest").GetString() != promotion.PolicyDigest) return "trusted_bundle_promotion_policy_digest_mismatch";
        if (root.GetProperty("configDigest").GetString() != promotion.ConfigDigest) return "trusted_bundle_promotion_config_digest_mismatch";
        return root.GetProperty("trustDigest").GetString() != promotion.TrustDigest ? "trusted_bundle_promotion_trust_digest_mismatch" : null;
    }

    private static FileSetResult EnumerateFiles(string root)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var portablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        int entryCount = 0;
        pending.Push(root);
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entryCount > MaximumEnumeratedEntries)
                {
                    return new FileSetResult(null, "trusted_bundle_entry_limit_exceeded");
                }

                if (IsSymbolicLink(entry))
                {
                    return new FileSetResult(null, "trusted_bundle_symlink_ambiguous");
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                    continue;
                }

                string relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (relative != ManifestName)
                {
                    if (!IsSafeRelativePath(relative, root)) return new FileSetResult(null, "trusted_bundle_unsafe_path");
                    if (!paths.Add(relative)) return new FileSetResult(null, "trusted_bundle_duplicate_path");
                    if (!portablePaths.Add(relative)) return new FileSetResult(null, "trusted_bundle_path_case_collision");
                    if (paths.Count > MaximumBundleFiles) return new FileSetResult(null, "trusted_bundle_file_limit_exceeded");
                }
            }
        }

        return new FileSetResult(paths, null);
    }

    private static bool IsSafeRelativePath(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || StrictUtf8.GetByteCount(path) > MaximumPathUtf8Bytes || path.Contains('\\') || path != path.Normalize(NormalizationForm.FormC) || Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string[] segments = path.Split('/');
        if (segments.Length > MaximumPathDepth || segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            return false;
        }

        string full = Path.GetFullPath(Path.Combine(root, path));
        return full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool PathsOverlap(string left, string right)
    {
        string leftPrefix = Path.TrimEndingDirectorySeparator(left) + Path.DirectorySeparatorChar;
        string rightPrefix = Path.TrimEndingDirectorySeparator(right) + Path.DirectorySeparatorChar;
        return string.Equals(left, right, PathComparison) || left.StartsWith(rightPrefix, PathComparison) || right.StartsWith(leftPrefix, PathComparison);
    }

    private static RootResolution ResolveDirectoryRoot(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                return new RootResolution(null, false, "missing");
            }

            if (!IsSymbolicLink(fullPath))
            {
                return new RootResolution(Path.TrimEndingDirectorySeparator(fullPath), false, null);
            }

            FileSystemInfo? target = new DirectoryInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
            return target is null
                ? new RootResolution(null, true, "unresolved")
                : new RootResolution(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.FullName)), true, null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or PathTooLongException or UnauthorizedAccessException)
        {
            return new RootResolution(null, false, "invalid");
        }
    }

    private static RootResolution ResolveFileRoot(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return new RootResolution(null, false, "missing");
            }

            if (!IsSymbolicLink(fullPath))
            {
                return new RootResolution(fullPath, false, null);
            }

            FileSystemInfo? target = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
            return target is null
                ? new RootResolution(null, true, "unresolved")
                : new RootResolution(Path.GetFullPath(target.FullName), true, null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or PathTooLongException or UnauthorizedAccessException)
        {
            return new RootResolution(null, false, "invalid");
        }
    }

    private static LinkSafetyResult VerifyLinkSafety(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return new LinkSafetyResult(null);
        }

        string executable = OperatingSystem.IsLinux() ? "/usr/bin/stat" : "/usr/bin/stat";
        if (!File.Exists(executable))
        {
            return new LinkSafetyResult("trusted_bundle_link_count_unsafe");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
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
            if (!process.Start()) return new LinkSafetyResult("trusted_bundle_link_count_unsafe");
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return new LinkSafetyResult("trusted_bundle_link_count_unsafe");
            }

            return process.ExitCode == 0 && int.TryParse(output.Trim(), out int count) && count == 1
                ? new LinkSafetyResult(null)
                : new LinkSafetyResult("trusted_bundle_link_count_unsafe");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new LinkSafetyResult("trusted_bundle_link_count_unsafe");
        }
    }

    private static bool IsSymbolicLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool HasExactProperties(JsonElement element, params string[] names) =>
        element.ValueKind == JsonValueKind.Object &&
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool FixedDigestEquals(string actual, string expected) =>
        Sha256Pattern.IsMatch(expected) && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected));

    private static BoundedReadResult ReadBounded(string path, int maximumBytes, string tooLargeDiagnostic)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) return new BoundedReadResult(null, tooLargeDiagnostic);
        byte[] bytes = new byte[stream.Length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) break;
            offset += read;
        }

        if (offset != bytes.Length || stream.ReadByte() != -1) return new BoundedReadResult(null, tooLargeDiagnostic);
        return new BoundedReadResult(bytes, null);
    }

    private static FileHashResult HashFileBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65_536];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            total += read;
            if (total > MaximumFileBytes) return new FileHashResult(null, total, "trusted_bundle_file_too_large");
            hash.AppendData(buffer, 0, read);
        }

        return new FileHashResult(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total, null);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static TrustedBundleResult Invalid(string diagnostic) => new(false, null, diagnostic);
    private static PromotionResult PromotionInvalid(string diagnostic) => new(null, null, null, null, null, null, null, null, null, diagnostic);
    private sealed record PromotionResult(string? ManifestDigest, string? BundleId, string? BundleVersion, string? PolicyVersion, string? ConfigVersion, string? TrustVersion, string? PolicyDigest, string? ConfigDigest, string? TrustDigest, string? Diagnostic);
    private sealed record BoundedReadResult(byte[]? Bytes, string? Diagnostic);
    private sealed record FileHashResult(string? Digest, long Length, string? Diagnostic);
    private sealed record FileSetResult(HashSet<string>? Paths, string? Diagnostic);
    private sealed record RootResolution(string? ResolvedPath, bool IsAlias, string? Diagnostic);
    private sealed record LinkSafetyResult(string? Diagnostic);
}

public static class SshSignerPolicy
{
    private static readonly Regex ObjectIdPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static TrustPolicyResult Authorize(IReadOnlyCollection<TrustedSshSigner> signers, SshTagAuthorizationRequest request)
    {
        if (signers is null || request is null) return Invalid("release_signer_request_missing");
        if (!request.IsAnnotatedTag) return Invalid("release_tag_not_annotated");
        if (!request.CryptographicSignatureVerified) return Invalid("release_tag_signature_invalid");
        if (request.RequiredRole != "release") return Invalid("release_signer_role_forbidden");
        if (request.Algorithm != "ssh-ed25519") return Invalid("release_signer_algorithm_forbidden");
        if (!ObjectIdPattern.IsMatch(request.ExpectedTagObjectId) || !ObjectIdPattern.IsMatch(request.ObservedTagObjectId)) return Invalid("release_tag_object_invalid");
        if (request.ExpectedTagObjectId != request.ObservedTagObjectId) return Invalid("release_tag_object_replaced");
        if (request.PreviouslyRecordedTagObjectId is not null && request.PreviouslyRecordedTagObjectId != request.ObservedTagObjectId) return Invalid("release_tag_object_recreated");
        if (signers.Select(signer => signer.Principal).Distinct(StringComparer.Ordinal).Count() != signers.Count ||
            signers.Select(signer => signer.KeyFingerprint).Distinct(StringComparer.Ordinal).Count() != signers.Count)
        {
            return Invalid("release_signer_policy_not_unique");
        }

        if (signers.Any(signer => signer.Algorithm != "ssh-ed25519" || signer.ValidFrom > signer.ValidUntil || string.IsNullOrWhiteSpace(signer.Principal) || string.IsNullOrWhiteSpace(signer.KeyFingerprint)))
        {
            return Invalid("release_signer_policy_invalid");
        }

        TrustedSshSigner? signer = signers.SingleOrDefault(candidate =>
            candidate.Principal == request.Principal && candidate.Role == request.RequiredRole && candidate.KeyFingerprint == request.KeyFingerprint);
        if (signer is null) return Invalid("release_signer_unauthorized");
        if (signer.Algorithm != "ssh-ed25519" || signer.Algorithm != request.Algorithm) return Invalid("release_signer_algorithm_forbidden");
        if (signer.RevokedOn is not null && request.VerificationDate >= signer.RevokedOn) return Invalid("release_signer_revoked");
        if (request.VerificationDate < signer.ValidFrom || request.VerificationDate > signer.ValidUntil) return Invalid("release_signer_not_current");
        return new TrustPolicyResult(true, null);
    }

    private static TrustPolicyResult Invalid(string diagnostic) => new(false, diagnostic);
}

public static class EmbargoPolicy
{
    private static readonly Regex PublicReferencePattern = new("^(?:CVE-[0-9]{4}-[0-9]{4,}|SEC-[0-9]{4}-[0-9]{4})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static PublicSecurityDispositionResult CreatePublicDisposition(RestrictedSecurityInput input, bool disclosureAuthorized)
    {
        if (input is null)
        {
            return Invalid("embargo_input_missing");
        }
        if (!disclosureAuthorized)
        {
            return Invalid("embargo_disclosure_not_authorized");
        }

        CanonicalTextResult disposition = CanonicalArtifactPolicy.EscapeUntrustedMarkdown(input.ApprovedPublicDisposition);
        if (!PublicReferencePattern.IsMatch(input.ApprovedPublicReference) || !disposition.IsValid || input.ApprovedPublicDisposition.Contains('/') || input.ApprovedPublicDisposition.Contains('\\') || AliasesRestrictedInput(input))
        {
            return Invalid("embargo_public_disposition_invalid");
        }

        return new PublicSecurityDispositionResult(true, new PublicSecurityDisposition(input.ApprovedPublicReference, disposition.Text!), null);
    }

    private static bool AliasesRestrictedInput(RestrictedSecurityInput input)
    {
        string publicReference = NormalizeAlias(input.ApprovedPublicReference);
        string publicDisposition = NormalizeAlias(input.ApprovedPublicDisposition);
        string[] restrictedValues =
        [
            input.RestrictedDetails,
            input.Secret,
            input.Identity,
            input.StoragePath,
            input.ProviderMetadata,
        ];

        return restrictedValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeAlias)
            .Any(restricted => restricted.Length != 0 && (restricted == publicReference || restricted == publicDisposition));
    }

    private static string NormalizeAlias(string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        var builder = new StringBuilder(normalized.Length);
        bool previousWhitespace = false;
        foreach (Rune rune in normalized.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Control or UnicodeCategory.Format)
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }
                continue;
            }

            builder.Append(rune.ToString().ToLowerInvariant());
            previousWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static PublicSecurityDispositionResult Invalid(string diagnostic) => new(false, null, diagnostic);
}
