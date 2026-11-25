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
    private const string SchemasDirectory = "schemas";
    private const string DefsKeyword = "$defs";
    private const string ReadFailedDiagnostic = "contract_diff_config_schema_read_failed";
    private const string MalformedDiagnostic = "contract_diff_config_schema_malformed";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Regex ActiveEnvPattern = new(
        "^(?<name>[A-Za-z][A-Za-z0-9_]*)=(?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex CommentedEnvPattern = new(
        "^#\\s*(?<name>[A-Za-z][A-Za-z0-9_]*)=(?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    public string Family => ContractFamilies.Configuration;

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
        return schemaDiagnostic is not null
            ? new ContractDifferResult(false, Family, [], Diagnostic: schemaDiagnostic)
            : new ContractDifferResult(true, Family, changes);
    }

    private string? DiffEnvExample(IGitObjectReader reader, string baseOid, string headOid, List<ContractDiffChange> changes)
    {
        GitBlobReadResult baseBlob = reader.ReadBlob(baseOid, EnvExamplePath);
        GitBlobReadResult headBlob = reader.ReadBlob(headOid, EnvExamplePath);
        if (baseBlob.Diagnostic is not null || headBlob.Diagnostic is not null)
        {
            return baseBlob.Diagnostic ?? headBlob.Diagnostic;
        }

        if ((!baseBlob.Found && !headBlob.Found) || AreIdenticalBlobs(baseBlob, headBlob))
        {
            return null;
        }

        var collector = new ContractChangeCollector(Family, EnvExamplePath, changes);
        if (baseBlob.Found && !headBlob.Found)
        {
            collector.Breaking(
                ContractChangeKinds.ContractFileRemoved,
                EnvExamplePath,
                $"Removed environment configuration template '{EnvExamplePath}'.");
            return null;
        }

        if (!TryParseEnvBlob(baseBlob, out Dictionary<string, EnvEntry> baseEntries) ||
            !TryParseEnvBlob(headBlob, out Dictionary<string, EnvEntry> headEntries))
        {
            return "contract_diff_env_encoding_invalid";
        }

        DiffEnvEntryMaps(baseEntries, headEntries, collector);
        return null;
    }

    private static void DiffEnvEntryMaps(
        Dictionary<string, EnvEntry> baseEntries,
        Dictionary<string, EnvEntry> headEntries,
        ContractChangeCollector collector)
    {
        foreach (string name in baseEntries.Keys.Union(headEntries.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseEntries.TryGetValue(name, out EnvEntry? baseEntry);
            bool hasHead = headEntries.TryGetValue(name, out EnvEntry? headEntry);
            DiffSingleEnvEntry(name, hasBase ? baseEntry : null, hasHead ? headEntry : null, collector);
        }
    }

    private static void DiffSingleEnvEntry(string name, EnvEntry? baseEntry, EnvEntry? headEntry, ContractChangeCollector collector)
    {
        if (baseEntry is not null && headEntry is null)
        {
            collector.Breaking(ContractChangeKinds.EnvVarRemoved, name, $"Removed environment variable '{name}' from {EnvExamplePath}.");
            return;
        }

        if (baseEntry is null && headEntry is not null)
        {
            bool requiredInput = !headEntry.IsCommentedOptional && !headEntry.HasNonEmptyDefault;
            if (requiredInput)
            {
                collector.ReviewRequired(
                    ContractChangeKinds.EnvVarAddedRequired,
                    name,
                    $"Added active environment variable '{name}' without default in {EnvExamplePath}; verify operator provisioning.");
            }
            else
            {
                collector.Additive(
                    ContractChangeKinds.EnvVarAdded,
                    name,
                    $"Added environment variable '{name}' to {EnvExamplePath} (value redacted).");
            }

            return;
        }

        if (baseEntry is null || headEntry is null)
        {
            return;
        }

        if (baseEntry.IsCommentedOptional != headEntry.IsCommentedOptional)
        {
            collector.ReviewRequired(
                ContractChangeKinds.EnvVarActivationChanged,
                name,
                $"Environment variable '{name}' changed between optional commented template and active entry in {EnvExamplePath}.");
        }
        else if (!string.Equals(baseEntry.ValueDigest, headEntry.ValueDigest, StringComparison.Ordinal))
        {
            collector.ReviewRequired(
                ContractChangeKinds.EnvVarDefaultChanged,
                name,
                $"Default value changed for environment variable '{name}' in {EnvExamplePath} (value redacted).");
        }
    }

    private string? DiffConfigurationSchemas(IGitObjectReader reader, string baseOid, string headOid, List<ContractDiffChange> changes)
    {
        GitTreeListResult baseTree = reader.ListFiles(baseOid, SchemasDirectory);
        if (!baseTree.IsValid)
        {
            return baseTree.Diagnostic;
        }

        GitTreeListResult headTree = reader.ListFiles(headOid, SchemasDirectory);
        if (!headTree.IsValid)
        {
            return headTree.Diagnostic;
        }

        HashSet<string> baseSchemas = baseTree.Paths.Where(IsConfigurationSchemaPath).ToHashSet(StringComparer.Ordinal);
        HashSet<string> headSchemas = headTree.Paths.Where(IsConfigurationSchemaPath).ToHashSet(StringComparer.Ordinal);

        foreach (string path in baseSchemas.Union(headSchemas, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            string? diagnostic = DiffSingleSchemaFile(reader, baseOid, headOid, path, baseSchemas.Contains(path), headSchemas.Contains(path), changes);
            if (diagnostic is not null)
            {
                return diagnostic;
            }
        }

        return null;
    }

    private string? DiffSingleSchemaFile(
        IGitObjectReader reader,
        string baseOid,
        string headOid,
        string path,
        bool inBase,
        bool inHead,
        List<ContractDiffChange> changes)
    {
        var collector = new ContractChangeCollector(Family, path, changes);
        if (inBase && !inHead)
        {
            collector.Breaking(ContractChangeKinds.ContractFileRemoved, path, $"Removed configuration JSON Schema '{path}'.");
            return null;
        }

        if (!inBase && inHead)
        {
            return ValidateAddedSchemaFile(reader, headOid, path, collector);
        }

        return DiffModifiedSchemaFile(reader, baseOid, headOid, path, collector);
    }

    private static string? ValidateAddedSchemaFile(IGitObjectReader reader, string headOid, string path, ContractChangeCollector collector)
    {
        GitBlobReadResult addedBlob = reader.ReadBlob(headOid, path);
        if (addedBlob.Diagnostic is not null || !addedBlob.Found || addedBlob.Bytes is null)
        {
            return addedBlob.Diagnostic ?? ReadFailedDiagnostic;
        }

        if (!JsonElementCanonicalizer.TryParseObject(addedBlob.Bytes, out JsonDocument? addedDoc))
        {
            return MalformedDiagnostic;
        }

        addedDoc.Dispose();
        collector.ReviewRequired(
            ContractChangeKinds.ContractFileAdded,
            path,
            $"Added configuration JSON Schema '{path}'; review operator configuration requirements.");
        return null;
    }

    private static string? DiffModifiedSchemaFile(
        IGitObjectReader reader,
        string baseOid,
        string headOid,
        string path,
        ContractChangeCollector collector)
    {
        GitBlobReadResult baseBlob = reader.ReadBlob(baseOid, path);
        GitBlobReadResult headBlob = reader.ReadBlob(headOid, path);
        if (baseBlob.Diagnostic is not null || headBlob.Diagnostic is not null ||
            !baseBlob.Found || !headBlob.Found || baseBlob.Bytes is null || headBlob.Bytes is null)
        {
            return baseBlob.Diagnostic ?? headBlob.Diagnostic ?? ReadFailedDiagnostic;
        }

        if (!JsonElementCanonicalizer.TryParseObject(baseBlob.Bytes, out JsonDocument? baseDoc))
        {
            return MalformedDiagnostic;
        }

        using (baseDoc)
        {
            if (!JsonElementCanonicalizer.TryParseObject(headBlob.Bytes, out JsonDocument? headDoc))
            {
                return MalformedDiagnostic;
            }

            using (headDoc)
            {
                if (!baseBlob.Bytes.AsSpan().SequenceEqual(headBlob.Bytes))
                {
                    ConservativeJsonSchemaDiffer.DiffSchema(collector, "$", baseDoc.RootElement, headDoc.RootElement, baseDoc.RootElement, headDoc.RootElement);
                    DiffDefs(baseDoc.RootElement, headDoc.RootElement, collector);
                }
            }
        }

        return null;
    }

    private static void DiffDefs(JsonElement baseRoot, JsonElement headRoot, ContractChangeCollector collector)
    {
        Dictionary<string, JsonElement> baseDefs = JsonElementCanonicalizer.ReadObjectMap(baseRoot, DefsKeyword);
        Dictionary<string, JsonElement> headDefs = JsonElementCanonicalizer.ReadObjectMap(headRoot, DefsKeyword);

        foreach (string name in baseDefs.Keys.Union(headDefs.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            bool hasBase = baseDefs.TryGetValue(name, out JsonElement baseDef);
            bool hasHead = headDefs.TryGetValue(name, out JsonElement headDef);
            string target = $"{DefsKeyword}.{name}";

            if (hasBase && !hasHead)
            {
                collector.Breaking(ContractChangeKinds.SchemaRemoved, target, $"Removed configuration definition '{target}'.");
            }
            else if (!hasBase && hasHead)
            {
                collector.Additive(ContractChangeKinds.SchemaAdded, target, $"Added configuration definition '{target}'.");
            }
            else
            {
                ConservativeJsonSchemaDiffer.DiffSchema(collector, target, baseRoot, headRoot, baseDef, headDef);
            }
        }
    }

    private static bool TryParseEnvBlob(GitBlobReadResult blob, out Dictionary<string, EnvEntry> entries)
    {
        if (!blob.Found || blob.Bytes is null)
        {
            entries = new Dictionary<string, EnvEntry>(StringComparer.Ordinal);
            return true;
        }

        try
        {
            entries = ParseEnvEntries(StrictUtf8.GetString(blob.Bytes));
            return true;
        }
        catch (DecoderFallbackException)
        {
            entries = new Dictionary<string, EnvEntry>(StringComparer.Ordinal);
            return false;
        }
    }

    private static bool AreIdenticalBlobs(GitBlobReadResult baseBlob, GitBlobReadResult headBlob) =>
        baseBlob.Found && headBlob.Found &&
        baseBlob.Bytes is not null && headBlob.Bytes is not null &&
        baseBlob.Bytes.AsSpan().SequenceEqual(headBlob.Bytes);

    private static Dictionary<string, EnvEntry> ParseEnvEntries(string content)
    {
        var entries = new Dictionary<string, EnvEntry>(StringComparer.Ordinal);
        foreach (string line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(raw => raw.Trim())
            .Where(trimmed => trimmed.Length > 0))
        {
            ParseEnvLine(line, entries);
        }

        return entries;
    }

    private static void ParseEnvLine(string line, Dictionary<string, EnvEntry> entries)
    {
        Match active = ActiveEnvPattern.Match(line);
        if (active.Success)
        {
            string name = active.Groups["name"].Value;
            string value = active.Groups["value"].Value.Trim();
            entries[name] = new EnvEntry(IsCommentedOptional: false, value.Length > 0, HashValue(value));
            return;
        }

        Match commented = CommentedEnvPattern.Match(line);
        if (!commented.Success)
        {
            return;
        }

        string commentedName = commented.Groups["name"].Value;
        if (!LooksLikeEnvVariableName(commentedName))
        {
            return;
        }

        string commentedValue = commented.Groups["value"].Value.Trim();
        if (!entries.TryGetValue(commentedName, out EnvEntry? existing) || existing.IsCommentedOptional)
        {
            entries[commentedName] = new EnvEntry(IsCommentedOptional: true, commentedValue.Length > 0, HashValue(commentedValue));
        }
    }

    private static bool LooksLikeEnvVariableName(string name) =>
        name.Contains('_', StringComparison.Ordinal) ||
        (name.Length >= 2 && name.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)));

    private static string HashValue(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(value)));

    private static bool IsConfigurationSchemaPath(string path) =>
        path.StartsWith($"{SchemasDirectory}/", StringComparison.Ordinal) &&
        path.EndsWith(".schema.json", StringComparison.Ordinal) &&
        path.Contains("configuration", StringComparison.OrdinalIgnoreCase);

    private sealed record EnvEntry(bool IsCommentedOptional, bool HasNonEmptyDefault, string ValueDigest);
}
