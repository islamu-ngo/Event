using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class GitCliffRendererTests
{
    [Test]
    public async Task RendererUsesOnlyExplicitTrustedInputsFromAnIsolatedNonGitDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.13.1'; exit 0; fi
            [ "$1" = "--config" ] && [ "$3" = "--from-context" ] && [ "$5" = "--offline" ] && [ "$6" = "--no-exec" ] || exit 21
            [ ! -e .git ] || exit 22
            [ -z "$GIT_CLIFF_CONFIG" ] && [ -z "$GITHUB_TOKEN" ] && [ -z "$SOURCE_DATE_EPOCH" ] || exit 23
            grep -q '"version":"1.1.0"' "$4" || exit 24
            first=$(head -c 1 "$4")
            [ "$first" = '[' ] || exit 25
            [ "$(grep -o '"version":"1.1.0"' "$4" | wc -l)" = "1" ] || exit 26
            printf '# Release 1.1.0\n\n- registration: attendee updates (cccccccccccc)\n'
            """);

        GitCliffRenderResult result = fixture.Render();

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Markdown).IsEquivalentTo("# Release 1.1.0\n\n- registration: attendee updates (cccccccccccc)\n"u8.ToArray());
        await Assert.That(Directory.EnumerateFileSystemEntries(fixture.IsolationRoot)).IsEmpty();
    }

    [Test]
    public async Task RendererRejectsUntrustedConfigAndUnsafeOrNoncanonicalOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.13.1'; exit 0; fi
            printf '%s\r\n' '<script>unsafe</script> https://example.org maintainer@example.org'
            """);

        VerifiedTrustedBundle verified = fixture.VerifiedBundle();
        File.AppendAllText(fixture.ConfigPath, "# drift\n");
        GitCliffRenderResult wrongConfig = fixture.Render(verified);
        fixture.RestoreConfig();
        GitCliffRenderResult unsafeOutput = fixture.Render();

        await Assert.That(wrongConfig.Diagnostic).IsEqualTo("renderer_config_digest_mismatch");
        await Assert.That(unsafeOutput.Diagnostic).IsEqualTo("renderer_output_not_canonical");
    }

    [Test]
    public async Task RendererRejectsRestrictedCanonicalOutputWithoutEchoingProcessErrors()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.13.1'; exit 0; fi
            printf '# Release 1.1.0\n\n- maintainer@example.org https://example.org <details>unsafe</details>\n'
            """);
        GitCliffRenderResult restricted = fixture.Render();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.13.1'; exit 0; fi
            printf '%s\n' 'secret stderr value' >&2
            exit 7
            """);
        GitCliffRenderResult failed = fixture.Render();

        await Assert.That(restricted.Diagnostic).IsEqualTo("renderer_output_restricted");
        await Assert.That(failed.Diagnostic).IsEqualTo("renderer_process_failed");
        await Assert.That(failed.Diagnostic).DoesNotContain("secret stderr value");
    }

    [Test]
    public async Task RendererRejectsNoncanonicalContextAndPolicyBearingTrustedConfig()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        fixture.WriteExecutable("printf '# Release 1.1.0\n'");
        byte[] canonical = fixture.Context();
        byte[] noncanonical = System.Text.Encoding.UTF8.Preamble.ToArray().Concat(canonical).ToArray();
        GitCliffRenderResult contextResult = fixture.Render(context: noncanonical);

        GitCliffRenderResult configResult = fixture.RenderWithConfig("[git]\ncommit_parsers = []\n");

        await Assert.That(contextResult.Diagnostic).IsEqualTo("renderer_context_not_canonical");
        await Assert.That(configResult.Diagnostic).IsEqualTo("renderer_config_not_presentation_only");
    }

    [Test]
    public async Task RendererRequestCannotBeForgedFromCandidatePathsOrPublicCapabilityConstructor()
    {
        string[] requestProperties = typeof(GitCliffRenderRequest).GetProperties().Select(property => property.Name).ToArray();
        var publicConstructors = typeof(VerifiedTrustedBundle).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        await Assert.That(requestProperties).Contains("TrustedBundle");
        await Assert.That(requestProperties).DoesNotContain("ToolBundlePath");
        await Assert.That(requestProperties).DoesNotContain("ToolchainLockPath");
        await Assert.That(requestProperties).DoesNotContain("TrustedConfigPath");
        await Assert.That(requestProperties).DoesNotContain("ExpectedConfigSha256");
        await Assert.That(publicConstructors).IsEmpty();
    }

    [Test]
    public async Task RendererRejectsDottedSpacedQuotedPolicyTomlVariants()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = new RendererFixture();
        fixture.WriteExecutable("if [ \"$1\" = \"--version\" ]; then printf '%s\\n' 'git-cliff 2.13.1'; exit 0; fi\nprintf '# Release 1.1.0\\n'");
        string[] configs =
        [
            "git.commit_parsers = []\n[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n",
            "[ git ]\ncommit_parsers = []\n[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n",
            "[\"remote.github\"]\nowner = \"candidate\"\n[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n",
            "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.github.username }}\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n",
            "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }}) https://example.invalid\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n",
            "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n[changelog.postprocessors]\nreplace_command = \"touch marker\"\n",
        ];

        foreach (string config in configs)
        {
            GitCliffRenderResult result = fixture.RenderWithConfig(config);
            await Assert.That(result.Diagnostic).IsEqualTo("renderer_config_not_presentation_only");
        }
    }

    [Test]
    public async Task RendererRejectsTrustedConfigSymlinkAndHardlinkAliasesAtUseTime()
    {
        if (OperatingSystem.IsWindows()) return;

        using var symlink = new RendererFixture();
        symlink.WriteExecutable("if [ \"$1\" = \"--version\" ]; then printf '%s\\n' 'git-cliff 2.13.1'; exit 0; fi\nprintf '# Release 1.1.0\\n'");
        VerifiedTrustedBundle symlinkCapability = symlink.VerifiedBundle();
        string symlinkTarget = Path.Combine(symlink.Root, "config-target.toml");
        File.Move(symlink.ConfigPath, symlinkTarget);
        File.CreateSymbolicLink(symlink.ConfigPath, symlinkTarget);

        using var hardlink = new RendererFixture();
        hardlink.WriteExecutable("if [ \"$1\" = \"--version\" ]; then printf '%s\\n' 'git-cliff 2.13.1'; exit 0; fi\nprintf '# Release 1.1.0\\n'");
        VerifiedTrustedBundle hardlinkCapability = hardlink.VerifiedBundle();
        Run("/usr/bin/ln", hardlink.ConfigPath, Path.Combine(hardlink.Root, "config-hardlink.toml"));

        await Assert.That(symlink.Render(symlinkCapability).Diagnostic).IsEqualTo("renderer_config_invalid");
        await Assert.That(hardlink.Render(hardlinkCapability).Diagnostic).IsEqualTo("renderer_config_invalid");
    }

    [Test]
    public async Task RendererKillsHungOrUnboundedProcessesWithoutKeepingOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.13.1'; exit 0; fi
            yes x
            """);

        GitCliffRenderResult result = fixture.Render(timeout: TimeSpan.FromSeconds(1));

        await Assert.That(result.Diagnostic).IsEqualTo("renderer_process_limit_exceeded");
        await Assert.That(result.Markdown).IsNull();
    }

    [Test]
    public async Task HostileCandidateConfigProcessorAndAmbientEnvironmentAreInert()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        string marker = Path.Combine(fixture.Root, "processor-ran");
        string candidateConfig = Path.Combine(fixture.Root, "cliff.toml");
        File.WriteAllText(candidateConfig, $"[changelog]\npostprocessors = [{{ pattern = '.*', replace_command = 'touch {marker}' }}]\n");
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.13.1'; exit 0; fi
            [ "$GIT_CLIFF_CONFIG" != "$1" ] || exit 31
            [ "$GIT_CLIFF_CONTEXT" != "$3" ] || exit 32
            printf '# Release 1.1.0\n'
            """);

        string? oldConfig = Environment.GetEnvironmentVariable("GIT_CLIFF_CONFIG");
        string? oldContext = Environment.GetEnvironmentVariable("GIT_CLIFF_CONTEXT");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONFIG", candidateConfig);
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONTEXT", candidateConfig);
            GitCliffRenderResult result = fixture.Render();

            await Assert.That(result.IsValid).IsTrue();
            await Assert.That(File.Exists(marker)).IsFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONFIG", oldConfig);
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONTEXT", oldContext);
        }
    }

    [Test]
    [Explicit]
    [Category("Runtime")]
    public async Task PromotedBinaryRendersTwiceByteIdenticallyOutsideGit()
    {
        string bundlePath = Environment.GetEnvironmentVariable("ISLAMU_RELEASE_TOOL_BUNDLE")
            ?? throw new InvalidOperationException("ISLAMU_RELEASE_TOOL_BUNDLE is required for the real renderer fixture.");
        string root = RepositoryRoot.Find();
        byte[] context = File.ReadAllBytes(Path.Combine(root, "eng", "release", "tests", "ISLAMU.ReleaseEngineering.Tests", "Fixtures", "stable-release-context.v1.json"));
        string isolationRoot = Path.Combine(Path.GetTempPath(), $"islamu-renderer-real-{Guid.NewGuid():N}");
        string trustedBundle = Path.Combine(isolationRoot, "trusted-bundle");
        string trustedConfig = Path.Combine(trustedBundle, "config", "cliff.toml");
        string trustedLock = Path.Combine(trustedBundle, "toolchain.lock.json");
        Directory.CreateDirectory(isolationRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(trustedConfig)!);
        File.Copy(Path.Combine(bundlePath, "git-cliff"), Path.Combine(trustedBundle, "git-cliff"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(trustedBundle, "git-cliff"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        File.Copy(Path.Combine(root, "eng", "release", "cliff.toml"), trustedConfig);
        File.Copy(Path.Combine(root, "eng", "release", "toolchain.lock.json"), trustedLock);
        string marker = Path.Combine(isolationRoot, "candidate-processor-ran");
        string candidateConfig = Path.Combine(isolationRoot, "candidate-cliff.toml");
        File.WriteAllText(candidateConfig, $"[changelog]\npostprocessors = [{{ pattern = '.*', replace_command = 'touch {marker}' }}]\n");
        string? oldConfig = Environment.GetEnvironmentVariable("GIT_CLIFF_CONFIG");
        string? oldContext = Environment.GetEnvironmentVariable("GIT_CLIFF_CONTEXT");
        string? oldToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        string? oldEpoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONFIG", candidateConfig);
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONTEXT", candidateConfig);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "candidate-token");
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "9999999999");
            using var fixture = RendererFixture.FromExistingBundle(trustedBundle, isolationRoot);
            VerifiedTrustedBundle verified = fixture.VerifiedBundle();
            var request = new GitCliffRenderRequest(
                verified,
                context,
                "linux-x64",
                isolationRoot,
                TimeSpan.FromSeconds(5));

            GitCliffRenderResult first = GitCliffRenderer.Render(request);
            GitCliffRenderResult second = GitCliffRenderer.Render(request);

            await Assert.That(first.Diagnostic).IsNull();
            await Assert.That(second.Diagnostic).IsNull();
            await Assert.That(first.IsValid).IsTrue();
            await Assert.That(second.IsValid).IsTrue();
            await Assert.That(second.Markdown).IsEquivalentTo(first.Markdown!);
            await Assert.That(System.Text.Encoding.UTF8.GetString(first.Markdown!)).IsEqualTo(
                "# Release 1.1.0\n\n### \U0001f680 Features\n\n- registration: let attendees correct registration details (cccccccccccc)\n");
            await VerifyCategorizedCases(request, context);
            await Assert.That(File.Exists(marker)).IsFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONFIG", oldConfig);
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONTEXT", oldContext);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", oldToken);
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", oldEpoch);
            if (Directory.Exists(isolationRoot))
            {
                Directory.Delete(isolationRoot, recursive: true);
            }
        }
    }

    private static async Task VerifyCategorizedCases(GitCliffRenderRequest request, byte[] canonicalContext)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };
        ReleaseContext context = JsonSerializer.Deserialize<ReleaseContext>(canonicalContext, options)!;
        ReleaseContextChange seed = context.Changes[0];
        ReleaseContextChange[] changes =
        [
            seed with { DisplayId = "999999999999", Oid = new string('9', 40), Type = "docs", Scope = "guides", Title = "keep punctuation: (safe), caf\u00e9" },
            seed with { DisplayId = "bbbbbbbbbbbb", Oid = new string('b', 40), Type = "fix", Scope = "api", Title = "remove obsolete response shape", Breaking = true },
            seed with { DisplayId = "cccccccccccc", Oid = new string('c', 40), Type = "feat", Scope = "registration", Title = "first feature in context" },
            seed with { DisplayId = "aaaaaaaaaaaa", Oid = new string('a', 40), Type = "feat", Scope = "registration", Title = "second feature in context" },
            seed with { DisplayId = "dddddddddddd", Oid = new string('d', 40), Type = "fix", Scope = "registration", Title = "repair attendee lookup" },
            seed with { DisplayId = "eeeeeeeeeeee", Oid = new string('e', 40), Type = "perf", Scope = "search", Title = "reduce allocation" },
            seed with { DisplayId = "ffffffffffff", Oid = new string('f', 40), Type = "fix", Scope = "registration", Title = "backport attendee validation", Backport = true, BackportOf = new string('1', 40) },
            seed with { DisplayId = "888888888888", Oid = new string('8', 40), Type = "chore", Scope = "release", Title = "refresh contributor tooling" },
        ];
        string mixed =
            "# Release 1.1.0\n\n" +
            "### \u26a0\ufe0f Breaking Changes\n\n" +
            "- api: remove obsolete response shape (bbbbbbbbbbbb)\n\n" +
            "### \U0001f680 Features\n\n" +
            "- registration: first feature in context (cccccccccccc)\n" +
            "- registration: second feature in context (aaaaaaaaaaaa)\n\n" +
            "### \U0001f41b Bug Fixes\n\n" +
            "- registration: repair attendee lookup (dddddddddddd)\n" +
            "- registration: backport attendee validation (ffffffffffff)\n\n" +
            "### \u26a1 Performance\n\n" +
            "- search: reduce allocation (eeeeeeeeeeee)\n\n" +
            "### \U0001f527 Other Improvements\n\n" +
            "- guides: keep punctuation: (safe), caf\u00e9 (999999999999)\n" +
            "- release: refresh contributor tooling (888888888888)\n";
        (ReleaseContextChange[] Changes, string Markdown)[] cases =
        [
            (changes, mixed),
            ([changes[1]], "# Release 1.1.0\n\n### \u26a0\ufe0f Breaking Changes\n\n- api: remove obsolete response shape (bbbbbbbbbbbb)\n"),
            ([changes[4], changes[6]], "# Release 1.1.0\n\n### \U0001f41b Bug Fixes\n\n- registration: repair attendee lookup (dddddddddddd)\n- registration: backport attendee validation (ffffffffffff)\n"),
            ([], "# Release 1.1.0\n"),
        ];
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        string? originalTimeZone = Environment.GetEnvironmentVariable("TZ");
        try
        {
            foreach (var scenario in cases)
            {
                byte[] canonical = System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(context with { Changes = scenario.Changes }, options) + "\n");
                foreach (string cultureName in new[] { "en-US", "tr-TR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                    Environment.SetEnvironmentVariable("TZ", cultureName == "en-US" ? "Pacific/Honolulu" : "Asia/Tokyo");
                    GitCliffRenderResult rendered = GitCliffRenderer.Render(request with { CanonicalContext = canonical });
                    await Assert.That(rendered.IsValid).IsTrue().Because(rendered.Diagnostic ?? "categorized render");
                    await Assert.That(System.Text.Encoding.UTF8.GetString(rendered.Markdown!)).IsEqualTo(scenario.Markdown);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            Environment.SetEnvironmentVariable("TZ", originalTimeZone);
        }
    }

    private sealed class RendererFixture : IDisposable
    {
        private readonly string authorityRoot;
        private readonly string privateKeyPath;
        private readonly string receiptPath;
        private readonly string signaturePath;
        private readonly string allowedSignersPath;

        public RendererFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-renderer-{Guid.NewGuid():N}");
            BundleRoot = Path.Combine(Root, "tool-bundle");
            CandidateRoot = Path.Combine(Root, "candidate");
            authorityRoot = Path.Combine(Root, "authority");
            privateKeyPath = Path.Combine(authorityRoot, "promotion-key");
            receiptPath = Path.Combine(authorityRoot, "promotion-receipt.v1.json");
            signaturePath = receiptPath + ".sig";
            allowedSignersPath = Path.Combine(authorityRoot, "allowed-promoters");
            IsolationRoot = Path.Combine(Root, "isolation");
            LockPath = Path.Combine(BundleRoot, "toolchain.lock.json");
            ConfigPath = Path.Combine(BundleRoot, "config", "cliff.toml");
            Directory.CreateDirectory(BundleRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            Directory.CreateDirectory(IsolationRoot);
            Directory.CreateDirectory(CandidateRoot);
            Directory.CreateDirectory(authorityRoot);
            File.WriteAllText(ConfigPath, PackagedConfig);
            CreatePromotionAuthority();
        }

        private RendererFixture(string root, string bundleRoot, string isolationRoot) : this()
        {
            Directory.Delete(Root, recursive: true);
            Root = root;
            BundleRoot = bundleRoot;
            CandidateRoot = Path.Combine(root, "candidate");
            authorityRoot = Path.Combine(root, "authority");
            privateKeyPath = Path.Combine(authorityRoot, "promotion-key");
            receiptPath = Path.Combine(authorityRoot, "promotion-receipt.v1.json");
            signaturePath = receiptPath + ".sig";
            allowedSignersPath = Path.Combine(authorityRoot, "allowed-promoters");
            IsolationRoot = isolationRoot;
            LockPath = Path.Combine(bundleRoot, "toolchain.lock.json");
            ConfigPath = Path.Combine(bundleRoot, "config", "cliff.toml");
            Directory.CreateDirectory(CandidateRoot);
            Directory.CreateDirectory(authorityRoot);
            CreatePromotionAuthority();
            RewriteManifestAndReceipt();
        }

        public string Root { get; private set; }
        public string BundleRoot { get; private set; }
        public string CandidateRoot { get; private set; }
        public string IsolationRoot { get; private set; }
        public string LockPath { get; private set; }
        public string ConfigPath { get; private set; }

        private static string PackagedConfig => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "eng", "release", "cliff.toml"));

        public static RendererFixture FromExistingBundle(string bundleRoot, string isolationRoot)
        {
            string root = Path.GetDirectoryName(bundleRoot)!;
            return new RendererFixture(root, bundleRoot, isolationRoot);
        }

        public void WriteExecutable(string body)
        {
            string path = Path.Combine(BundleRoot, "git-cliff");
            File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            File.WriteAllText(
                LockPath,
                $$"""
                {
                  "schemaVersion": 1,
                  "tool": "git-cliff",
                  "version": "2.13.1",
                  "platforms": [{ "platform": "linux-x64", "executable": "git-cliff", "executableSha256": "{{digest}}" }]
                }
                """);
            RewriteManifestAndReceipt();
        }

        public byte[] Context() => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "eng", "release", "tests", "ISLAMU.ReleaseEngineering.Tests", "Fixtures", "stable-release-context.v1.json"));

        public GitCliffRenderResult Render(byte[]? context = null, TimeSpan? timeout = null) => Render(VerifiedBundle(), context, timeout);

        public GitCliffRenderResult Render(VerifiedTrustedBundle bundle, byte[]? context = null, TimeSpan? timeout = null)
        {
            return GitCliffRenderer.Render(new GitCliffRenderRequest(
                bundle,
                context ?? Context(),
                "linux-x64",
                IsolationRoot,
                timeout ?? TimeSpan.FromSeconds(2)));
        }

        public GitCliffRenderResult RenderWithoutReverify(byte[]? context = null) => Render(VerifiedBundleFromCurrentManifest(), context);

        public GitCliffRenderResult RenderWithConfig(string config)
        {
            File.WriteAllText(ConfigPath, config);
            RewriteManifestAndReceipt();
            return Render();
        }

        public void RestoreConfig()
        {
            File.WriteAllText(ConfigPath, PackagedConfig);
            RewriteManifestAndReceipt();
        }

        public VerifiedTrustedBundle VerifiedBundle()
        {
            RewriteManifestAndReceipt();
            return VerifiedBundleFromCurrentManifest();
        }

        private VerifiedTrustedBundle VerifiedBundleFromCurrentManifest()
        {
            using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedSignersPath);
            TrustedBundleResult result = TrustedBundlePolicy.Verify(Request());
            if (!result.IsValid || result.Bundle is null)
            {
                throw new InvalidOperationException(result.Diagnostic);
            }

            return result.Bundle;
        }

        private TrustedBundleVerificationRequest Request() => new(
            BundleRoot,
            CandidateRoot,
            new PromotionAuthorityInput(receiptPath, signaturePath, "fixture-tooling-promoter"),
            "islamu-release-engineering",
            "1.0.0",
            "policy-v1",
            "config-v1",
            "trust-v1")
        { ExpectedManifestDigest = Digest(Path.Combine(BundleRoot, "trusted-bundle.manifest.json")) };

        private void RewriteManifestAndReceipt()
        {
            EnsureFile("bin/ISLAMU.ReleaseEngineering.dll", "release-engine-binary");
            EnsureFile("policy/context-version.txt", "context-v1\n");
            EnsureFile("policy/schema-version.txt", "schema-v1\n");
            EnsureFile("trust/allowed-signers", "# production signers absent\n");
            string manifestJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "trusted-bundle.v1",
                bundleId = "islamu-release-engineering",
                bundleVersion = "1.0.0",
                policyVersion = "policy-v1",
                configVersion = "config-v1",
                trustVersion = "trust-v1",
                policyDigest = Digest(EnsureFile("policy/release-policy.yaml", "release-policy")),
                configDigest = Digest(ConfigPath),
                trustDigest = Digest(EnsureFile("trust/release-signing-policy.yaml", "status: inactive-fixture-only\n")),
                files = Directory.EnumerateFiles(BundleRoot, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path) != "trusted-bundle.manifest.json")
                    .Select(path => new { path = Path.GetRelativePath(BundleRoot, path).Replace(Path.DirectorySeparatorChar, '/'), sha256 = Digest(path) })
                    .OrderBy(item => item.path, StringComparer.Ordinal)
                    .ToArray(),
            });
            File.WriteAllBytes(Path.Combine(BundleRoot, "trusted-bundle.manifest.json"), CanonicalArtifactPolicy.CanonicalizeJson(manifestJson).Bytes!);
            ResignReceipt();
        }

        private string EnsureFile(string path, string content)
        {
            string fullPath = Path.Combine(BundleRoot, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            if (!File.Exists(fullPath)) File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private void CreatePromotionAuthority()
        {
            Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-promotion-fixture", "-f", privateKeyPath);
            string publicKey = string.Join(' ', File.ReadAllText(privateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(allowedSignersPath, $"fixture-tooling-promoter namespaces=\"islamu-release-promotion\" {publicKey}\n");
        }

        private void ResignReceipt()
        {
            string manifestPath = Path.Combine(BundleRoot, "trusted-bundle.manifest.json");
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
            Run("/usr/bin/ssh-keygen", "-Y", "sign", "-f", privateKeyPath, "-n", "islamu-release-promotion", receiptPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONFIG", null);
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONTEXT", null);
            Directory.Delete(Root, recursive: true);
        }
    }

    private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Run(string executable, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("synthetic_fixture_timeout");
        }

        if (process.ExitCode != 0) throw new InvalidOperationException("synthetic_fixture_failed");
    }
}
