// ABOUTME: Builds disposable multi-release Git repositories with a promoted bundle and signed release tags.
// ABOUTME: Exposes branch mutation and tag-only clone helpers so attestation durability can be proven.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>
/// Creates a disposable repository that contains two governed releases on the same version line,
/// each closed by an SSH-signed annotated tag, plus the promoted trusted bundle that attests them.
/// The fixture deliberately keeps the promoted bundle outside the repository so a tag-only clone can
/// reuse it, which is what makes offline provider-independent re-verification observable in a test.
/// </summary>
internal sealed class GovernedReleaseFixture : IDisposable
{
    /// <summary>
    /// Integration branch that accumulates commits before each release tag is created. Release
    /// identity never depends on it; it exists only because commits need somewhere to land, and it
    /// is deliberately *not* a per-line branch — nothing is provisioned per release line.
    /// </summary>
    internal const string IntegrationBranch = "develop";

    private const string FirstVersion = "1.1.0";
    private const string SecondVersion = "1.1.1";
    private const string FirstGovernedVersion = "0.1.0";
    private const string BaselineTagName = "changelog-baseline-2026-08-23";
    private const string ReleasePolicyYaml = "schemaVersion: 1\nmaximumCommitMessageBytes: 8192\nreleaseVisibleTypes:\n  - feat\n  - fix\n  - perf\n  - revert\n  - docs\ninternalTypes:\n  - test\n  - refactor\n  - style\n  - build\n  - ci\n  - chore\nrequiredBreakingSignals:\n  bang: true\n  footer: BREAKING CHANGE\nskipTrailer: Changelog\nskipValue: skip\nskipReasonTrailer: Changelog-Reason\n";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly string bundleRoot;
    private readonly string authorityRoot;
    private readonly string promotionPrivateKeyPath;
    private readonly string releasePrivateKeyPath;
    private readonly string receiptPath;
    private readonly string signaturePath;
    private readonly string allowedPromotersPath;
    private readonly string allowedReleaseSignersPath;
    private readonly string configPath;
    private readonly string executablePath;
    private readonly List<string> clonePaths = [];

    private GovernedReleaseFixture(string objectFormat, bool firstGovernedRelease = false)
    {
        ObjectFormat = objectFormat;
        IsFirstGovernedRelease = firstGovernedRelease;
        Root = Path.Combine(Path.GetTempPath(), $"islamu-tag-anchored-{Guid.NewGuid():N}");
        RepositoryPath = Path.Combine(Root, "repo");
        bundleRoot = Path.Combine(Root, "bundle");
        authorityRoot = Path.Combine(Root, "authority");
        promotionPrivateKeyPath = Path.Combine(authorityRoot, "promotion-key");
        releasePrivateKeyPath = Path.Combine(authorityRoot, "release-key");
        receiptPath = Path.Combine(authorityRoot, "promotion-receipt.v1.json");
        signaturePath = receiptPath + ".sig";
        allowedPromotersPath = Path.Combine(authorityRoot, "allowed-promoters");
        allowedReleaseSignersPath = Path.Combine(bundleRoot, "trust", "allowed-signers");
        configPath = Path.Combine(bundleRoot, "config", "cliff.toml");
        executablePath = Path.Combine(bundleRoot, "git-cliff");

        Directory.CreateDirectory(RepositoryPath);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        Directory.CreateDirectory(authorityRoot);
        Git("init", $"--object-format={objectFormat}", $"--initial-branch={IntegrationBranch}");
        CreatePromotionAuthority();
        CreateReleaseAuthority();
        WriteWorkspace();
        WriteBundle();

        Initial = Commit("fix(events): preserve published event notes");
        if (firstGovernedRelease)
        {
            // Eleven months of pre-automation history end at an activation commit. That commit gets a
            // signed non-SemVer baseline tag, which lower-bounds the first governed release without
            // re-parsing messy historical commits and without pretending to be a release itself.
            BaselineTargetOid = Commit("chore(release): activate governed release tooling\n\nChangelog: skip\nChangelog-Reason: release activation commit");
            BaselineTagObject = CreateSignedTag(BaselineTagName, BaselineTargetOid, $"{BaselineTagName}\n");
            (int baselineCode, string baselineOutput) = VerifyBaseline(BaselineTagName, BaselineTargetOid, BaselineTagObject);
            if (baselineCode != Program.Success) throw new InvalidOperationException($"verify_baseline: {baselineOutput}");

            A = Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");
            B = PrepareAndCommit(FirstGovernedVersion, baseTag: BaselineTagName, baseOid: BaselineTargetOid, releaseDate: "2026-08-24");
            FirstTagObject = CloseRelease(FirstGovernedVersion, B);
            C = string.Empty;
            D = string.Empty;
            SecondTagObject = string.Empty;
            Git("switch", "--detach");
            return;
        }

        AnnotateTag("v1.0.0", Initial, "v1.0.0\n");
        A = Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");
        B = PrepareAndCommit(FirstVersion, baseTag: "v1.0.0", baseOid: Initial, releaseDate: "2026-08-14");
        FirstTagObject = CloseRelease(FirstVersion, B);

        C = Commit("fix(events): keep event notes readable after publication");
        D = PrepareAndCommit(SecondVersion, baseTag: $"v{FirstVersion}", baseOid: B, releaseDate: "2026-08-15");
        SecondTagObject = CloseRelease(SecondVersion, D);

        Git("switch", "--detach");
    }

