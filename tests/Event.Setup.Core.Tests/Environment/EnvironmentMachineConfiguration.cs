namespace ISLAMU.Setup.Core.EnvironmentTests;

using System.Text.Json;
using System.Text.RegularExpressions;

internal static partial class EnvironmentMachineConfiguration
{
    internal static string RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, ".env.example"))
                && File.Exists(Path.Combine(candidate.FullName, "docker-compose.yml")))
                return candidate.FullName;
            candidate = candidate.Parent;
        }

        throw new InvalidOperationException("Repository machine-configuration root was not found.");
    }

    internal static MachineEnvironmentFile ParseEnvironmentTemplate(string text)
    {
        var entries = new List<MachineEnvironmentEntry>();
        foreach (string line in text.Split('\n'))
        {
            string normalized = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.TrimStart().StartsWith('#'))
                continue;
            Match match = EnvironmentAssignment().Match(normalized);
            if (!match.Success)
                throw new InvalidDataException("Environment template contains an unparseable machine assignment.");
            entries.Add(new(match.Groups[1].Value, match.Groups[2].Length == 0));
        }

        return new(entries.AsReadOnly());
    }

    internal static MachineComposeFile ParseCompose(string text)
    {
        var ordered = new List<string>();
        var required = new HashSet<string>(StringComparer.Ordinal);
        for (int start = 0; start < text.Length - 2; start++)
        {
            if (text[start] != '$' || text[start + 1] != '{') continue;
            int keyStart = start + 2;
            int cursor = keyStart;
            while (cursor < text.Length && (text[cursor] is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) cursor++;
            if (cursor == keyStart) continue;
            string key = text[keyStart..cursor];
            if (!ordered.Contains(key, StringComparer.Ordinal)) ordered.Add(key);
            if ((cursor < text.Length && text[cursor] == '?')
                || (cursor + 1 < text.Length && text[cursor] == ':' && text[cursor + 1] == '?'))
                required.Add(key);
        }

        return new(ordered.AsReadOnly(), required);
    }

    internal static MachineCatalogue ParseMachineCatalogue(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Environment machine catalogue schema is unsupported.");

        var definitions = new List<MachineCatalogueDefinition>();
        foreach (JsonElement item in root.GetProperty("definitions").EnumerateArray())
        {
            definitions.Add(new(
                item.GetProperty("key").GetString()!,
                item.GetProperty("category").GetString()!,
                item.GetProperty("sensitivity").GetString()!,
                item.GetProperty("requirement").GetString()!,
                item.GetProperty("order").GetInt32(),
                item.TryGetProperty("hasSafeDefault", out JsonElement hasDefault)
                    && hasDefault.GetBoolean(),
                item.GetProperty("validatorId").GetString()!,
                item.GetProperty("restartBehavior").GetString()!,
                item.GetProperty("documentation").GetProperty("anchor").GetString()!,
                item.GetProperty("generation").GetProperty("surfaces").GetInt32(),
                ParseActivation(item.GetProperty("activation"))));
        }

        var secretBindings = root.GetProperty("secretBindingEnvironmentKeys")
            .EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        string[] dotenvKeys = root.GetProperty("dotenvEnvironmentKeys")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        string[] composeKeys = root.GetProperty("composeEnvironmentKeys")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        var composeRequired = root.GetProperty("composeRequiredEnvironmentKeys")
            .EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        string[] startupKeys = root.GetProperty("startupEnvironmentKeys")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        return new(definitions.AsReadOnly(), secretBindings, dotenvKeys, composeKeys, composeRequired, startupKeys);
    }

    private static MachineActivationNode ParseActivation(JsonElement element) => new(
        element.GetProperty("kind").GetString()!,
        element.TryGetProperty("identifier", out JsonElement identifier)
            ? identifier.GetString()
            : null,
        element.GetProperty("operands").EnumerateArray().Select(ParseActivation).ToArray());

    [GeneratedRegex("^([A-Za-z_][A-Za-z0-9_]*)=(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentAssignment();

}

internal sealed record MachineEnvironmentEntry(string Key, bool IsEmptyPlaceholder);
internal sealed record MachineEnvironmentFile(IReadOnlyList<MachineEnvironmentEntry> Entries);
internal sealed record MachineComposeFile(IReadOnlyList<string> Keys, IReadOnlySet<string> RequiredKeys);
internal sealed record MachineCatalogueDefinition(
    string Key,
    string Category,
    string Sensitivity,
    string Requirement,
    int Order,
    bool HasSafeDefault,
    string ValidatorId,
    string RestartBehavior,
    string DocumentationAnchor,
    int GenerationSurfaces,
    MachineActivationNode Activation);
internal sealed record MachineActivationNode(
    string Kind,
    string? Identifier,
    IReadOnlyList<MachineActivationNode> Operands);
internal sealed record MachineCatalogue(
    IReadOnlyList<MachineCatalogueDefinition> Definitions,
    IReadOnlySet<string> SecretBindingEnvironmentKeys,
    IReadOnlyList<string> DotenvEnvironmentKeys,
    IReadOnlyList<string> ComposeEnvironmentKeys,
    IReadOnlySet<string> ComposeRequiredEnvironmentKeys,
    IReadOnlyList<string> StartupEnvironmentKeys);
