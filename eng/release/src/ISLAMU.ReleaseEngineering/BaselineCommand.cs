// ABOUTME: Verifies SSH-signed annotated non-SemVer changelog baseline tags.
// ABOUTME: Writes deterministic baseline evidence without creating, moving, or mutating Git tags.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public static class BaselineCommand
{
    private const int MaximumGitOutputCharacters = 131_072;
    private const int MaximumArtifactBytes = 1_048_576;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SignaturePattern = new("\n-----BEGIN SSH SIGNATURE-----\n", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex GoodSignaturePattern = new("Good \\\"git\\\" signature for (?<principal>[^\\s]+)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static int Run(string[] args, TextWriter output, string repositoryRoot, TimeSpan timeout)
    {
        if (args.Length != 4)
        {
            output.WriteLine("invalid_arguments: verify-baseline requires baseline ref, target oid, and tag object id");
            return Program.UsageError;
        }

        try
        {
            string root = ResolveDirectory(repositoryRoot);
            string baselineRef = args[1];
            string targetOid = args[2];
            string expectedTagObjectId = args[3];
            if (!BaselineEvidencePolicy.IsBaselineRef(baselineRef)) return Reject(output, "release_baseline_ref_invalid");
            if (!FullOidPattern.IsMatch(targetOid) || !FullOidPattern.IsMatch(expectedTagObjectId)) return Reject(output, "release_baseline_object_invalid");

            TrustedBundleResult trusted = VerifyBundle(root);
            if (!trusted.IsValid || trusted.Bundle is null) return Reject(output, "release_trusted_bundle_invalid");

            string tagType = RunGit(root, "cat-file", "-t", expectedTagObjectId).Trim();
            if (tagType != "tag") return Reject(output, "release_baseline_tag_not_annotated");
            string observedTagObjectId = RunGit(root, "rev-parse", "--verify", $"refs/tags/{baselineRef}^{{object}}").Trim();
            if (observedTagObjectId != expectedTagObjectId) return Reject(output, "release_baseline_tag_object_replaced");
            string observedTarget = RunGit(root, "rev-parse", "--verify", $"{expectedTagObjectId}^{{commit}}").Trim();
            if (observedTarget != targetOid) return Reject(output, "release_baseline_wrong_target");

            TagObject tag = ParseTagObject(RunGit(root, "cat-file", "-p", expectedTagObjectId));
            if (tag.Name != baselineRef) return Reject(output, "release_baseline_name_mismatch");
            SshVerification verification = VerifyTagSignature(root, expectedTagObjectId, Path.Combine(trusted.Bundle.Root, "trust", "allowed-signers"));
            if (!verification.Verified) return Reject(output, "release_baseline_signature_invalid");

            DateOnly verificationDate = DateOnly.ParseExact(baselineRef[^10..], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            ReleaseSigningPolicy signingPolicy = ReadSigningPolicy(Path.Combine(trusted.Bundle.Root, "trust", "release-signing-policy.yaml"));
            TrustedSshSigner signer = ReadTrustedSigner(Path.Combine(trusted.Bundle.Root, "trust", "allowed-signers"), signingPolicy, verification.Principal, verificationDate);
            TrustPolicyResult authorization = SshSignerPolicy.Authorize([signer], new SshTagAuthorizationRequest(true, true, verification.Principal, "release", signer.KeyFingerprint, signer.Algorithm, verificationDate, expectedTagObjectId, observedTagObjectId, ExistingTagObjectId(root, baselineRef)));
            if (!authorization.IsValid) return Reject(output, authorization.Diagnostic!);

            string baselineFileName = Path.GetFileName(baselineRef + ".v1.json");
            string evidencePath = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "baselines", baselineFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            byte[] manifest = BuildManifest(baselineRef, targetOid, observedTagObjectId, signer);
            if (File.Exists(evidencePath))
            {
                if (!ReadFileBounded(evidencePath).AsSpan().SequenceEqual(manifest)) return Reject(output, "release_baseline_evidence_stale");
            }
            else
            {
                WriteAtomic(evidencePath, manifest);
            }

            output.WriteLine($"release_baseline_verified: {Path.GetRelativePath(root, evidencePath).Replace(Path.DirectorySeparatorChar, '/')}");
            return Program.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.DecoderFallbackException or JsonException or InvalidOperationException)
        {
            return Reject(output, "release_baseline_input_invalid");
        }
    }

    private static byte[] BuildManifest(string baselineRef, string targetOid, string tagObjectId, TrustedSshSigner signer)
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = "release-baseline.v1",
            baselineRef,
            targetOid,
            tagObjectId,
            signerPrincipal = signer.Principal,
            signerRole = signer.Role,
            signerKeyFingerprint = signer.KeyFingerprint,
            signerAlgorithm = signer.Algorithm,
            signerValidFrom = signer.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            signerValidUntil = signer.ValidUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }, JsonOptions);
        return CanonicalArtifactPolicy.CanonicalizeJson(json).Bytes ?? throw new InvalidOperationException("release_baseline_not_canonical");
    }

    private static TrustedBundleResult VerifyBundle(string candidateRoot)
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;
        return TrustedBundlePolicy.Verify(new TrustedBundleVerificationRequest(Required("ISLAMU_RELEASE_TRUSTED_BUNDLE"), candidateRoot, new PromotionAuthorityInput(Required("ISLAMU_RELEASE_PROMOTION_RECEIPT"), Required("ISLAMU_RELEASE_PROMOTION_SIGNATURE"), Required("ISLAMU_RELEASE_PROMOTION_PRINCIPAL")), Required("ISLAMU_RELEASE_BUNDLE_ID"), Required("ISLAMU_RELEASE_BUNDLE_VERSION"), Required("ISLAMU_RELEASE_POLICY_VERSION"), Required("ISLAMU_RELEASE_CONFIG_VERSION"), Required("ISLAMU_RELEASE_TRUST_VERSION")) { ExpectedManifestDigest = Required("ISLAMU_RELEASE_MANIFEST_SHA256") });
    }

    private static ReleaseSigningPolicy ReadSigningPolicy(string path)
    {
        string releasePrincipal = string.Empty;
        DateOnly? validFrom = null;
        DateOnly? validUntil = null;
        foreach (string line in ReadLinesBounded(path))
        {
            string trimmed = line.Trim();
            if (releasePrincipal.Length == 0 && trimmed.StartsWith("principal:", StringComparison.Ordinal)) releasePrincipal = trimmed["principal:".Length..].Trim();
            else if (trimmed.StartsWith("validFrom:", StringComparison.Ordinal)) validFrom = DateOnly.ParseExact(trimmed["validFrom:".Length..].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            else if (trimmed.StartsWith("validUntil:", StringComparison.Ordinal)) validUntil = DateOnly.ParseExact(trimmed["validUntil:".Length..].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (releasePrincipal.Length == 0) throw new InvalidOperationException();
        return new ReleaseSigningPolicy(releasePrincipal, validFrom, validUntil);
    }

    private static TrustedSshSigner ReadTrustedSigner(string signerPolicyPath, ReleaseSigningPolicy policy, string observedPrincipal, DateOnly releaseDate)
    {
        foreach (string rawLine in ReadLinesBounded(signerPolicyPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0] != observedPrincipal) continue;
            int keyIndex = Array.FindIndex(parts, 1, item => item.StartsWith("ssh-", StringComparison.Ordinal));
            DateOnly validFrom = policy.ValidFrom ?? releaseDate;
            DateOnly validUntil = policy.ValidUntil ?? releaseDate;
            foreach (string option in string.Join(',', parts[1..keyIndex]).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (policy.ValidFrom is null && option.StartsWith("valid-after=\"", StringComparison.Ordinal)) validFrom = DateOnly.ParseExact(option[13..^1], "yyyyMMdd", CultureInfo.InvariantCulture);
                if (policy.ValidUntil is null && option.StartsWith("valid-before=\"", StringComparison.Ordinal)) validUntil = DateOnly.ParseExact(option[14..^1], "yyyyMMdd", CultureInfo.InvariantCulture);
            }
            string role = observedPrincipal == policy.ReleasePrincipal ? "release" : string.Empty;
            return new TrustedSshSigner(observedPrincipal, role, PublicKeyFingerprint(parts[keyIndex], parts[keyIndex + 1]), "ssh-ed25519", validFrom, validUntil, null);
        }
        throw new InvalidOperationException();
    }

    private static SshVerification VerifyTagSignature(string root, string tagName, string signerPolicyPath)
    {
        string output = RunProcessAllowError("git", root, "-c", "gpg.format=ssh", "-c", $"gpg.ssh.allowedSignersFile={signerPolicyPath}", "verify-tag", "-v", tagName);
        Match match = GoodSignaturePattern.Match(output);
        return match.Success ? new SshVerification(true, match.Groups["principal"].Value) : new SshVerification(false, string.Empty);
    }

    private static TagObject ParseTagObject(string raw)
    {
        string[] parts = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split("\n\n", 2, StringSplitOptions.None);
        Dictionary<string, string> headers = parts[0].Split('\n').Select(line => line.Split(' ', 2)).Where(item => item.Length == 2).ToDictionary(item => item[0], item => item[1], StringComparer.Ordinal);
        return new TagObject(headers.GetValueOrDefault("object", string.Empty), headers.GetValueOrDefault("tag", string.Empty), SignaturePattern.Split(parts[1], 2)[0]);
    }

    private static string? ExistingTagObjectId(string root, string baselineRef)
    {
        string baselineFileName = Path.GetFileName(baselineRef + ".v1.json");
        string path = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "baselines", baselineFileName);
        if (!File.Exists(path)) return null;
        using JsonDocument document = JsonDocument.Parse(ReadFileBounded(path));
        return document.RootElement.TryGetProperty("tagObjectId", out JsonElement value) ? value.GetString() : null;
    }

    private static string PublicKeyFingerprint(string algorithm, string key)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"islamu-release-key-{Guid.NewGuid():N}.pub");
        try
        {
            File.WriteAllText(temporary, $"{algorithm} {key}\n");
            return RunProcess("/usr/bin/ssh-keygen", null, "-lf", temporary).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string RunGit(string repositoryRoot, params string[] arguments) => RunProcess("git", repositoryRoot, arguments);
    private static string RunProcess(string executable, string? workingDirectory, params string[] arguments)
    {
        (int exitCode, string output) = RunProcessCore(executable, workingDirectory, arguments);
        if (exitCode != 0) throw new IOException();
        return output;
    }

    private static string RunProcessAllowError(string executable, string? workingDirectory, params string[] arguments) => RunProcessCore(executable, workingDirectory, arguments).Output;

    private static (int ExitCode, string Output) RunProcessCore(string executable, string? workingDirectory, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory } };
        string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        if (executable == "git") { process.StartInfo.ArgumentList.Add("--no-replace-objects"); process.StartInfo.ArgumentList.Add("-c"); process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}"); }
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> stdout = ReadBoundedAsync(process.StandardOutput);
        Task<string> stderr = ReadBoundedAsync(process.StandardError);
        if (!process.WaitForExit(TimeSpan.FromSeconds(5))) { process.Kill(entireProcessTree: true); throw new IOException(); }
        Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(1));
        string combined = stdout.Result + stderr.Result;
        return (process.ExitCode, combined);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[MaximumGitOutputCharacters];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count));
            if (read == 0) return new string(buffer, 0, count);
            count += read;
        }
        throw new IOException();
    }

    private static string[] ReadLinesBounded(string path) => StrictUtf8.GetString(ReadFileBounded(path)).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    private static byte[] ReadFileBounded(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan); if (stream.Length > MaximumArtifactBytes) throw new IOException(); var bytes = new byte[stream.Length]; stream.ReadExactly(bytes); return bytes; }
    private static void WriteAtomic(string path, byte[] bytes) { string temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp"); try { File.WriteAllBytes(temporaryPath, bytes); File.Move(temporaryPath, path); } finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } }
    private static string ResolveDirectory(string path) { string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); if (!Directory.Exists(full)) throw new IOException(); return full; }
    private static int Reject(TextWriter output, string diagnostic) { output.WriteLine($"verify_baseline_failed: {diagnostic}"); return Program.ToolchainRejected; }
    private sealed record ReleaseSigningPolicy(string ReleasePrincipal, DateOnly? ValidFrom, DateOnly? ValidUntil);
    private sealed record SshVerification(bool Verified, string Principal);
    private sealed record TagObject(string TargetOid, string Name, string Message);
}
