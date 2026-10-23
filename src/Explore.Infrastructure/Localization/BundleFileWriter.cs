using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Common.Localization;
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
        bool tempOpened = false;

        try
        {
            var sortedTranslations = BundleSchema.ValidateAndSort(translations);
            byte[] output = JsonSerializer.SerializeToUtf8Bytes(sortedTranslations, SerializerOptions);
            if (output.Length > LocalizationBundlePolicy.MaximumBytes)
                throw new JsonException("Localization bundle output limit exceeded.");
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);

            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                tempOpened = true;
                await stream.WriteAsync(output, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(tempPath, finalPath, overwrite: true);

            _logger.LogInformation(
                "localization_bundle_persisted key_count={KeyCount}",
                translations.Count);

            return finalPath;
        }
        catch (Exception)
        {
            if (tempOpened)
                TryDeleteTemp(tempPath);
            _logger.LogError("localization_bundle_write_failed");
            throw new BundleWriteException("localization_bundle_write_failed");
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
        catch (Exception)
        {
            _logger.LogWarning("localization_bundle_health_probe_failed");
            return new WritablePathHealth(
                Exists: Directory.Exists(directory),
                Writable: false,
                Reason: "localization_bundle_health_probe_failed",
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
        catch (Exception)
        {
            _logger.LogDebug("localization_bundle_temporary_cleanup_failed");
        }
    }
}
