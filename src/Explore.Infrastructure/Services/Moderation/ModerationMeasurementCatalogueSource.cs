using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services.Moderation;

namespace Explore.Infrastructure.Services.Moderation;

/// <summary>
/// Loads only the digest-selected deployment sidecar. There is no working-directory search,
/// latest-file selection, authority cache, embedded fallback or caller-controlled path.
/// </summary>
public sealed class ModerationMeasurementCatalogueSource : IModerationMeasurementCatalogueSource
{
    public async Task<ModerationMeasurementCatalogueArtifact?> ReadAsync(
        string approvedDigest, CancellationToken cancellationToken = default)
    {
        if (!ModerationMeasurementCatalogue.IsDigest(approvedDigest)) return null;
        var path = Path.Combine(AppContext.BaseDirectory, "moderation", "catalogues",
            approvedDigest, "catalogue.v1.json");
        try
        {
            var current = path;
            while (true)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
                var parent = Path.GetDirectoryName(current);
                if (parent is null || parent == current) break;
                current = parent;
            }
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (stream.Length is 0 or > ModerationMeasurementCatalogue.MaximumBytes) return null;
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            if (stream.ReadByte() != -1 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != approvedDigest)
                return null;
            return new(approvedDigest, ModerationMeasurementCatalogue.Parse(bytes));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }
}
