using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Conservative differ across <c>.env.example</c> (with strict value redaction) and JSON Schema
/// configuration contracts under <c>schemas/*configuration*.schema.json</c>.
/// </summary>
public sealed class ConfigurationManifestDiffer : IReleaseContractDiffer
{
    private const string EnvExamplePath = ".env.example";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Regex ActiveEnvPattern = new(
        "^(?<name>[A-Za-z][A-Za-z0-9_]*)=(?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex CommentedEnvPattern = new(
        "^#\\s*(?<name>[A-Za-z][A-Za-z0-9_]*)=(?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    public string Family => "configuration";

    public int Order => 20;

    public ContractDifferResult Diff(IGitObjectReader reader, string baseOid, string headOid)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var changes = new List<ContractDiffChange>();
        string? envDiagnostic = DiffEnvExample(reader, baseOid, headOid, changes);
        if (envDiagnostic is not null)
        {
            return new ContractDifferResult(false, Family, [], Diagnostic: envDiagnostic);
        }

        string? schemaDiagnostic = DiffConfigurationSchemas(reader, baseOid, headOid, changes);
        if (schemaDiagnostic is not null)
        {
            return new ContractDifferResult(false, Family, [], Diagnostic: schemaDiagnostic);
        }

        return new ContractDifferResult(true, Family, changes);
    }

    private string? DiffEnvExample(IGitObjectReader reader, string baseOid, string headOid, List<ContractDiffChange> changes)
    {
        GitBlobReadResult baseBlob = reader.ReadBlob(baseOid, EnvExamplePath);
        GitBlobReadResult headBlob = reader.ReadBlob(headOid, EnvExamplePath);
        if (baseBlob.Diagnostic is not null || headBlob.Diagnostic is not null)
        {
            return baseBlob.Diagnostic ?? headBlob.Diagnostic;
        }

        if (!baseBlob.Found && !headBlob.Found)
        {
            return null;
        }

        if (baseBlob.Found && !headBlob.Found)
        {
            changes.Add(new ContractDiffChange(
                Family,
                ContractChangeSeverity.Breaking,
                "contract_file_removed",
                EnvExamplePath,
                $"Removed environment configuration template '{EnvExamplePath}'.",
                EnvExamplePath));
            return null;
        }

        if (baseBlob.Found && headBlob.Found &&
            baseBlob.Bytes is not null && headBlob.Bytes is not null &&
            baseBlob.Bytes.AsSpan().SequenceEqual(headBlob.Bytes))
        {
            return null;
        }

        Dictionary<string, EnvEntry> baseEntries;
        Dictionary<string, EnvEntry> headEntries;
        try
        {
            baseEntries = baseBlob.Found && baseBlob.Bytes is not null
                ? ParseEnvEntries(StrictUtf8.GetString(baseBlob.Bytes))
                : new Dictionary<string, EnvEntry>(StringComparer.Ordinal);
            headEntries = headBlob.Found && headBlob.Bytes is not null
                ? ParseEnvEntries(StrictUtf8.GetString(headBlob.Bytes))
                : new Dictionary<string, EnvEntry>(StringComparer.Ordinal);
        }
        catch (DecoderFallbackException)
        {
            return "contract_diff_env_encoding_invalid";
        }

        foreach (string name in baseEntries.Keys.Union(headEntries.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseEntries.TryGetValue(name, out EnvEntry? baseEntry);
            bool hasHead = headEntries.TryGetValue(name, out EnvEntry? headEntry);

            if (hasBase && !hasHead)
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Breaking,
                    "env_var_removed",
                    name,
                    $"Removed environment variable '{name}' from {EnvExamplePath}.",
                    EnvExamplePath));
                continue;
            }

            if (!hasBase && hasHead)
            {
                bool requiredInput = !headEntry!.IsCommentedOptional && !headEntry.HasNonEmptyDefault;
                changes.Add(new ContractDiffChange(
                    Family,
                    requiredInput ? ContractChangeSeverity.ReviewRequired : ContractChangeSeverity.Additive,
                    requiredInput ? "env_var_added_required" : "env_var_added",
                    name,
                    requiredInput
                        ? $"Added active environment variable '{name}' without default in {EnvExamplePath}; verify operator provisioning."
                        : $"Added environment variable '{name}' to {EnvExamplePath} (value redacted).",
                    EnvExamplePath));
                continue;
            }

