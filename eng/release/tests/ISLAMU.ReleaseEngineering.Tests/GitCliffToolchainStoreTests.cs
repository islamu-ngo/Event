using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class GitCliffToolchainStoreTests
{
    [Test]
    public async Task RepositoryLockPinsGitCliff2142AcrossFourPlatformsWithFreshDigests()
    {
        string lockPath = Path.Combine(RepositoryRoot.Find(), "eng", "release", "toolchain.lock.json");
        ToolchainLockLoadResult loaded = GitCliffToolchainStore.LoadLock(lockPath);

        await Assert.That(loaded.IsValid).IsTrue().Because(loaded.Diagnostic ?? "valid repository toolchain lock");
        await Assert.That(loaded.Lock!.Tool).IsEqualTo("git-cliff");
        await Assert.That(loaded.Lock.Version).IsEqualTo("2.14.2");

        string[] platforms = loaded.Lock.Platforms.Select(entry => entry.Platform).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(platforms).IsEquivalentTo(["linux-arm64", "linux-x64", "osx-arm64", "windows-x64"]);

        string[] retired2131Digests =
        [
            "25d1281e34da5c45b22d9c174425c1099e7b3aa24c9e1f2d78272df09a6a8dde",
            "200d2535da6d9703f3bcc8a4d159c3b55eacdb01cf2148c55b3eee9dd04d5249",
            "af0e46671560e716ec634b398f2eedd45b2b4e01ca1b43445094e6e0bac94039",
            "3ae3a5549e85c7ad5b20192ebcfee4371269deca51255f6f2f2e051c6541f5ca",
        ];

        foreach (ToolchainPlatformEntry entry in loaded.Lock.Platforms)
        {
            await Assert.That(entry.ArchiveSha256.Length).IsEqualTo(64);
            await Assert.That(entry.ExecutableSha256.Length).IsEqualTo(64);
            await Assert.That(entry.Source.StartsWith("https://github.com/orhun/git-cliff/releases/download/v2.14.2/", StringComparison.Ordinal)).IsTrue();
            await Assert.That(retired2131Digests).DoesNotContain(entry.ArchiveSha256);
            await Assert.That(retired2131Digests).DoesNotContain(entry.ExecutableSha256);
        }
    }

    [Test]
    public async Task VerifyInstalledNeverDownloadsAndRejectsMissingOrMismatchedArtifacts()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new StoreFixture();
        byte[] binary = fixture.BuildFakeExecutableBytes("git-cliff 2.14.2");
        byte[] archive = StoreFixture.BuildTarGz([("git-cliff-2.14.2/git-cliff", binary, TarEntryType.RegularFile)]);
        fixture.WriteLock(archive, binary);

        ToolchainVerificationResult missing = GitCliffToolchainStore.VerifyInstalled(
            fixture.LockPath,
            fixture.InstallDirectory,
            "linux-x64",
            TimeSpan.FromSeconds(2));
        await Assert.That(missing.IsValid).IsFalse();
        await Assert.That(missing.Diagnostic).IsEqualTo("required executable is missing: git-cliff");

        string executablePath = Path.Combine(fixture.InstallDirectory, "git-cliff");
        await File.WriteAllBytesAsync(executablePath, fixture.BuildFakeExecutableBytes("git-cliff 2.14.2 tampered"));
        File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        ToolchainVerificationResult wrongDigest = GitCliffToolchainStore.VerifyInstalled(
            fixture.LockPath,
            fixture.InstallDirectory,
            "linux-x64",
            TimeSpan.FromSeconds(2));
        await Assert.That(wrongDigest.IsValid).IsFalse();
        await Assert.That(wrongDigest.Diagnostic).IsEqualTo("executable digest mismatch: git-cliff");

        byte[] wrongVersionBinary = fixture.BuildFakeExecutableBytes("git-cliff 2.13.1");
        byte[] wrongVersionArchive = StoreFixture.BuildTarGz([("git-cliff-2.14.2/git-cliff", wrongVersionBinary, TarEntryType.RegularFile)]);
        fixture.WriteLock(wrongVersionArchive, wrongVersionBinary);
        await File.WriteAllBytesAsync(executablePath, wrongVersionBinary);

        ToolchainVerificationResult wrongVersion = GitCliffToolchainStore.VerifyInstalled(
            fixture.LockPath,
            fixture.InstallDirectory,
            "linux-x64",
            TimeSpan.FromSeconds(2));
        await Assert.That(wrongVersion.IsValid).IsFalse();
        await Assert.That(wrongVersion.Diagnostic).IsEqualTo("version mismatch: expected git-cliff 2.14.2");
    }

    [Test]
    public async Task EnsureProvisionedExtractsVerifiedTarGzAndReusesCachedBinaryOffline()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new StoreFixture();
        byte[] binary = fixture.BuildFakeExecutableBytes("git-cliff 2.14.2");
        byte[] archive = StoreFixture.BuildTarGz([
            ("git-cliff-2.14.2/LICENSE-MIT", "MIT"u8.ToArray(), TarEntryType.RegularFile),
            ("git-cliff-2.14.2/git-cliff", binary, TarEntryType.RegularFile),
        ]);
        fixture.WriteLock(archive, binary);

        int fetchCalls = 0;
        ToolchainVerificationResult provisioned = GitCliffToolchainStore.EnsureProvisioned(
            fixture.LockPath,
            fixture.InstallDirectory,
            "linux-x64",
            TimeSpan.FromSeconds(2),
            _ =>
            {
                fetchCalls++;
                return archive;
            });

        await Assert.That(provisioned.IsValid).IsTrue().Because(provisioned.Diagnostic ?? "provisioned tar.gz");
        await Assert.That(fetchCalls).IsEqualTo(1);
        await Assert.That(File.Exists(provisioned.Executable!.ExecutablePath)).IsTrue();

        ToolchainVerificationResult reused = GitCliffToolchainStore.EnsureProvisioned(
            fixture.LockPath,
            fixture.InstallDirectory,
            "linux-x64",
            TimeSpan.FromSeconds(2),
            _ =>
            {
                fetchCalls++;
                throw new InvalidOperationException("must not fetch when already verified");
            });

        await Assert.That(reused.IsValid).IsTrue();
        await Assert.That(fetchCalls).IsEqualTo(1);
    }

    [Test]
    public async Task EnsureProvisionedRejectsArchiveHashMismatchBeforeExtraction()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new StoreFixture();
        byte[] binary = fixture.BuildFakeExecutableBytes("git-cliff 2.14.2");
        byte[] expectedArchive = StoreFixture.BuildTarGz([("git-cliff-2.14.2/git-cliff", binary, TarEntryType.RegularFile)]);
        byte[] tamperedArchive = StoreFixture.BuildTarGz([("git-cliff-2.14.2/git-cliff", binary.Concat("\n"u8.ToArray()).ToArray(), TarEntryType.RegularFile)]);
        fixture.WriteLock(expectedArchive, binary);

        ToolchainVerificationResult result = GitCliffToolchainStore.EnsureProvisioned(
            fixture.LockPath,
            fixture.InstallDirectory,
            "linux-x64",
            TimeSpan.FromSeconds(2),
            _ => tamperedArchive);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostic).IsEqualTo("archive digest mismatch: git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz");
        await Assert.That(File.Exists(Path.Combine(fixture.InstallDirectory, "git-cliff"))).IsFalse();
    }

    [Test]
    [Arguments("../git-cliff", TarEntryType.RegularFile)]
    [Arguments("/tmp/git-cliff", TarEntryType.RegularFile)]
    [Arguments("git-cliff-2.14.2/../../escape", TarEntryType.RegularFile)]
    [Arguments("git-cliff-2.14.2/link", TarEntryType.SymbolicLink)]
    public async Task EnsureProvisionedRejectsUnsafeTarArchiveEntries(string entryName, TarEntryType entryType)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new StoreFixture();
        byte[] binary = fixture.BuildFakeExecutableBytes("git-cliff 2.14.2");
        byte[] archive = StoreFixture.BuildTarGz([
            (entryName, entryType == TarEntryType.RegularFile ? binary : [], entryType),
            ("git-cliff-2.14.2/git-cliff", binary, TarEntryType.RegularFile),
        ]);
        fixture.WriteLock(archive, binary);

        ToolchainVerificationResult result = GitCliffToolchainStore.EnsureProvisioned(
            fixture.LockPath,
            fixture.InstallDirectory,
            "linux-x64",
            TimeSpan.FromSeconds(2),
            _ => archive);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostic).Contains("unsafe archive");
        await Assert.That(File.Exists(Path.Combine(fixture.InstallDirectory, "git-cliff"))).IsFalse();
    }

    [Test]
    public async Task EnsureProvisionedRejectsZipPathTraversalAndExtractsSafeZip()
    {
        using var fixture = new StoreFixture();
        byte[] binary = "fake-windows-pe"u8.ToArray();
        byte[] traversalZip = StoreFixture.BuildZip([
            ("../evil.exe", binary),
            ("git-cliff-2.14.2/git-cliff.exe", binary),
        ]);
        fixture.WriteLock(traversalZip, binary, platform: "windows-x64", executable: "git-cliff.exe", archiveName: "git-cliff-2.14.2-x86_64-pc-windows-msvc.zip");

        ToolchainVerificationResult rejected = GitCliffToolchainStore.EnsureProvisioned(
            fixture.LockPath,
            fixture.InstallDirectory,
            "windows-x64",
            TimeSpan.FromSeconds(2),
            _ => traversalZip);

        await Assert.That(rejected.IsValid).IsFalse();
        await Assert.That(rejected.Diagnostic).Contains("unsafe archive");

        byte[] safeZip = StoreFixture.BuildZip([
            ("git-cliff-2.14.2/git-cliff.exe", binary),
        ]);
        byte[] extracted = GitCliffToolchainStore.ExtractVerifiedExecutableFromArchiveBytes(
            safeZip,
            new ToolchainPlatformEntry(
                "windows-x64",
                "git-cliff.exe",
                StoreFixture.Sha256(binary),
                "git-cliff-2.14.2-x86_64-pc-windows-msvc.zip",
                StoreFixture.Sha256(safeZip),
                "https://github.com/orhun/git-cliff/releases/download/v2.14.2/git-cliff-2.14.2-x86_64-pc-windows-msvc.zip"));
        await Assert.That(extracted).IsEquivalentTo(binary);
    }

    [Test]
    public async Task CreateOfflineNoExecStartInfoEnforcesMandatoryFlagsAndScrubsAmbientEnvironment()
    {
        var tool = new VerifiedGitCliffExecutable(
            "2.14.2",
            "linux-x64",
            "/opt/verified/git-cliff",
            new string('a', 64));

        var startInfo = GitCliffToolchainStore.CreateOfflineNoExecStartInfo(
            tool,
            "/repo/eng/release/cliff.toml",
            "/tmp/work",
            ["--context"]);

        await Assert.That(startInfo.UseShellExecute).IsFalse();
        await Assert.That(startInfo.FileName).IsEqualTo("/opt/verified/git-cliff");
        await Assert.That(startInfo.WorkingDirectory).IsEqualTo("/tmp/work");
        await Assert.That(startInfo.ArgumentList).IsEquivalentTo([
            "--config",
            "/repo/eng/release/cliff.toml",
            "--offline",
            "--no-exec",
            "--context",
        ]);
        await Assert.That(startInfo.Environment["GIT_CLIFF_OFFLINE"]).IsEqualTo("1");
        await Assert.That(startInfo.Environment.ContainsKey("GITHUB_TOKEN")).IsFalse();
        await Assert.That(startInfo.Environment.ContainsKey("GIT_CLIFF_CONFIG")).IsFalse();
        await Assert.That(startInfo.Environment.ContainsKey("GIT_CLIFF_CONTEXT")).IsFalse();
        await Assert.That(startInfo.Environment.ContainsKey("SOURCE_DATE_EPOCH")).IsFalse();
    }

    private sealed class StoreFixture : IDisposable
    {
        public StoreFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-toolchain-store-{Guid.NewGuid():N}");
            InstallDirectory = Path.Combine(Root, "store");
            LockPath = Path.Combine(Root, "toolchain.lock.json");
            Directory.CreateDirectory(InstallDirectory);
        }

        public string Root { get; }
        public string InstallDirectory { get; }
        public string LockPath { get; }

        public byte[] BuildFakeExecutableBytes(string versionLine) =>
            Encoding.UTF8.GetBytes($"#!/bin/sh\nprintf '%s\\n' '{versionLine}'\n");

        public void WriteLock(
            byte[] archiveBytes,
            byte[] executableBytes,
            string platform = "linux-x64",
            string executable = "git-cliff",
            string archiveName = "git-cliff-2.14.2-x86_64-unknown-linux-musl.tar.gz")
        {
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                tool = "git-cliff",
                version = "2.14.2",
                platforms = new[]
                {
                    new
                    {
                        platform,
                        executable,
                        executableSha256 = Sha256(executableBytes),
                        archive = archiveName,
                        archiveSha256 = Sha256(archiveBytes),
                        source = $"https://github.com/orhun/git-cliff/releases/download/v2.14.2/{archiveName}",
                    },
                },
            });
            File.WriteAllText(LockPath, json);
        }

        public static byte[] BuildTarGz(IReadOnlyList<(string Name, byte[] Content, TarEntryType Type)> entries)
        {
            using var memory = new MemoryStream();
            using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
            using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
            {
                foreach ((string name, byte[] content, TarEntryType type) in entries)
                {
                    if (type == TarEntryType.SymbolicLink)
                    {
                        var symlink = new PaxTarEntry(TarEntryType.SymbolicLink, name)
                        {
                            LinkName = "/etc/passwd",
                        };
                        writer.WriteEntry(symlink);
                    }
                    else
                    {
                        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                        {
                            DataStream = new MemoryStream(content),
                        };
                        writer.WriteEntry(entry);
                    }
                }
            }

            return memory.ToArray();
        }

        public static byte[] BuildZip(IReadOnlyList<(string Name, byte[] Content)> entries)
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach ((string name, byte[] content) in entries)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(name);
                    using Stream stream = entry.Open();
                    stream.Write(content);
                }
            }

            return memory.ToArray();
        }

        public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
