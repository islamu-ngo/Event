using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// CLI entry point for <c>diff-contracts &lt;base-oid&gt; &lt;head-oid&gt; &lt;output-path&gt;</c>.
/// Reads exclusively from pinned Git revisions via <see cref="IGitObjectReader"/> and writes
/// canonical <c>contract-diff.v1.json</c> atomically without clobbering existing files.
/// </summary>
public static class ContractDiffCommand
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static int Run(string[] args, TextWriter output, string repositoryRoot, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        TimeSpan effectiveTimeout = timeout < TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : timeout;
        var reader = new GitProcessObjectReader(repositoryRoot, effectiveTimeout);
        return Run(args, output, reader);
    }

    public static int Run(string[] args, TextWriter output, IGitObjectReader reader)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(reader);

        if (args.Length != 4)
        {
            output.WriteLine("invalid_arguments: diff-contracts requires <base-oid> <head-oid> <output-path>");
            return Program.UsageError;
        }

        string baseOid = args[1];
        string headOid = args[2];
        string rawOutputPath = args[3];

        if (!TryValidateOutputPath(rawOutputPath, out string? fullOutputPath, out string? pathDiagnostic))
        {
            return Reject(output, pathDiagnostic!);
        }

        ReleaseContractDiffResult result = ReleaseContractDiffPipeline.Default.Execute(reader, baseOid, headOid);
        if (!result.IsValid || result.Artifact is null || result.Json is null)
        {
            return Reject(output, result.Diagnostic ?? "contract_diff_failed");
        }

        if (!ValidateArtifactSchema(result.Json))
        {
            return Reject(output, "contract_diff_schema_invalid");
        }

        try
        {
            WriteAtomicNoClobber(fullOutputPath!, StrictUtf8.GetBytes(result.Json));
        }
        catch (IOException)
        {
            return Reject(output, File.Exists(fullOutputPath!) ? "contract_diff_output_exists" : "contract_diff_output_write_failed");
        }
        catch (UnauthorizedAccessException)
        {
            return Reject(output, "contract_diff_output_write_failed");
        }

        ContractDiffCounts counts = result.Artifact.Summary;
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"contract_diff_written: path={rawOutputPath} total={counts.Total} breaking={counts.Breaking} review_required={counts.ReviewRequired} additive={counts.Additive}"));
        return Program.Success;
    }

    private static bool TryValidateOutputPath(string rawPath, out string? fullPath, out string? diagnostic)
    {
        fullPath = null;
        diagnostic = null;

        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Any(char.IsControl))
        {
            diagnostic = "contract_diff_output_invalid";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostic = "contract_diff_output_invalid";
            return false;
        }

        if (Directory.Exists(fullPath) || HasReparsePointInExistingPath(fullPath))
        {
            diagnostic = "contract_diff_output_invalid";
            return false;
        }

        if (File.Exists(fullPath))
        {
            diagnostic = "contract_diff_output_exists";
            return false;
        }

        return true;
    }

    private static bool HasReparsePointInExistingPath(string fullPath)
    {
        string? current = fullPath;
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return false;
    }

    private static bool ValidateArtifactSchema(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("schemaVersion", out JsonElement sv) && sv.ValueKind == JsonValueKind.Number && sv.GetInt32() == 1 &&
                root.TryGetProperty("baseOid", out JsonElement bo) && bo.ValueKind == JsonValueKind.String && GitProcessObjectReader.IsFullOid(bo.GetString()) &&
                root.TryGetProperty("headOid", out JsonElement ho) && ho.ValueKind == JsonValueKind.String && GitProcessObjectReader.IsFullOid(ho.GetString()) &&
                root.TryGetProperty("evaluatedFamilies", out JsonElement ef) && ef.ValueKind == JsonValueKind.Array &&
                root.TryGetProperty("summary", out JsonElement summary) && summary.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("changes", out JsonElement changes) && changes.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WriteAtomicNoClobber(string destinationPath, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(destinationPath)
            ?? throw new IOException("Output directory missing.");
        Directory.CreateDirectory(directory);
        if (HasReparsePointInExistingPath(directory))
        {
            throw new UnauthorizedAccessException("Output directory is a reparse point.");
        }

        string tempPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, destinationPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine($"contract_diff_failed: {diagnostic}");
        return Program.ToolchainRejected;
    }
}