            if (baseEntry!.IsCommentedOptional != headEntry!.IsCommentedOptional)
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "env_var_activation_changed",
                    name,
                    $"Environment variable '{name}' changed between optional commented template and active entry in {EnvExamplePath}.",
                    EnvExamplePath));
            }
            else if (!string.Equals(baseEntry.ValueDigest, headEntry.ValueDigest, StringComparison.Ordinal))
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "env_var_default_changed",
                    name,
                    $"Default value changed for environment variable '{name}' in {EnvExamplePath} (value redacted).",
                    EnvExamplePath));
            }
        }

        return null;
    }

    private string? DiffConfigurationSchemas(IGitObjectReader reader, string baseOid, string headOid, List<ContractDiffChange> changes)
    {
        GitTreeListResult baseTree = reader.ListFiles(baseOid, "schemas");
        if (!baseTree.IsValid)
        {
            return baseTree.Diagnostic;
        }

        GitTreeListResult headTree = reader.ListFiles(headOid, "schemas");
        if (!headTree.IsValid)
        {
            return headTree.Diagnostic;
        }

        HashSet<string> baseSchemas = baseTree.Paths.Where(IsConfigurationSchemaPath).ToHashSet(StringComparer.Ordinal);
        HashSet<string> headSchemas = headTree.Paths.Where(IsConfigurationSchemaPath).ToHashSet(StringComparer.Ordinal);

        foreach (string path in baseSchemas.Union(headSchemas, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool inBase = baseSchemas.Contains(path);
            bool inHead = headSchemas.Contains(path);

            if (inBase && !inHead)
            {
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.Breaking,
                    "contract_file_removed",
                    path,
                    $"Removed configuration JSON Schema '{path}'.",
                    path));
                continue;
            }

            if (!inBase && inHead)
            {
                GitBlobReadResult addedBlob = reader.ReadBlob(headOid, path);
                if (addedBlob.Diagnostic is not null || !addedBlob.Found || addedBlob.Bytes is null)
                {
                    return addedBlob.Diagnostic ?? "contract_diff_config_schema_read_failed";
                }

                if (!OpenApiContractDiffer.TryParseObject(addedBlob.Bytes, out JsonDocument? addedDoc))
                {
                    return "contract_diff_config_schema_malformed";
                }

                addedDoc.Dispose();
                changes.Add(new ContractDiffChange(
                    Family,
                    ContractChangeSeverity.ReviewRequired,
                    "contract_file_added",
                    path,
                    $"Added configuration JSON Schema '{path}'; review operator configuration requirements.",
                    path));
                continue;
            }

            GitBlobReadResult baseBlob = reader.ReadBlob(baseOid, path);
            GitBlobReadResult headBlob = reader.ReadBlob(headOid, path);
            if (baseBlob.Diagnostic is not null || headBlob.Diagnostic is not null ||
                !baseBlob.Found || !headBlob.Found || baseBlob.Bytes is null || headBlob.Bytes is null)
            {
                return baseBlob.Diagnostic ?? headBlob.Diagnostic ?? "contract_diff_config_schema_read_failed";
            }

            if (!OpenApiContractDiffer.TryParseObject(baseBlob.Bytes, out JsonDocument? baseDoc))
            {
                return "contract_diff_config_schema_malformed";
            }

            using (baseDoc)
            {
                if (!OpenApiContractDiffer.TryParseObject(headBlob.Bytes, out JsonDocument? headDoc))
                {
                    return "contract_diff_config_schema_malformed";
                }

                using (headDoc)
                {
                    if (baseBlob.Bytes.AsSpan().SequenceEqual(headBlob.Bytes))
                    {
                        continue;
                    }

                    ConservativeJsonSchemaDiffer.DiffSchema(
                        Family,
                        path,
                        "$",
                        baseDoc.RootElement,
                        headDoc.RootElement,
                        baseDoc.RootElement,
                        headDoc.RootElement,
                        changes);
                    DiffDefs(baseDoc.RootElement, headDoc.RootElement, path, changes);
                }
            }
        }

        return null;
    }

    private void DiffDefs(JsonElement baseRoot, JsonElement headRoot, string artifactPath, List<ContractDiffChange> changes)
    {
        Dictionary<string, JsonElement> baseDefs = ReadDefs(baseRoot);
        Dictionary<string, JsonElement> headDefs = ReadDefs(headRoot);

        foreach (string name in baseDefs.Keys.Union(headDefs.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseDefs.TryGetValue(name, out JsonElement baseDef);
            bool hasHead = headDefs.TryGetValue(name, out JsonElement headDef);
            string target = $"$defs.{name}";

            if (hasBase && !hasHead)
            {
                changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Breaking, "schema_removed", target, $"Removed configuration definition '{target}'.", artifactPath));
                continue;
            }

            if (!hasBase && hasHead)
            {
                changes.Add(new ContractDiffChange(Family, ContractChangeSeverity.Additive, "schema_added", target, $"Added configuration definition '{target}'.", artifactPath));
                continue;
            }

            ConservativeJsonSchemaDiffer.DiffSchema(
                Family,
                artifactPath,
                target,
                baseRoot,
                headRoot,
                baseDef,
                headDef,
                changes);
        }
    }

    private static Dictionary<string, JsonElement> ReadDefs(JsonElement root)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.TryGetProperty("$defs", out JsonElement defs) && defs.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in defs.EnumerateObject())
            {
                map[prop.Name] = prop.Value;
            }
        }

        return map;
    }

    private static Dictionary<string, EnvEntry> ParseEnvEntries(string content)
    {
        var entries = new Dictionary<string, EnvEntry>(StringComparer.Ordinal);
        foreach (string rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            Match active = ActiveEnvPattern.Match(line);
            if (active.Success)
            {
                string name = active.Groups["name"].Value;
                string value = active.Groups["value"].Value.Trim();
                entries[name] = new EnvEntry(IsCommentedOptional: false, value.Length > 0, HashValue(value));
                continue;
            }

            Match commented = CommentedEnvPattern.Match(line);
            if (commented.Success)
            {
                string name = commented.Groups["name"].Value;
                if (!LooksLikeEnvVariableName(name))
                {
                    continue;
                }

                string value = commented.Groups["value"].Value.Trim();
                // Active declarations take precedence if a variable is both commented and uncommented.
                if (!entries.TryGetValue(name, out EnvEntry? existing) || existing.IsCommentedOptional)
                {
                    entries[name] = new EnvEntry(IsCommentedOptional: true, value.Length > 0, HashValue(value));
                }
            }
        }

        return entries;
    }

    private static bool LooksLikeEnvVariableName(string name) =>
        name.Contains('_', StringComparison.Ordinal) ||
        (name.Length >= 2 && name.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)));

    private static string HashValue(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(value)));

    private static bool IsConfigurationSchemaPath(string path) =>
        path.StartsWith("schemas/", StringComparison.Ordinal) &&
        path.EndsWith(".schema.json", StringComparison.Ordinal) &&
        path.Contains("configuration", StringComparison.OrdinalIgnoreCase);

    private sealed record EnvEntry(bool IsCommentedOptional, bool HasNonEmptyDefault, string ValueDigest);
}