    public string ObjectFormat { get; }
    public bool IsFirstGovernedRelease { get; }
    public string Root { get; }
    public string RepositoryPath { get; }
    public string Initial { get; }
    public string A { get; }
    public string B { get; }

    /// <summary>Second-release commits. Empty in the first-governed-release topology, which has one release.</summary>
    public string C { get; }
    public string D { get; }
    public string FirstTagObject { get; }
    public string SecondTagObject { get; }

    /// <summary>Baseline anchor. Empty unless the fixture was built for the first governed release.</summary>
    public string BaselineTargetOid { get; } = string.Empty;
    public string BaselineTagObject { get; } = string.Empty;
    public static string BaselineRef => BaselineTagName;
    public static string FirstGovernedReleaseVersion => FirstGovernedVersion;
    public string FirstReleaseDirectory => Path.Combine(RepositoryPath, "docs", "internal", "releases", FirstVersion);
    public string SecondReleaseDirectory => Path.Combine(RepositoryPath, "docs", "internal", "releases", SecondVersion);
    public static string FirstReleaseVersion => FirstVersion;
    public static string SecondReleaseVersion => SecondVersion;

    public static GovernedReleaseFixture CreateSha1() => new("sha1");

    /// <summary>Builds the Decision 10 topology: a signed non-SemVer baseline plus one first governed release.</summary>
    public static GovernedReleaseFixture CreateFirstGovernedRelease(string objectFormat = "sha1") => new(objectFormat, firstGovernedRelease: true);

    public (int ExitCode, string Output) VerifyBaseline(string baselineRef, string targetOid, string tagObjectId, string? repositoryPath = null) =>
        RunWithEnvironment(writer => BaselineCommand.Run(
            ["verify-baseline", baselineRef, targetOid, tagObjectId],
            writer,
            repositoryPath ?? RepositoryPath,
            CommandTimeout));

