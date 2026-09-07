// ABOUTME: Verifies SSH-signed annotated release tags and writes deterministic final release evidence.
// ABOUTME: Generates canonical tag messages from committed release sources and candidate manifests.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public static class TagCommand
{
    private const int MaximumGitOutputCharacters = 131_072;
    private const int MaximumArtifactBytes = 1_048_576;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SignaturePattern = new("\\n-----BEGIN SSH SIGNATURE-----\\n", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex GoodSignaturePattern = new("Good \\\"git\\\" signature for (?<principal>[^\\s]+)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static int Run(string[] args, TextWriter output, string repositoryRoot, string platform, TimeSpan timeout)
    {
        if (args.Length > 0 && string.Equals(args[0], "tag-message", StringComparison.Ordinal))
        {
            return GenerateTagMessage(args, output, repositoryRoot);
        }

        if (args.Length == 3)
        {
            try
            {
                string root = ResolveDirectory(repositoryRoot);
                CandidateManifest candidate = ReadCandidateManifest(ResolveReleaseDirectory(root, args[1]));
                string tagName = $"v{candidate.Version}";
                if (!string.Equals(args[2], tagName, StringComparison.Ordinal)) return Reject(output, "release_tag_name_mismatch");
                string tagObjectId = RunGit(root, "rev-parse", "--verify", $"refs/tags/{tagName}^{{object}}").Trim();
                return Run(["verify-tag", args[1], candidate.CandidateOid, tagObjectId], output, repositoryRoot, platform, timeout);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException or JsonException or InvalidOperationException)
            {
                return Reject(output, "release_tag_input_invalid");
            }
        }

        if (args.Length is not 4 and not 5)
        {
            output.WriteLine("invalid_arguments: verify-tag requires release directory and tag name");
            return Program.UsageError;
        }

        try
        {
            string root = ResolveDirectory(repositoryRoot);
            string releaseDirectory = ResolveReleaseDirectory(root, args[1]);
            string candidateOid = args[2];
            string expectedTagObjectId = args[3];
            string? expectedPreviousPublishedTagObjectId = args.Length == 5 ? args[4] : null;
            if (!FullOidPattern.IsMatch(candidateOid) || !FullOidPattern.IsMatch(expectedTagObjectId) || (expectedPreviousPublishedTagObjectId is not null && !FullOidPattern.IsMatch(expectedPreviousPublishedTagObjectId)))
            {
                return Reject(output, "release_tag_object_invalid");
            }

            TrustedBundleResult trusted = VerifyBundle(root);
            if (!trusted.IsValid || trusted.Bundle is null)
            {
                return Reject(output, "release_trusted_bundle_invalid");
            }

            BundleDigests bundleDigests = ReadBundleDigests(trusted.Bundle.Root);
            CandidateManifest candidate = ReadCandidateManifest(releaseDirectory);
            if (!string.Equals(candidate.CandidateOid, candidateOid, StringComparison.Ordinal)) return Reject(output, "release_candidate_oid_mismatch");
            if (!string.Equals(candidate.TrustedBundleManifestSha256, trusted.ManifestDigest, StringComparison.Ordinal)) return Reject(output, "release_candidate_manifest_drift");
            if (!string.Equals(candidate.TrustedBundlePolicySha256, bundleDigests.PolicySha256, StringComparison.Ordinal)) return Reject(output, "release_policy_hash_mismatch");
            if (!string.Equals(candidate.TrustedBundleTrustSha256, bundleDigests.TrustSha256, StringComparison.Ordinal)) return Reject(output, "release_trust_hash_mismatch");
            if (!string.Equals(candidate.TrustedBundleToolchainSha256, trusted.Bundle.ToolchainLockDigest, StringComparison.Ordinal)) return Reject(output, "release_toolchain_hash_mismatch");
            if (!string.Equals(candidate.TrustedBundleGitCliffSha256, bundleDigests.GitCliffSha256, StringComparison.Ordinal)) return Reject(output, "release_tool_hash_mismatch");

            string releasePath = ResolveChild(releaseDirectory, "release.yaml", mustExist: true);
            string summaryPath = ResolveChild(releaseDirectory, "summary.md", mustExist: true);
            string contextPath = ResolveChild(releaseDirectory, "release-context.v1.json", mustExist: true);
            string notesPath = ResolveChild(releaseDirectory, "release-notes.md", mustExist: true);
            string candidatePath = ResolveChild(releaseDirectory, "release-candidate.v1.json", mustExist: true);
            string finalPath = ResolveChild(releaseDirectory, "release-evidence.v1.json", mustExist: false);
            byte[] summaryBytes = ReadFileBounded(summaryPath);
            byte[] contextBytes = ReadFileBounded(contextPath);
            byte[] notesBytes = ReadFileBounded(notesPath);
            byte[] candidateBytes = ReadFileBounded(candidatePath);
            string candidateDigest = Sha256(candidateBytes);
            if (!string.Equals(candidate.ReleaseDescriptorSha256, Sha256(StrictUtf8.GetBytes(ReadCanonicalText(releasePath))), StringComparison.Ordinal)) return Reject(output, "release_descriptor_hash_mismatch");
            if (!string.Equals(candidate.ReleaseSummarySha256, Sha256(summaryBytes), StringComparison.Ordinal)) return Reject(output, "release_summary_hash_mismatch");
            if (!string.Equals(candidate.ReleaseContextSha256, Sha256(contextBytes), StringComparison.Ordinal)) return Reject(output, "release_context_hash_mismatch");
            if (!string.Equals(candidate.ReleaseNotesSha256, Sha256(notesBytes), StringComparison.Ordinal)) return Reject(output, "release_notes_hash_mismatch");
            if (!IsExactCommittedFile(root, candidateOid, releasePath) || !IsExactCommittedFile(root, candidateOid, summaryPath) || !IsExactCommittedFile(root, candidateOid, contextPath) || !IsExactCommittedFile(root, candidateOid, notesPath))
            {
                return Reject(output, "release_committed_artifact_mismatch");
            }

            string tagName = $"v{candidate.Version}";
            Dictionary<string, string>? expectedPriorTags = expectedPreviousPublishedTagObjectId is null
                ? null
                : new Dictionary<string, string> { [candidate.PreviousPublishedTag] = expectedPreviousPublishedTagObjectId };
            GitReleaseValidationResult git = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
                root,
                candidate.Line,
                candidate.Version,
                candidate.BaseStableRef,
                candidate.PreviousPublishedRef,
                candidateOid,
                ExpectedTagObjectOids: expectedPriorTags),
                timeout);
            if (!git.IsValid || git.Identity is null) return Reject(output, FirstDiagnostic(git.Diagnostics));
            if (!string.Equals(RunGit(root, "rev-parse", "--verify", $"{candidate.PreviousPublishedRef}^{{commit}}").Trim(), candidate.PreviousPublishedOid, StringComparison.Ordinal))
            {
                return Reject(output, $"git_previous_tag_target_mismatch:{candidate.PreviousPublishedTag}");
            }
            if (!string.Equals(RunGit(root, "rev-parse", "--verify", $"{candidate.BaseStableRef}^{{commit}}").Trim(), candidate.BaseStableOid, StringComparison.Ordinal))
            {
                return Reject(output, $"git_base_stable_tag_target_mismatch:{candidate.BaseStableTag}");
            }

            string tagType = RunGit(root, "cat-file", "-t", expectedTagObjectId).Trim();
            if (!string.Equals(tagType, "tag", StringComparison.Ordinal)) return Reject(output, "release_tag_not_annotated");
            string observedTagObjectId = RunGit(root, "rev-parse", "--verify", $"{expectedTagObjectId}^{{object}}").Trim();
            if (!string.Equals(observedTagObjectId, expectedTagObjectId, StringComparison.Ordinal)) return Reject(output, "release_tag_object_replaced");
            string targetOid = RunGit(root, "rev-parse", "--verify", $"{expectedTagObjectId}^{{commit}}").Trim();
            if (!string.Equals(targetOid, candidateOid, StringComparison.Ordinal)) return Reject(output, "release_tag_wrong_target");

            TagObject tag = ParseTagObject(RunGit(root, "cat-file", "-p", observedTagObjectId));
            if (!string.Equals(tag.Name, tagName, StringComparison.Ordinal)) return Reject(output, "release_tag_name_mismatch");
            string tagRef = $"refs/tags/{tagName}";
            if (!string.Equals(RunGit(root, "rev-parse", "--verify", $"{tagRef}^{{object}}").Trim(), observedTagObjectId, StringComparison.Ordinal)) return Reject(output, "release_tag_object_replaced");
            if (!string.Equals(tag.TargetOid, candidateOid, StringComparison.Ordinal)) return Reject(output, "release_tag_wrong_target");
            string expectedMessage = BuildTagMessage(candidate, candidateDigest);
            if (!string.Equals(NormalizeTagMessage(tag.Message), NormalizeTagMessage(expectedMessage), StringComparison.Ordinal)) return Reject(output, "release_tag_message_mismatch");

            ReleaseSigningPolicy signingPolicy = ReadSigningPolicy(Path.Combine(trusted.Bundle.Root, "trust", "release-signing-policy.yaml"));
            SshVerification verification = VerifyTagSignature(root, observedTagObjectId, Path.Combine(trusted.Bundle.Root, "trust", "allowed-signers"));
            if (!verification.Verified) return Reject(output, "release_tag_signature_invalid");
            TrustedSshSigner signer = ReadTrustedSigner(Path.Combine(trusted.Bundle.Root, "trust", "allowed-signers"), signingPolicy, verification.Principal, candidate.ReleaseDate);
            TrustPolicyResult authorization = SshSignerPolicy.Authorize([signer], new SshTagAuthorizationRequest(
                IsAnnotatedTag: true,
                CryptographicSignatureVerified: true,
                Principal: verification.Principal,
                RequiredRole: "release",
                KeyFingerprint: signer.KeyFingerprint,
                Algorithm: signer.Algorithm,
                VerificationDate: candidate.ReleaseDate,
                ExpectedTagObjectId: expectedTagObjectId,
                ObservedTagObjectId: observedTagObjectId,
                PreviouslyRecordedTagObjectId: ExistingTagObjectId(finalPath)));
            if (!authorization.IsValid) return Reject(output, authorization.Diagnostic!);

            byte[] manifest = BuildManifest(candidate, git.Identity, trusted.Bundle, bundleDigests, candidateDigest, observedTagObjectId, targetOid, signer, summaryBytes, contextBytes, notesBytes);
            if (!string.Equals(RunGit(root, "rev-parse", "--verify", $"{tagRef}^{{object}}").Trim(), observedTagObjectId, StringComparison.Ordinal)) return Reject(output, "release_tag_object_replaced");
            if (File.Exists(finalPath))
            {
                if (!ReadFileBounded(finalPath).AsSpan().SequenceEqual(manifest)) return Reject(output, "release_evidence_manifest_stale");
            }
            else
            {
                WriteAtomic(finalPath, manifest);
            }

            output.WriteLine($"release_tag_verified: {Path.GetRelativePath(root, finalPath).Replace(Path.DirectorySeparatorChar, '/')}");
            return Program.Success;
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("release_", StringComparison.Ordinal))
        {
            return Reject(output, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException or JsonException or InvalidOperationException)
        {
            return Reject(output, "release_tag_input_invalid");
        }
    }

    private static int GenerateTagMessage(string[] args, TextWriter output, string repositoryRoot)
    {
        if (args.Length != 2)
        {
            output.WriteLine("invalid_arguments: tag-message requires release directory");
            return Program.UsageError;
        }

        try
        {
            string root = ResolveDirectory(repositoryRoot);
            string releaseDirectory = ResolveReleaseDirectory(root, args[1]);
            CandidateManifest candidate = ReadCandidateManifest(releaseDirectory);
            byte[] candidateBytes = ReadFileBounded(Path.Combine(releaseDirectory, "release-candidate.v1.json"));
            output.Write(BuildTagMessage(candidate, Sha256(candidateBytes)));
            return Program.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException or JsonException or InvalidOperationException)
        {
            output.WriteLine("tag_message_failed: release_tag_input_invalid");
            return Program.ToolchainRejected;
        }
    }

    private static string BuildTagMessage(CandidateManifest candidate, string candidateDigest) =>
        $"ISLAMU release tag v1\nTag: v{candidate.Version}\nVersion: {candidate.Version}\nLine: {candidate.Line}\nCandidate-Oid: {candidate.CandidateOid}\nCandidate-SHA256: {candidateDigest}\nRelease-Notes-SHA256: {candidate.ReleaseNotesSha256}\n";

    private static byte[] BuildManifest(CandidateManifest candidate, GitRepositoryObjectIdentity git, VerifiedTrustedBundle trusted, BundleDigests bundleDigests, string candidateDigest, string tagObjectId, string targetOid, TrustedSshSigner signer, byte[] summaryBytes, byte[] contextBytes, byte[] notesBytes)
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = "release-evidence.v1",
            objectFormat = candidate.ObjectFormat,
            oidLength = candidate.OidLength,
            version = candidate.Version,
            line = candidate.Line,
            releaseDate = candidate.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            tagName = $"v{candidate.Version}",
            tagObjectId,
            targetOid,
            candidateOid = candidate.CandidateOid,
            candidateManifestSha256 = candidateDigest,
            candidateManifestSchemaVersion = "release-candidate.v1",
            previousPublishedTag = candidate.PreviousPublishedTag,
            previousPublishedOid = candidate.PreviousPublishedOid,
            previousPublishedTagObjectId = git.PreviousPublishedTagObjectOid,
            baseStableTag = candidate.BaseStableTag,
            baseStableOid = candidate.BaseStableOid,
            baseStableTagObjectId = git.BaseStableTagObjectOid,
            trustedBundleManifestSha256 = trusted.ManifestDigest,
            trustedBundlePolicySha256 = bundleDigests.PolicySha256,
            trustedBundleConfigSha256 = trusted.ConfigDigest,
            trustedBundleTrustSha256 = bundleDigests.TrustSha256,
            trustedBundleToolchainSha256 = trusted.ToolchainLockDigest,
            trustedBundleGitCliffSha256 = bundleDigests.GitCliffSha256,
            releaseDescriptorSha256 = candidate.ReleaseDescriptorSha256,
            releaseFragmentsSha256 = candidate.ReleaseFragmentsSha256,
            releaseSummarySha256 = Sha256(summaryBytes),
            releaseContextSha256 = Sha256(contextBytes),
            releaseNotesSha256 = Sha256(notesBytes),
            signerPrincipal = signer.Principal,
            signerRole = signer.Role,
            signerKeyFingerprint = signer.KeyFingerprint,
            signerAlgorithm = signer.Algorithm,
            signerValidFrom = signer.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            signerValidUntil = signer.ValidUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }, JsonOptions);
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(json);
        if (!canonical.IsValid || canonical.Bytes is null) throw new InvalidOperationException("release_evidence_not_canonical");
        return canonical.Bytes;
    }

    private static CandidateManifest ReadCandidateManifest(string releaseDirectory)
    {
        string path = Path.Combine(releaseDirectory, "release-candidate.v1.json");
        byte[] bytes = ReadFileBounded(path);
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(StrictUtf8.GetString(bytes));
        if (!canonical.IsValid || canonical.Bytes is null || !bytes.AsSpan().SequenceEqual(canonical.Bytes)) throw new InvalidOperationException("release_candidate_manifest_invalid");
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetString() != "release-candidate.v1") throw new InvalidOperationException("release_candidate_manifest_invalid");
            return new CandidateManifest(
            root.GetProperty("objectFormat").GetString()!,
            root.GetProperty("oidLength").GetInt32(),
            root.GetProperty("version").GetString()!,
            root.GetProperty("line").GetString()!,
            DateOnly.ParseExact(root.GetProperty("releaseDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            root.GetProperty("candidateOid").GetString()!,
            root.GetProperty("baseStableTag").GetString()!,
            root.GetProperty("baseStableRef").GetString()!,
            root.GetProperty("baseStableOid").GetString()!,
            root.GetProperty("previousPublishedTag").GetString()!,
            root.GetProperty("previousPublishedRef").GetString()!,
            root.GetProperty("previousPublishedOid").GetString()!,
            root.GetProperty("trustedBundleManifestSha256").GetString()!,
            root.GetProperty("trustedBundlePolicySha256").GetString()!,
            root.GetProperty("trustedBundleConfigSha256").GetString()!,
            root.GetProperty("trustedBundleTrustSha256").GetString()!,
            root.GetProperty("trustedBundleToolchainSha256").GetString()!,
            root.GetProperty("trustedBundleGitCliffSha256").GetString()!,
            root.GetProperty("releaseDescriptorSha256").GetString()!,
            root.GetProperty("releaseFragmentsSha256").GetString()!,
            root.GetProperty("releaseSummarySha256").GetString()!,
            root.GetProperty("releaseContextSha256").GetString()!,
            root.GetProperty("releaseNotesSha256").GetString()!);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException("release_candidate_manifest_invalid", exception);
        }
    }

    private static BundleDigests ReadBundleDigests(string bundleRoot)
    {
        using JsonDocument document = JsonDocument.Parse(ReadFileBounded(Path.Combine(bundleRoot, "trusted-bundle.manifest.json")));
        Dictionary<string, string> files = document.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(item => item.GetProperty("path").GetString()!, item => item.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
        string gitCliff = files.TryGetValue("git-cliff", out string? linux) ? linux : files.TryGetValue("git-cliff.exe", out string? windows) ? windows : string.Empty;
        if (gitCliff.Length == 0 || !files.TryGetValue("policy/release-policy.yaml", out string? policy) || !files.TryGetValue("trust/release-signing-policy.yaml", out string? trust)) throw new InvalidOperationException();
        return new BundleDigests(policy, trust, gitCliff);
    }

    private static ReleaseSigningPolicy ReadSigningPolicy(string path)
    {
        string releasePrincipal = string.Empty;
        string requiredAlgorithm = "ssh-ed25519";
        DateOnly? validFrom = null;
        DateOnly? validUntil = null;
        DateOnly? revokedOn = null;
        bool inRelease = false;
        foreach (string line in ReadLinesBounded(path))
        {
            string trimmed = line.Trim();
            if (trimmed == "release:") inRelease = true;
            else if (!line.StartsWith("    ", StringComparison.Ordinal) && trimmed.EndsWith(':')) inRelease = false;
            else if (inRelease && trimmed.StartsWith("principal:", StringComparison.Ordinal)) releasePrincipal = trimmed["principal:".Length..].Trim();
            else if (inRelease && trimmed.StartsWith("algorithm:", StringComparison.Ordinal)) requiredAlgorithm = trimmed["algorithm:".Length..].Trim();
            else if (inRelease && trimmed.StartsWith("validFrom:", StringComparison.Ordinal)) validFrom = DateOnly.ParseExact(trimmed["validFrom:".Length..].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            else if (inRelease && trimmed.StartsWith("validUntil:", StringComparison.Ordinal)) validUntil = DateOnly.ParseExact(trimmed["validUntil:".Length..].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            else if (inRelease && trimmed.StartsWith("revokedOn:", StringComparison.Ordinal)) revokedOn = DateOnly.ParseExact(trimmed["revokedOn:".Length..].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (releasePrincipal.Length == 0) throw new InvalidOperationException();
        return new ReleaseSigningPolicy(releasePrincipal, requiredAlgorithm, validFrom, validUntil, revokedOn);
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
            if (keyIndex < 1 || keyIndex + 1 >= parts.Length) throw new InvalidOperationException();
            DateOnly validFrom = policy.ValidFrom ?? releaseDate;
            DateOnly validUntil = policy.ValidUntil ?? releaseDate;
            foreach (string option in string.Join(',', parts[1..keyIndex]).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (policy.ValidFrom is null && option.StartsWith("valid-after=\"", StringComparison.Ordinal)) validFrom = ParseSshDate(option[13..^1]);
                if (policy.ValidUntil is null && option.StartsWith("valid-before=\"", StringComparison.Ordinal)) validUntil = ParseSshDate(option[14..^1]);
            }
            string role = string.Equals(observedPrincipal, policy.ReleasePrincipal, StringComparison.Ordinal) ? "release" : string.Empty;
            return new TrustedSshSigner(observedPrincipal, role, PublicKeyFingerprint(parts[keyIndex], parts[keyIndex + 1]), policy.RequiredAlgorithm, validFrom, validUntil, policy.RevokedOn);
        }
        throw new InvalidOperationException();
    }

    private static SshVerification VerifyTagSignature(string root, string tagName, string signerPolicyPath)
    {
        string output = RunGitAllowError(root, "-c", "gpg.format=ssh", "-c", $"gpg.ssh.allowedSignersFile={signerPolicyPath}", "verify-tag", "-v", tagName);
        Match match = GoodSignaturePattern.Match(output);
        return match.Success
            ? new SshVerification(true, match.Groups["principal"].Value)
            : new SshVerification(false, string.Empty);
    }

    private static TagObject ParseTagObject(string raw)
    {
        string normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] parts = normalized.Split("\n\n", 2, StringSplitOptions.None);
        if (parts.Length != 2) throw new IOException();
        Dictionary<string, string> headers = parts[0].Split('\n').Select(line => line.Split(' ', 2)).Where(item => item.Length == 2).ToDictionary(item => item[0], item => item[1], StringComparer.Ordinal);
        string message = SignaturePattern.Split(parts[1], 2)[0];
        return new TagObject(headers.GetValueOrDefault("object", string.Empty), headers.GetValueOrDefault("tag", string.Empty), message);
    }

    private static string NormalizeTagMessage(string message) => message.TrimEnd('\n') + "\n";

    private static string? ExistingTagObjectId(string finalPath)
    {
        if (!File.Exists(finalPath)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(ReadFileBounded(finalPath));
            return document.RootElement.TryGetProperty("tagObjectId", out JsonElement value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateOnly ParseSshDate(string value) => DateOnly.ParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture);

    private static string PublicKeyFingerprint(string algorithm, string key)
    {
        string temporary = Path.Combine(Path.GetTempPath(), $"islamu-release-key-{Guid.NewGuid():N}.pub");
        try
        {
            File.WriteAllText(temporary, $"{algorithm} {key}\n");
            string output = RunProcess("/usr/bin/ssh-keygen", null, "-lf", temporary).Trim();
            string[] parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new IOException();
            return parts[1];
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsExactCommittedFile(string repositoryRoot, string candidateOid, string path)
    {
        string relative = Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        string committed = RunGit(repositoryRoot, "rev-parse", "--verify", $"{candidateOid}:{relative}").Trim();
        string observed = RunGit(repositoryRoot, "hash-object", "--", relative).Trim();
        return FullOidPattern.IsMatch(committed) && string.Equals(committed, observed, StringComparison.Ordinal);
    }

    private static TrustedBundleResult VerifyBundle(string candidateRoot)
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;
        return TrustedBundlePolicy.Verify(new TrustedBundleVerificationRequest(
            Required("ISLAMU_RELEASE_TRUSTED_BUNDLE"),
            candidateRoot,
            new PromotionAuthorityInput(Required("ISLAMU_RELEASE_PROMOTION_RECEIPT"), Required("ISLAMU_RELEASE_PROMOTION_SIGNATURE"), Required("ISLAMU_RELEASE_PROMOTION_PRINCIPAL")),
            Required("ISLAMU_RELEASE_BUNDLE_ID"),
            Required("ISLAMU_RELEASE_BUNDLE_VERSION"),
            Required("ISLAMU_RELEASE_POLICY_VERSION"),
            Required("ISLAMU_RELEASE_CONFIG_VERSION"),
            Required("ISLAMU_RELEASE_TRUST_VERSION"))
        { ExpectedManifestDigest = Required("ISLAMU_RELEASE_MANIFEST_SHA256") });
    }

    private static string RunGit(string repositoryRoot, params string[] arguments)
    {
        string output = RunGitAllowError(repositoryRoot, arguments);
        if (output.StartsWith("__git_failed__", StringComparison.Ordinal)) throw new IOException();
        return output;
    }

    private static string RunGitAllowError(string repositoryRoot, params string[] arguments) => RunProcess("git", repositoryRoot, arguments);

    private static string RunProcess(string executable, string? workingDirectory, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory } };
        string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
        if (executable == "git")
        {
            process.StartInfo.ArgumentList.Add("--no-replace-objects");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
        }
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
            Task<string> stdout = ReadBoundedAsync(process.StandardOutput);
            Task<string> stderr = ReadBoundedAsync(process.StandardError);
            if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                process.Kill(entireProcessTree: true);
                throw new IOException();
            }
            Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(1));
            string combined = stdout.Result + stderr.Result;
            return process.ExitCode == 0 ? combined : "__git_failed__" + combined;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new IOException();
        }
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
        if (await reader.ReadAsync(new char[1]) != 0) throw new OperationCanceledException();
        return new string(buffer);
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        string temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string ReadCanonicalText(string path)
    {
        byte[] bytes = ReadFileBounded(path);
        string text = StrictUtf8.GetString(bytes);
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeText(text);
        if (!canonical.IsValid || canonical.Bytes is null || !bytes.AsSpan().SequenceEqual(canonical.Bytes)) throw new IOException();
        return text;
    }

    private static byte[] ReadFileBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        if (stream.Length > MaximumArtifactBytes) throw new IOException();
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string[] ReadLinesBounded(string path) =>
        StrictUtf8.GetString(ReadFileBounded(path)).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string ResolveReleaseDirectory(string root, string path)
    {
        string releaseDirectory = ResolveChild(root, path, mustExist: true);
        string expectedParent = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases");
        if (!string.Equals(Path.GetDirectoryName(releaseDirectory), expectedParent, PathComparison)) throw new IOException();
        return releaseDirectory;
    }

    private static string ResolveDirectory(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(full) || IsLink(full)) throw new IOException();
        return full;
    }

    private static string ResolveChild(string root, string child, bool mustExist)
    {
        string full = Path.GetFullPath(Path.Combine(root, child));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison) || (mustExist && !File.Exists(full) && !Directory.Exists(full)) || IsLink(full)) throw new IOException();
        return full;
    }

    private static bool IsLink(string path) => File.Exists(path) || Directory.Exists(path) ? (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 : false;
    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string FirstDiagnostic(IReadOnlyList<string> diagnostics) => diagnostics.Count == 0 ? "release_git_invalid" : diagnostics[0];
    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine($"verify_tag_failed: {diagnostic}");
        return Program.ToolchainRejected;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record BundleDigests(string PolicySha256, string TrustSha256, string GitCliffSha256);
    private sealed record ReleaseSigningPolicy(string ReleasePrincipal, string RequiredAlgorithm, DateOnly? ValidFrom, DateOnly? ValidUntil, DateOnly? RevokedOn);
    private sealed record SshVerification(bool Verified, string Principal);
    private sealed record TagObject(string TargetOid, string Name, string Message);
    private sealed record CandidateManifest(
        string ObjectFormat,
        int OidLength,
        string Version,
        string Line,
        DateOnly ReleaseDate,
        string CandidateOid,
        string BaseStableTag,
        string BaseStableRef,
        string BaseStableOid,
        string PreviousPublishedTag,
        string PreviousPublishedRef,
        string PreviousPublishedOid,
        string TrustedBundleManifestSha256,
        string TrustedBundlePolicySha256,
        string TrustedBundleConfigSha256,
        string TrustedBundleTrustSha256,
        string TrustedBundleToolchainSha256,
        string TrustedBundleGitCliffSha256,
        string ReleaseDescriptorSha256,
        string ReleaseFragmentsSha256,
        string ReleaseSummarySha256,
        string ReleaseContextSha256,
        string ReleaseNotesSha256);
}
