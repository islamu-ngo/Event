using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Genesis trust activation. Until this runs, <c>eng/release/trust/</c> is comment-only and every
/// attestation path fails closed, which is the correct default: a release-signing root asserted by
/// nobody is worse than no root at all.
///
/// The command takes two <em>public</em> keys and refuses to proceed unless they belong to two
/// different principals with different key material and different fingerprints. That is not a
/// nicety — <c>separationOfDuty.releaseSignerCannotPromoteOwnCandidateBundle</c> means one person
/// must never be able to both promote the tooling bundle and sign the release it attests, because
/// then a single compromised key forges the entire chain. This is why activation is inherently a
/// two-person act and cannot be completed by any single operator or agent.
/// </summary>
public static class TrustActivationCommand
{
    private const int MaximumKeyBytes = 16 * 1024;
    private const string ReleaseNamespace = "git";
    private const string PromotionNamespace = "islamu-release-promotion";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex PrincipalPattern = new("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex PublicKeyPattern = new("^(?<algorithm>ssh-[a-z0-9-]+) (?<key>[A-Za-z0-9+/]+={0,3})(?: (?<comment>\\S.*))?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));

    public static int Run(string[] args, TextWriter output, string workingDirectory)
    {
        try
        {
            ActivationOptions options = ParseOptions(args, workingDirectory);
            SignerKey release = ReadPublicKey(options.ReleaseKeyPath);
            SignerKey promotion = ReadPublicKey(options.PromotionKeyPath);

            ValidateSeparationOfDuty(options, release, promotion);

            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["allowed-signers"] = BuildAllowedSigners(options.ReleasePrincipal, ReleaseNamespace, release, options.ValidFrom, options.ValidUntil),
                ["promotion-allowed-signers"] = BuildAllowedSigners(options.PromotionPrincipal, PromotionNamespace, promotion, validFrom: null, validUntil: null),
                ["release-signing-policy.yaml"] = BuildSigningPolicy(options),
            };

            WriteActivation(options, files);

            output.WriteLine($"trust_activated: output={options.OutputDirectory.Replace(Path.DirectorySeparatorChar, '/')}");
            output.WriteLine($"trust_release_signer: principal={options.ReleasePrincipal} algorithm={release.Algorithm} fingerprint={release.Fingerprint} valid-from={Format(options.ValidFrom)} valid-until={Format(options.ValidUntil)}");
            output.WriteLine($"trust_promotion_signer: principal={options.PromotionPrincipal} algorithm={promotion.Algorithm} fingerprint={promotion.Fingerprint}");
            output.WriteLine("trust_next_step: promote a trusted bundle whose manifest is signed by the promotion principal, then verify a release tag signed by the release principal");
            return Program.Success;
        }
        catch (TrustActivationException exception)
        {
            output.WriteLine($"activate_trust_failed: {exception.Code}");
            return Program.ToolchainRejected;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException or FormatException)
        {
            output.WriteLine("activate_trust_failed: trust_activation_input_invalid");
            return Program.ToolchainRejected;
        }
    }

    private static ActivationOptions ParseOptions(string[] args, string workingDirectory)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var replace = false;
        for (var index = 1; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--replace", StringComparison.Ordinal))
            {
                replace = true;
                continue;
            }

            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)) throw new TrustActivationException("trust_activation_usage_invalid");
            if (!values.TryAdd(args[index], args[++index])) throw new TrustActivationException("trust_activation_usage_invalid");
        }

        string Required(string name) => values.TryGetValue(name, out string? value) && value.Length != 0 ? value : throw new TrustActivationException("trust_activation_usage_invalid");

        var options = new ActivationOptions(
            Required("--release-principal"),
            ResolvePath(workingDirectory, Required("--release-key")),
            Required("--promotion-principal"),
            ResolvePath(workingDirectory, Required("--promotion-key")),
            ParseDate(Required("--valid-from")),
            ParseDate(Required("--valid-until")),
            ResolvePath(workingDirectory, Required("--output")),
            replace);

        if (values.Count != 7) throw new TrustActivationException("trust_activation_usage_invalid");
        return options;
    }

    private static void ValidateSeparationOfDuty(ActivationOptions options, SignerKey release, SignerKey promotion)
    {
        if (!PrincipalPattern.IsMatch(options.ReleasePrincipal) || !PrincipalPattern.IsMatch(options.PromotionPrincipal))
        {
            throw new TrustActivationException("trust_activation_principal_invalid");
        }

        if (options.ValidFrom > options.ValidUntil) throw new TrustActivationException("trust_activation_validity_invalid");

        // One key must never be able to both promote the tooling and sign the release it attests.
        if (string.Equals(options.ReleasePrincipal, options.PromotionPrincipal, StringComparison.Ordinal) ||
            string.Equals(release.Key, promotion.Key, StringComparison.Ordinal) ||
            string.Equals(release.Fingerprint, promotion.Fingerprint, StringComparison.Ordinal))
        {
            throw new TrustActivationException("trust_activation_separation_of_duty");
        }
    }

    private static SignerKey ReadPublicKey(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumKeyBytes || IsLink(path)) throw new TrustActivationException("trust_activation_key_missing");

        string text = StrictUtf8.GetString(File.ReadAllBytes(path));

        // Guard the most damaging operator mistake there is: handing this command a private key.
        if (text.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new TrustActivationException("trust_activation_private_key_supplied");

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .ToArray();
        if (lines.Length != 1) throw new TrustActivationException("trust_activation_key_malformed");

        Match match = PublicKeyPattern.Match(lines[0]);
        if (!match.Success) throw new TrustActivationException("trust_activation_key_malformed");

        string algorithm = match.Groups["algorithm"].Value;
        if (!string.Equals(algorithm, "ssh-ed25519", StringComparison.Ordinal)) throw new TrustActivationException("trust_activation_algorithm_forbidden");

        return new SignerKey(algorithm, match.Groups["key"].Value, Fingerprint(algorithm, match.Groups["key"].Value));
    }

    /// <summary>
    /// The release signer carries an explicit validity window because the signing policy models one
    /// and every tag verification checks the release date against it. The promotion signer does not:
    /// the policy has no validity for that role, so writing one here would create a constraint
    /// nothing enforces or renews, and bundle promotion would silently start failing on expiry.
    /// </summary>
    private static string BuildAllowedSigners(string principal, string signatureNamespace, SignerKey key, DateOnly? validFrom, DateOnly? validUntil)
    {
        string validity = validFrom is not null && validUntil is not null
            ? $",valid-after=\"{validFrom.Value:yyyyMMdd}\",valid-before=\"{validUntil.Value:yyyyMMdd}\""
            : string.Empty;
        return $"# ABOUTME: Activated production SSH allowed-signers root for the {signatureNamespace} namespace.\n" +
            "# ABOUTME: Public key material only; private keys never belong in this repository or a trusted bundle.\n" +
            $"{principal} namespaces=\"{signatureNamespace}\"{validity} {key.Algorithm} {key.Key}\n";
    }

    private static string BuildSigningPolicy(ActivationOptions options) =>
        "# ABOUTME: Defines the active SSH signer roles, validity, rotation, revocation, and tag-integrity contract.\n" +
        "# ABOUTME: Principals and validity are operator-reviewed; public keys live in the allowed-signers roots.\n" +
        "schemaVersion: release-signing-policy.v1\n" +
        "status: active\n" +
        "allowedAlgorithms:\n" +
        "  - ssh-ed25519\n" +
        "roles:\n" +
        "  release:\n" +
        "    tagPattern: v<major>.<minor>.<patch>[-prerelease]\n" +
        "    tagKind: annotated\n" +
        $"    namespace: {ReleaseNamespace}\n" +
        $"    principal: {options.ReleasePrincipal}\n" +
        "    algorithm: ssh-ed25519\n" +
        $"    validFrom: {Format(options.ValidFrom)}\n" +
        $"    validUntil: {Format(options.ValidUntil)}\n" +
        "  tooling-promotion:\n" +
        "    tagPattern: release-tooling/v<major>.<minor>.<patch>\n" +
        "    tagKind: annotated\n" +
        $"    namespace: {PromotionNamespace}\n" +
        $"    principal: {options.PromotionPrincipal}\n" +
        "separationOfDuty:\n" +
        "  genesisIndependentReviewRequired: true\n" +
        "  releaseSignerCannotPromoteOwnCandidateBundle: true\n" +
        "rotation:\n" +
        "  uniquePrincipalPerActiveKey: true\n" +
        "  uniqueFingerprint: true\n" +
        "  overlapRequiresDistinctKeys: true\n" +
        "  validityIsInclusive: true\n" +
        "revocation:\n" +
        "  effectiveOnOrAfterRecordedDate: true\n" +
        "  removeFromAllowedSigners: true\n" +
        "  appendRotationHistory: true\n" +
        "tagIntegrity:\n" +
        "  rejectLightweight: true\n" +
        "  rejectUnsigned: true\n" +
        "  rejectReplacedObjectId: true\n" +
        "  rejectRecreatedObjectId: true\n";

    private static void WriteActivation(ActivationOptions options, IReadOnlyDictionary<string, string> files)
    {
        if (!Directory.Exists(options.OutputDirectory) || IsLink(options.OutputDirectory)) throw new TrustActivationException("trust_activation_output_invalid");

        // Re-running with identical inputs is a no-op. Replacing a different existing trust root is
        // a rotation, not an activation, and must be an explicit decision.
        foreach ((string name, string content) in files)
        {
            string path = Path.Combine(options.OutputDirectory, name);
            if (!File.Exists(path)) continue;
            if (IsLink(path)) throw new TrustActivationException("trust_activation_output_invalid");
            if (string.Equals(StrictUtf8.GetString(File.ReadAllBytes(path)), content, StringComparison.Ordinal)) continue;
            if (!options.Replace) throw new TrustActivationException("trust_activation_would_replace_existing_root");
        }

        foreach ((string name, string content) in files)
        {
            WriteAtomic(Path.Combine(options.OutputDirectory, name), StrictUtf8.GetBytes(content));
        }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        string temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string Fingerprint(string algorithm, string key)
    {
        // Matches `ssh-keygen -lf` so the value is identical to what TagCommand records in evidence.
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(key);
        }
        catch (FormatException)
        {
            throw new TrustActivationException("trust_activation_key_malformed");
        }

        _ = algorithm;
        return $"SHA256:{Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=')}";
    }

    private static DateOnly ParseDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
            ? parsed
            : throw new TrustActivationException("trust_activation_validity_invalid");

    private static string Format(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ResolvePath(string workingDirectory, string path) =>
        Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(workingDirectory, path));

    private static bool IsLink(string path) => (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private sealed record ActivationOptions(
        string ReleasePrincipal,
        string ReleaseKeyPath,
        string PromotionPrincipal,
        string PromotionKeyPath,
        DateOnly ValidFrom,
        DateOnly ValidUntil,
        string OutputDirectory,
        bool Replace);

    private sealed record SignerKey(string Algorithm, string Key, string Fingerprint);

    private sealed class TrustActivationException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