    /// <summary>Returns null when the host Git build cannot create SHA-256 repositories.</summary>
    public static GovernedReleaseFixture? CreateSha256OrNull()
    {
        try
        {
            return new GovernedReleaseFixture("sha256");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public (int ExitCode, string Output) VerifyCandidate(string version, string candidateOid, string? repositoryPath = null) =>
        RunWithEnvironment(writer => CandidateCommand.Run(
            ["verify-candidate", $"docs/internal/releases/{version}", candidateOid],
            writer,
            repositoryPath ?? RepositoryPath,
            "linux-x64",
            CommandTimeout));

    public (int ExitCode, string Output) VerifyTag(string version, string candidateOid, string tagObjectId, string? repositoryPath = null) =>
        RunWithEnvironment(writer => TagCommand.Run(
            ["verify-tag", $"docs/internal/releases/{version}", candidateOid, tagObjectId],
            writer,
            repositoryPath ?? RepositoryPath,
            "linux-x64",
            CommandTimeout));

    public (int ExitCode, string Output) VerifyMain(string version, string expectedOldOid, string tagObjectId, string? repositoryPath = null) =>
        RunWithEnvironment(writer => MainCommand.Run(
            ["verify-main", $"docs/internal/releases/{version}", expectedOldOid, tagObjectId],
            writer,
            repositoryPath ?? RepositoryPath,
            CommandTimeout));

    /// <summary>Points the observed remote-tracking ref at a commit so `verify-main` has a CAS anchor.</summary>
    public void SetObservedOriginMain(string oid) => Git("update-ref", "refs/remotes/origin/main", oid);

    public string GenerateTagMessage(string version, string? repositoryPath = null)
    {
        (int exitCode, string output) = RunWithEnvironment(writer => TagCommand.Run(
            ["tag-message", $"docs/internal/releases/{version}"],
            writer,
            repositoryPath ?? RepositoryPath,
            "linux-x64",
            CommandTimeout));
        return exitCode == Program.Success ? output : throw new InvalidOperationException(output);
    }

    public (int ExitCode, string Output) OpenMaintenanceLine(string version, string tagObjectId, string? repositoryPath = null) =>
        RunWithEnvironment(writer => MaintenanceLineCommand.Run(
            ["open-maintenance-line", $"docs/internal/releases/{version}", tagObjectId],
            writer,
            repositoryPath ?? RepositoryPath,
            "linux-x64",
            CommandTimeout));

    public void DeleteIntegrationBranch() => Git("branch", "-D", IntegrationBranch);

    public void DeleteBranch(string name) => Git("branch", "-D", name);

    public string AllRefs() => RunProcess("git", RepositoryPath, "for-each-ref", "--format=%(refname) %(objectname)", "refs/heads", "refs/tags", "refs/remotes");

    public void MoveIntegrationBranch(string target) => Git("branch", "-f", IntegrationBranch, target);

    public void CreateBranch(string name, string target) => Git("branch", "-f", name, target);

    public void DeleteGeneratedManifests(string version)
    {
        string directory = Path.Combine(RepositoryPath, "docs", "internal", "releases", version);
        foreach (string name in new[] { "release-candidate.v1.json", "release-evidence.v1.json" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// Produces a fresh repository that fetched <c>refs/tags/*</c> only and never had a single entry
    /// under <c>refs/heads/</c>, then checks the requested tag out detached. Tags are fetched because
    /// a release range is defined by its base and previous tag objects; branches are not, because
    /// nothing in the release model may depend on them.
    /// </summary>
    public string CreateTagOnlyClone(string tagName)
    {
        string clonePath = Path.Combine(Root, $"tag-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(clonePath);
        RunProcess("git", clonePath, "init", $"--object-format={ObjectFormat}", "--initial-branch=placeholder");
        RunProcess("git", clonePath, "fetch", "--no-tags", RepositoryPath, "refs/tags/*:refs/tags/*");
        RunProcess("git", clonePath, "-c", "advice.detachedHead=false", "switch", "--detach", $"refs/tags/{tagName}");
        clonePaths.Add(clonePath);
        return clonePath;
    }

    public string BranchRefs(string? repositoryPath = null) =>
        RunProcess("git", repositoryPath ?? RepositoryPath, "for-each-ref", "--format=%(refname)", "refs/heads");

    public string CreateSignedTag(string tagName, string target, string message)
    {
        string messagePath = Path.Combine(Root, $"{tagName.Replace('/', '_')}.message");
        File.WriteAllText(messagePath, message);
        Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "-c", "gpg.format=ssh", "-c", $"user.signingKey={releasePrivateKeyPath}", "tag", "-s", tagName, target, "-F", messagePath);
        return ResolveTagObject(tagName);
    }

    public string CreateUnsignedAnnotatedTag(string tagName, string target, string message)
    {
        string messagePath = Path.Combine(Root, $"{tagName.Replace('/', '_')}.unsigned.message");
        File.WriteAllText(messagePath, message);
        Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", tagName, target, "-F", messagePath);
        return ResolveTagObject(tagName);
    }

    public void DeleteTag(string tagName) => Git("tag", "-d", tagName);

    public string ResolveTagObject(string tagName) => Git("rev-parse", $"refs/tags/{tagName}^{{object}}").Trim();

    public string ResolveRef(string reference) => Git("rev-parse", "--verify", $"{reference}^{{commit}}").Trim();

    /// <summary>Creates a commit that shares no history with the release line, for non-ancestor proofs.</summary>
    public string CreateUnrelatedCommit()
    {
        Git("checkout", "--orphan", "unrelated");
        Git("rm", "-r", "-f", "--cached", ".");
        File.WriteAllText(Path.Combine(RepositoryPath, "unrelated.txt"), "unrelated\n");
        Git("add", "unrelated.txt");
        Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", "chore(release): unrelated history");
        string oid = Git("rev-parse", "HEAD").Trim();
        Git("checkout", "--force", "--detach", D);
        Git("clean", "-fd");
        Git("branch", "-D", "unrelated");
        return oid;
    }

    /// <summary>Merges an unrelated commit into the detached head so a range becomes non-linear.</summary>
    public string CreateMergeCommitOnTopOfHead(string otherOid)
    {
        Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "merge", "--no-ff", "--allow-unrelated-histories", "-m", "chore(release): merge unrelated history", otherOid);
        return Git("rev-parse", "HEAD").Trim();
    }

    public void Dispose()
    {
        foreach (string clone in clonePaths)
        {
            if (Directory.Exists(clone)) Directory.Delete(clone, recursive: true);
        }

        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    private string PrepareAndCommit(string version, string baseTag, string baseOid, string releaseDate)
    {
        string releaseDirectory = Path.Combine(RepositoryPath, "docs", "internal", "releases", version);
        Directory.CreateDirectory(releaseDirectory);
        File.WriteAllText(
            Path.Combine(releaseDirectory, "release.yaml"),
            $"Version: {version}\nLine: {LineLabelFor(version)}\nRelease-Date: {releaseDate}\nBase-Stable-Tag: {baseTag}\nPrevious-Published-Tag: {baseTag}\nRelease-Range:\n  Base-Ref: {baseTag}\n  Base-Oid: {baseOid}\n  Previous-Ref: {baseTag}\n  Previous-Oid: {baseOid}\nCompatibility:\n  - {LineLabelFor(version)[..2]}\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: {(version == SecondVersion ? "not-applicable" : "documented")}\n");
        File.WriteAllText(
            Path.Combine(releaseDirectory, "summary.md"),
            version == SecondVersion
                ? "Published event notes stay readable.\n"
                : "Attendees can now correct registration details.\n");

        (int exitCode, string output) = RunWithEnvironment(writer => PrepareCommand.Run(
            ["prepare", $"docs/internal/releases/{version}"],
            writer,
            RepositoryPath,
            "linux-x64",
            CommandTimeout));
        if (exitCode != Program.Success) throw new InvalidOperationException($"prepare_{version}: {output}");

        return Commit($"docs(release): prepare {version}\n\nChangelog: skip\nChangelog-Reason: release metadata commit");
    }

    private static string LineLabelFor(string version)
    {
        string[] parts = version.Split('.');
        return $"v{parts[0]}.{parts[1]}";
    }

    private string CloseRelease(string version, string candidateOid)
    {
        (int candidateCode, string candidateOutput) = VerifyCandidate(version, candidateOid);
        if (candidateCode != Program.Success) throw new InvalidOperationException($"verify_candidate_{version}: {candidateOutput}");

        string tagObject = CreateSignedTag($"v{version}", candidateOid, GenerateTagMessage(version));
        (int tagCode, string tagOutput) = VerifyTag(version, candidateOid, tagObject);
        if (tagCode != Program.Success) throw new InvalidOperationException($"verify_tag_{version}: {tagOutput}");

        DeleteGeneratedManifests(version);
        return tagObject;
    }

    private (int ExitCode, string Output) RunWithEnvironment(Func<TextWriter, int> action)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = bundleRoot,
            ["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = receiptPath,
            ["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = signaturePath,
            ["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter",
            ["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(Path.Combine(bundleRoot, "trusted-bundle.manifest.json")),
            ["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering",
            ["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0",
            ["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1",
            ["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1",
            ["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1",
        };
        Dictionary<string, string?> originals = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach ((string name, string value) in variables) Environment.SetEnvironmentVariable(name, value);
            using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedPromotersPath);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            int exitCode = action(output);
            return (exitCode, output.ToString());
        }
        finally
        {
            foreach ((string name, string? value) in originals) Environment.SetEnvironmentVariable(name, value);
        }
    }

    private void WriteWorkspace()
    {
        Directory.CreateDirectory(Path.Combine(RepositoryPath, "docs", "internal", "releases", "changes"));
        Directory.CreateDirectory(Path.Combine(RepositoryPath, "eng", "release", "policy"));
        File.WriteAllText(
            Path.Combine(RepositoryPath, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"),
            "Change-Id: CHG-2026-0001\nTitle: Registration correction window\nType: feat\nScope: registration\nSummary: Attendees can now correct registration details.\nSupersedes: []\nImpacts:\n  Breaking:\n    Reference: docs/internal/releases/README.md\n    Disposition: not-applicable\n  Security:\n    Reference: docs/SECURITY_OVERVIEW.md\n    Disposition: not-applicable\n  Migration:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: not-applicable\n  Configuration:\n    Reference: docs/CONFIGURATION.md\n    Disposition: not-applicable\n  OpenAPI:\n    Reference: docs/API_CHANGELOG.md\n    Disposition: not-applicable\n  Operator:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: documented\n    Detail: Restart registration workers after deployment.\n");
        File.WriteAllText(Path.Combine(RepositoryPath, "eng", "release", "policy", "release-policy.yaml"), ReleasePolicyYaml);
        File.WriteAllText(Path.Combine(RepositoryPath, "eng", "release", "policy", "scope-registry.yaml"), "schemaVersion: 1\npublicScopes:\n  - events\n  - registration\nengineeringScopes:\n  - release\n");
    }

    private void WriteBundle()
    {
        File.WriteAllText(configPath, "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n");

        // The renderer clears PATH, so the stub uses shell builtins only. It reads the release
        // version out of the canonical context git-cliff was handed, which keeps one pinned binary
        // digest valid across every release the fixture produces.
        File.WriteAllText(
            executablePath,
            "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then printf 'git-cliff 2.13.1\\n'; exit 0; fi\nq='\"'\nIFS= read -r context < \"$4\"\nrest=${context#*${q}version${q}:${q}}\nversion=${rest%%${q}*}\nprintf '# Release %s\\n\\n- release: recorded change (cccccccccccc)\\n' \"$version\"\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        File.WriteAllText(Path.Combine(bundleRoot, "toolchain.lock.json"), $$"""
            {
              "schemaVersion": 1,
              "tool": "git-cliff",
              "version": "2.13.1",
              "platforms": [{ "platform": "linux-x64", "executable": "git-cliff", "executableSha256": "{{Digest(executablePath)}}" }]
            }
            """);
        EnsureFile("trust/release-signing-policy.yaml", "schemaVersion: release-signing-policy.v1\nstatus: fixture-only\nallowedAlgorithms:\n  - ssh-ed25519\nroles:\n  release:\n    tagPattern: v<major>.<minor>.<patch>[-prerelease]\n    tagKind: annotated\n    namespace: git\n    principal: fixture-release-operator\n    algorithm: ssh-ed25519\n    validFrom: 2026-01-01\n    validUntil: 2026-12-31\n  tooling-promotion:\n    principal: fixture-tooling-promoter\n");
        RewriteManifestAndReceipt();
    }

    private void RewriteManifestAndReceipt()
    {
        EnsureFile("bin/ISLAMU.ReleaseEngineering.dll", "release-engine-binary");
        EnsureFile("policy/context-version.txt", "context-v1\n");
        EnsureFile("policy/schema-version.txt", "schema-v1\n");
        EnsureFile("policy/release-policy.yaml", ReleasePolicyYaml);
        string manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "trusted-bundle.v1",
            bundleId = "islamu-release-engineering",
            bundleVersion = "1.0.0",
            policyVersion = "policy-v1",
            configVersion = "config-v1",
            trustVersion = "trust-v1",
            policyDigest = Digest(Path.Combine(bundleRoot, "policy", "release-policy.yaml")),
            configDigest = Digest(configPath),
            trustDigest = Digest(Path.Combine(bundleRoot, "trust", "release-signing-policy.yaml")),
            files = Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != "trusted-bundle.manifest.json")
                .Select(path => new { path = Path.GetRelativePath(bundleRoot, path).Replace(Path.DirectorySeparatorChar, '/'), sha256 = Digest(path) })
                .OrderBy(item => item.path, StringComparer.Ordinal)
                .ToArray(),
        });
        File.WriteAllBytes(Path.Combine(bundleRoot, "trusted-bundle.manifest.json"), CanonicalArtifactPolicy.CanonicalizeJson(manifestJson).Bytes!);
        ResignReceipt();
    }

    private void ResignReceipt()
    {
        string manifestPath = Path.Combine(bundleRoot, "trusted-bundle.manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        string receiptJson = JsonSerializer.Serialize(new
        {
            schemaVersion = "trusted-bundle-promotion.v1",
            receiptId = "promotion-fixture-0001",
            bundleManifestSha256 = Digest(manifestPath),
            bundleId = root.GetProperty("bundleId").GetString(),
            bundleVersion = root.GetProperty("bundleVersion").GetString(),
            policyVersion = root.GetProperty("policyVersion").GetString(),
            configVersion = root.GetProperty("configVersion").GetString(),
            trustVersion = root.GetProperty("trustVersion").GetString(),
            policyDigest = root.GetProperty("policyDigest").GetString(),
            configDigest = root.GetProperty("configDigest").GetString(),
            trustDigest = root.GetProperty("trustDigest").GetString(),
            promotionPrincipal = "fixture-tooling-promoter",
        });
        File.WriteAllBytes(receiptPath, CanonicalArtifactPolicy.CanonicalizeJson(receiptJson).Bytes!);
        if (File.Exists(signaturePath)) File.Delete(signaturePath);
        RunProcess("/usr/bin/ssh-keygen", null, "-Y", "sign", "-f", promotionPrivateKeyPath, "-n", "islamu-release-promotion", receiptPath);
    }

    private void CreatePromotionAuthority()
    {
        RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-promotion-fixture", "-f", promotionPrivateKeyPath);
        string[] publicKey = File.ReadAllText(promotionPrivateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        File.WriteAllText(allowedPromotersPath, $"fixture-tooling-promoter namespaces=\"islamu-release-promotion\" {publicKey[0]} {publicKey[1]}\n");
    }

    private void CreateReleaseAuthority()
    {
        RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-release-fixture", "-f", releasePrivateKeyPath);
        string[] publicKey = File.ReadAllText(releasePrivateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Directory.CreateDirectory(Path.GetDirectoryName(allowedReleaseSignersPath)!);
        File.WriteAllText(allowedReleaseSignersPath, $"fixture-release-operator namespaces=\"git\",valid-after=\"20260101\",valid-before=\"20261231\" {publicKey[0]} {publicKey[1]}\n");
    }

    private void AnnotateTag(string tagName, string target, string message)
    {
        string messagePath = Path.Combine(Root, $"{tagName}.baseline.message");
        File.WriteAllText(messagePath, message);
        Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", tagName, target, "-F", messagePath);
    }

    private string EnsureFile(string path, string content)
    {
        string fullPath = Path.Combine(bundleRoot, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private string Commit(string message)
    {
        File.AppendAllText(Path.Combine(RepositoryPath, "file.txt"), message.Split('\n')[0] + "\n");
        Git("add", ".");
        Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    private string Git(params string[] args) => RunProcess("git", RepositoryPath, args);

    private static string RunProcess(string executable, string? workingDirectory, params string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            },
        };
        string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        if (executable == "git")
        {
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
        }

        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"{executable}_failed:{string.Join(' ', args)}:{error}");
    }

    private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
