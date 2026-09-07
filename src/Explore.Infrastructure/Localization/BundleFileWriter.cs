using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Localization;

/// <summary>
/// Default <see cref="IBundleFileWriter"/>: persists bundles to the server's local filesystem.
/// <para>
/// Atomic write: serializes to <c>{code}.json.tmp</c> then <c>File.Move(..., overwrite: true)</c>.
/// Default <see cref="JsonSerializerOptions"/> only — NO <c>UnsafeRelaxedJsonEscaping</c>
/// (safe Unicode escaping is correct for Arabic/Hebrew bundle content).
/// </para>
/// </summary>
public sealed class BundleFileWriter : IBundleFileWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<BundleFileWriter> _logger;

    public BundleFileWriter(IWebHostEnvironment environment, ILogger<BundleFileWriter> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    private string BundleDirectory =>
        Path.Combine(_environment.ContentRootPath, "App_Data", "Localization", "Bundles");

    public async Task<string> WriteBundleAsync(
        string languageCode,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken ct = default)
    {
        var normalised = languageCode.Trim().ToLowerInvariant();
        var directory = BundleDirectory;
        var finalPath = Path.Combine(directory, $"{normalised}.json");
        var tempPath = finalPath + ".tmp";

        try
        {
            Directory.CreateDirectory(directory);

            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                var sortedTranslations = BundleSchema.ValidateAndSort(translations);
                await JsonSerializer.SerializeAsync(stream, sortedTranslations, SerializerOptions, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(tempPath, finalPath, overwrite: true);

            _logger.LogInformation(
                "[LOCALIZATION] Persisted bundle for {Language}: {Count} keys → {Path}",
                normalised, translations.Count, finalPath);

            return finalPath;
        }
        catch (Exception ex)
        {
            TryDeleteTemp(tempPath);
            _logger.LogError(ex, "[LOCALIZATION] Failed to write bundle for {Language} at {Path}", normalised, finalPath);
            throw new BundleWriteException(
                $"Failed to write bundle for language '{normalised}' at '{finalPath}': {ex.Message}",
                ex);
        }
    }

    public async Task<WritablePathHealth> CheckHealthAsync(CancellationToken ct = default)
    {
        var directory = BundleDirectory;

        try
        {
            var exists = Directory.Exists(directory);
            if (!exists)
            {
                Directory.CreateDirectory(directory);
                exists = true;
            }

            var probePath = Path.Combine(directory, ".healthcheck.tmp");
            await File.WriteAllBytesAsync(probePath, [], ct);
            File.Delete(probePath);

            return new WritablePathHealth(
                Exists: exists,
                Writable: true,
                Reason: null,
                TargetPath: directory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LOCALIZATION] Bundle directory health probe failed at {Path}", directory);
            return new WritablePathHealth(
                Exists: Directory.Exists(directory),
                Writable: false,
                Reason: ex.Message,
                TargetPath: directory);
        }
    }

    private void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[LOCALIZATION] Could not delete stale temp file {Path}", path);
        }
    }
}
