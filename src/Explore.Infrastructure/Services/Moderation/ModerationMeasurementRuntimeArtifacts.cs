using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;

namespace Explore.Infrastructure.Services.Moderation;

/// <summary>Reads only already-loaded products and their deployed locations, never receipt claims.</summary>
public sealed class ModerationMeasurementRuntimeArtifacts : IModerationMeasurementRuntimeArtifacts
{
    private static readonly ImmutableArray<string> ProductAssemblies =
    [
        "Explore.API", "Explore.Application", "Explore.Domain", "Explore.Persistence",
        "Explore.Infrastructure", "Explore.ServiceDefaults", "ISLAMU.Moderation.Application", "ISLAMU.Moderation.Domain"
    ];

    public async Task<ModerationMeasurementRuntimeArtifactObservation?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        var modules = ImmutableArray.CreateBuilder<ModerationMeasurementRuntimeModule>(ProductAssemblies.Length);
        foreach (var name in ProductAssemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = loaded.Where(assembly => assembly.GetName().Name == name).ToArray();
            if (matches.Length != 1 || matches[0].IsDynamic) return null;
            var assembly = matches[0];
            var module = await ReadArtifactAsync(assembly, assembly.Location, cancellationToken);
            if (module is null) return null;
            modules.Add(module);
        }
        var observation = modules.MoveToImmutable();
        return new(ProductDigest(observation), observation);
    }

    internal static string ProductDigest(ImmutableArray<ModerationMeasurementRuntimeModule> modules) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "moderation-products-v1",
            products = modules.Select(module => new { assembly = module.Assembly, sha256 = module.Sha256 })
        })));

    // This bounded file seam lets tests exercise real PE bytes without loading a synthetic deployment.
    internal static async Task<ModerationMeasurementRuntimeModule?> ReadArtifactAsync(
        Assembly assembly, string path, CancellationToken cancellationToken)
    {
        if (assembly.IsDynamic || string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            if (!IsRegularPath(path)) return null;
            var before = Stamp(path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (stream.Length is <= 0 or > 512L * 1024 * 1024 || stream.Length != before.Length) return null;
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            if (stream.ReadByte() != -1) return null;

            using var image = new MemoryStream(bytes, writable: false);
            using var pe = new PEReader(image);
            if (!pe.HasMetadata) return null;
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return null;
            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            var moduleId = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            if (name != assembly.GetName().Name || moduleId == Guid.Empty
                || moduleId != assembly.ManifestModule.ModuleVersionId) return null;

            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            // Reopen the actual path: a replaced file must not be mistaken for the held handle.
            if (!IsRegularPath(path) || Stamp(path) != before) return null;
            await using var current = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (current.Length != before.Length
                || Convert.ToHexStringLower(await SHA256.HashDataAsync(current, cancellationToken)) != digest
                || !IsRegularPath(path) || Stamp(path) != before) return null;
            return new(name, moduleId, digest);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (BadImageFormatException) { return null; }
    }

    private static (long Length, DateTime Modified, DateTime Created) Stamp(string path)
    {
        var file = new FileInfo(path);
        return (file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc);
    }

    private static bool IsRegularPath(string path)
    {
        var current = Path.GetFullPath(path);
        if ((File.GetAttributes(current) & FileAttributes.Directory) != 0) return false;
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current) return true;
            current = parent;
        }
    }
}
