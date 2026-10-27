using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Services.Moderation;

namespace ISLAMU.Moderation.Measurement;

/// <summary>
/// Versioned publication contract, not a caller verdict. Only exact catalogue bytes whose
/// SHA-256 was independently enrolled can select cases. No catalogue is shipped until
/// the programme's real cases and native identities have been published and approved.
/// </summary>
internal static class PinnedCatalogue
{
    internal static ImmutableArray<string> Bundles => ModerationMeasurementCatalogue.Bundles;

    internal static async Task<(ModerationMeasurementCatalogue Catalogue, string Digest)> LoadAsync(
        string root, string approvedDigest, CancellationToken token)
    {
        var path = Path.Combine(root, "eng/moderation/catalogue.v1.json");
        Require.That(File.Exists(path), "catalogue-unpublished");
        var bytes = await Artifacts.ReadAsync(path, ModerationMeasurementCatalogue.MaximumBytes, token);
        var digest = Artifacts.Hash(bytes);
        Require.That(digest == approvedDigest, "catalogue-unapproved");
        var catalogue = ModerationMeasurementCatalogue.Parse(bytes);
        Require.That(catalogue.RuntimeVersion == Environment.Version.ToString(), "catalogue-runtime-mismatch");
        return (catalogue, digest);
    }

    internal static string BundleDirectory(string root, string bundle)
    {
        Require.That(Bundles.Contains(bundle), "bundle-unknown");
        return Path.Combine(root, "tests", bundle, "bin/Release/net10.0");
    }
}

internal sealed record ArtifactSnapshot(string RunnerDigest, string TestBundleDigest,
    string ProductArtifactDigest, string RuntimeGeneration, string InventoryFingerprint);

internal static class Artifacts
{
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    internal static bool IsDigest(string value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void RegularPath(string path)
    {
        var current = Path.GetFullPath(path);
        while (true)
        {
            Require.That((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "artifact-link");
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current) break;
            current = parent;
        }
    }

    internal static async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken token)
    {
        RegularPath(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require.That(stream.Length is > 0 && stream.Length <= maximumBytes, "artifact-size");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token);
        Require.That(stream.ReadByte() == -1, "artifact-changed");
        return bytes;
    }

    internal static async Task<string> FileDigestAsync(string path, CancellationToken token)
    {
        RegularPath(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require.That(stream.Length <= 512L * 1024 * 1024, "artifact-size");
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
    }

    // The domain-separated manifest includes every relative filename, length and content hash.
    // An unlisted dependency/configuration/native library cannot slip into an approved bundle.
    internal static async Task<string> DirectoryDigestAsync(string directory, CancellationToken token)
    {
        RegularPath(directory);
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        var entryCount = 0;
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                Require.That(++entryCount <= 30_000, "artifact-count");
                RegularPath(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
                else found.Add(entry);
            }
        }
        var files = found.Order(StringComparer.Ordinal).ToArray();
        Require.That(files.Length is > 0 and <= 20_000, "artifact-count");
        long length = 0;
        var entries = new List<object>();
        foreach (var file in files)
        {
            var size = new FileInfo(file).Length;
            length = checked(length + size);
            Require.That(length <= 4L * 1024 * 1024 * 1024, "artifact-size");
            entries.Add(new { path = Path.GetRelativePath(directory, file).Replace('\\', '/'),
                length = size, sha256 = await FileDigestAsync(file, token) });
        }
        return Hash(JsonSerializer.SerializeToUtf8Bytes(new { format = "moderation-directory-v1", files = entries }));
    }

    internal static async Task<ArtifactSnapshot> MeasureAsync(
        string root, string consumerId, ModerationNativeMode mode, CancellationToken token)
    {
        var bundles = new List<object>();
        foreach (var bundle in PinnedCatalogue.Bundles)
        {
            var directory = PinnedCatalogue.BundleDirectory(root, bundle);
            Require.That(File.Exists(Path.Combine(directory, bundle + ".dll"))
                && File.Exists(Path.Combine(directory, bundle + ".runtimeconfig.json")), "bundle-unpublished");
            bundles.Add(new { bundle, digest = await DirectoryDigestAsync(directory, token) });
        }
        var api = PinnedCatalogue.BundleDirectory(root, "Event.API.IntegrationTests");
        var products = new List<ModerationMeasurementProductArtifact>();
        foreach (var name in ModerationMeasurementProductManifest.AssemblyNames)
        {
            var file = name + ".dll";
            var digest = await FileDigestAsync(Path.Combine(api, file), token);
            foreach (var bundle in PinnedCatalogue.Bundles)
            {
                var copy = Path.Combine(PinnedCatalogue.BundleDirectory(root, bundle), file);
                if (File.Exists(copy))
                    Require.That(await FileDigestAsync(copy, token) == digest, "product-artifact-mismatch");
            }
            if (name != "Explore.API")
                Require.That(await FileDigestAsync(Path.Combine(AppContext.BaseDirectory, file), token) == digest,
                    "loaded-product-mismatch");
            products.Add(new(name, digest));
        }
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        Require.That(inventory.Consumers.ContainsKey(consumerId), "consumer-unknown");
        var productDigest = ModerationMeasurementProductManifest.Digest([.. products]);
        var runtime = ModerationMeasurementProductManifest.RuntimeGeneration(
            consumerId, mode, inventory.Fingerprint, productDigest);
        var dotnet = Path.GetFullPath(Path.Combine(
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "../../..", "dotnet"));
        var runner = Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "moderation-runner-v1",
            directory = await DirectoryDigestAsync(AppContext.BaseDirectory, token),
            dotnet = await FileDigestAsync(dotnet, token),
            sessionLauncher = await FileDigestAsync(OwnedExecution.SessionLauncher, token),
            runtime = await FileDigestAsync(typeof(object).Assembly.Location, token)
        }));
        return new(runner,
            Hash(JsonSerializer.SerializeToUtf8Bytes(new { format = "moderation-bundles-v1", bundles })),
            productDigest,
            runtime, inventory.Fingerprint);
    }
}

internal sealed class MeasurementFailure(string category) : Exception(category)
{
    internal string Category { get; } = category;
}

internal static class Require
{
    internal static void That(bool condition, string category)
    {
        if (!condition) throw new MeasurementFailure(category);
    }

    internal static bool Identity(string value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
}
