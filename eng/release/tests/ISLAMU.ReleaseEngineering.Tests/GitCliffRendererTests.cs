using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class GitCliffRendererTests
{
    [Test]
    public async Task RendererUsesOnlyExplicitVerifiedInputsFromAnIsolatedNonGitDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.14.2'; exit 0; fi
            [ "$1" = "--config" ] && [ "$3" = "--offline" ] && [ "$4" = "--no-exec" ] && [ "$5" = "--from-context" ] || exit 21
            [ ! -e .git ] || exit 22
            [ -z "$GIT_CLIFF_CONFIG" ] && [ -z "$GITHUB_TOKEN" ] && [ -z "$SOURCE_DATE_EPOCH" ] || exit 23
            grep -q '"version":"1.1.0"' "$6" || exit 24
            first=$(head -c 1 "$6")
            [ "$first" = '[' ] || exit 25
            printf '# Release 1.1.0\n\n- registration: attendee updates (cccccccccccc)\n'
            """);

        GitCliffRenderResult result = fixture.Render();

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "valid render");
        await Assert.That(result.Markdown).IsEquivalentTo("# Release 1.1.0\n\n- registration: attendee updates (cccccccccccc)\n"u8.ToArray());
        await Assert.That(Directory.EnumerateFileSystemEntries(fixture.IsolationRoot)).IsEmpty();
    }

    [Test]
    public async Task RendererRejectsUnnormalizedAndRestrictedOutputWithoutEchoingProcessErrors()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.14.2'; exit 0; fi
            printf '%s\r\n' '<script>unsafe</script> https://example.org maintainer@example.org'
            """);
        GitCliffRenderResult unsafeOutput = fixture.Render();
        await Assert.That(unsafeOutput.Diagnostic).IsEqualTo("renderer_output_not_normalized");

        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.14.2'; exit 0; fi
            printf '# Release 1.1.0\n\n- maintainer@example.org https://example.org <details>unsafe</details>\n'
            """);
        GitCliffRenderResult restricted = fixture.Render();
        await Assert.That(restricted.Diagnostic).IsEqualTo("renderer_output_restricted");

        fixture.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.14.2'; exit 0; fi
            printf '%s\n' 'secret stderr value' >&2
            exit 7
            """);
        GitCliffRenderResult failed = fixture.Render();
        await Assert.That(failed.Diagnostic).IsEqualTo("renderer_process_failed");
        await Assert.That(failed.Diagnostic).DoesNotContain("secret stderr value");
    }

    [Test]
    public async Task RendererRejectsUnnormalizedContext()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        fixture.WriteExecutable("if [ \"$1\" = \"--version\" ]; then printf '%s\\n' 'git-cliff 2.14.2'; exit 0; fi\nprintf '# Release 1.1.0\\n'");
        byte[] normalized = fixture.Context();
        byte[] unnormalized = System.Text.Encoding.UTF8.Preamble.ToArray().Concat(normalized).ToArray();

        GitCliffRenderResult contextResult = fixture.Render(context: unnormalized);

        await Assert.That(contextResult.Diagnostic).IsEqualTo("renderer_context_not_normalized");
    }

    [Test]
    public async Task RendererEscapesPunctuationBeforePassingCommitTitlesToTheFormatter()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new RendererFixture();
        string captured = Path.Combine(fixture.Root, "captured-context.json");
        fixture.WriteExecutable(
            $"if [ \"$1\" = \"--version\" ]; then printf '%s\\n' 'git-cliff 2.14.2'; exit 0; fi\ncp \"$6\" \"{captured}\"\nprintf '# Release 1.1.0\\n'");

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };
        ReleaseContext context = JsonSerializer.Deserialize<ReleaseContext>(fixture.Context(), options)!;
        string title = "show *bold* _italic_ `code` [link](path)";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            context with { Changes = [context.Changes[0] with { Title = title }] }, options) + "\n");

        GitCliffRenderResult result = fixture.Render(context: input);
        using JsonDocument rendered = JsonDocument.Parse(File.ReadAllBytes(captured));
        JsonElement commit = rendered.RootElement[0].GetProperty("commits")[0];
        string escaped = ReleaseArtifactPolicy.EscapeUntrustedMarkdown(title).Text!;

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "formatter input");
        await Assert.That(commit.GetProperty("message").GetString()).IsEqualTo(escaped);
        await Assert.That(commit.GetProperty("raw_message").GetString()).IsEqualTo(escaped);
        await Assert.That(escaped).IsNotEqualTo(title);
    }

    [Test]
    public async Task RendererRejectsSymlinkConfigAndKillsHungProcesses()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var symlink = new RendererFixture();
        symlink.WriteExecutable("if [ \"$1\" = \"--version\" ]; then printf '%s\\n' 'git-cliff 2.14.2'; exit 0; fi\nprintf '# Release 1.1.0\\n'");
        string symlinkTarget = Path.Combine(symlink.Root, "config-target.toml");
        File.Move(symlink.ConfigPath, symlinkTarget);
        File.CreateSymbolicLink(symlink.ConfigPath, symlinkTarget);
        await Assert.That(symlink.Render().Diagnostic).IsEqualTo("renderer_config_invalid");

        using var hung = new RendererFixture();
        hung.WriteExecutable(
            """
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.14.2'; exit 0; fi
            yes x
            """);
        GitCliffRenderResult result = hung.Render(timeout: TimeSpan.FromSeconds(1));
        await Assert.That(result.Diagnostic).IsEqualTo("renderer_process_limit_exceeded");
        await Assert.That(result.Markdown).IsNull();
    }

    [Test]
    public async Task HostileAmbientEnvironmentIsInert()
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
            if [ "$1" = "--version" ]; then printf '%s\n' 'git-cliff 2.14.2'; exit 0; fi
            [ "$GIT_CLIFF_CONFIG" != "$2" ] || exit 31
            [ "$GIT_CLIFF_CONTEXT" != "$6" ] || exit 32
            printf '# Release 1.1.0\n'
            """);

        string? oldConfig = Environment.GetEnvironmentVariable("GIT_CLIFF_CONFIG");
        string? oldContext = Environment.GetEnvironmentVariable("GIT_CLIFF_CONTEXT");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONFIG", candidateConfig);
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONTEXT", candidateConfig);
            GitCliffRenderResult result = fixture.Render();

            await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "hostile env");
            await Assert.That(File.Exists(marker)).IsFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONFIG", oldConfig);
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONTEXT", oldContext);
        }
    }

    private sealed class RendererFixture : IDisposable
    {
        public RendererFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-renderer-{Guid.NewGuid():N}");
            BundleRoot = Path.Combine(Root, "tool-bundle");
            IsolationRoot = Path.Combine(Root, "isolation");
            LockPath = Path.Combine(BundleRoot, "toolchain.lock.json");
            ConfigPath = Path.Combine(BundleRoot, "cliff.toml");
            Directory.CreateDirectory(BundleRoot);
            Directory.CreateDirectory(IsolationRoot);
            File.WriteAllText(ConfigPath, File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "eng", "release", "cliff.toml")));
        }

        public string Root { get; }
        public string BundleRoot { get; }
        public string IsolationRoot { get; }
        public string LockPath { get; }
        public string ConfigPath { get; }

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
                  "version": "2.14.2",
                  "platforms": [
                    {
                      "platform": "linux-x64",
                      "executable": "git-cliff",
                      "executableSha256": "{{digest}}",
                      "archive": "git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz",
                      "archiveSha256": "{{new string('1', 64)}}",
                      "source": "https://github.com/orhun/git-cliff/releases/download/v2.14.2/git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz"
                    }
                  ]
                }
                """);
        }

        public byte[] Context() => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "eng", "release", "tests", "ISLAMU.ReleaseEngineering.Tests", "Fixtures", "stable-release-context.v1.json"));

        public GitCliffRenderResult Render(byte[]? context = null, TimeSpan? timeout = null) =>
            GitCliffRenderer.Render(new GitCliffRenderRequest(
                LockPath,
                BundleRoot,
                ConfigPath,
                context ?? Context(),
                "linux-x64",
                IsolationRoot,
                timeout ?? TimeSpan.FromSeconds(2)));

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONFIG", null);
            Environment.SetEnvironmentVariable("GIT_CLIFF_CONTEXT", null);
            Directory.Delete(Root, recursive: true);
        }
    }
}
