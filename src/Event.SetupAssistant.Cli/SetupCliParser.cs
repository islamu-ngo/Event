namespace ISLAMU.Event.SetupAssistant.Cli;

internal static class SetupCliParser
{
    internal static readonly Dictionary<string, string[]> Operations = new(StringComparer.Ordinal)
    {
        ["catalogue"] = ["list", "show", "describe"],
        ["manifest"] = ["create", "open", "validate", "format", "diff", "coverage", "export"],
        ["tenant-package"] = ["create", "open", "validate", "format", "diff", "coverage", "export"],
        ["env"] = ["render", "validate"], ["legal"] = ["validate", "preview"],
        ["doctor"] = ["doctor"]
    };
    private static readonly HashSet<string> InputOperations = new(["open", "validate", "format", "diff", "coverage", "export", "preview"], StringComparer.Ordinal);
    private static readonly HashSet<string> OutputOperations = new(["create", "format", "export", "render", "list", "show", "describe"], StringComparer.Ordinal);
    private static readonly string[] Forbidden = ["secret", "password", "token", "credential", "private-key", "api-key", "connection-string"];

    internal static SetupCliCommand Parse(SetupCliInvocation invocation)
    {
        string[] args = invocation.Arguments.ToArray();
        bool machine = invocation.Mode == SetupCliMode.Machine || args.Contains("--machine", StringComparer.Ordinal);
        if (args.Length == 1 && args[0] == "--help") return Empty("doctor", "doctor", machine) with { Help = true };
        if (args.Length == 0) return Empty("doctor", "doctor", machine) with { Error = "command-missing" };
        string family = args[0];
        if (!Operations.TryGetValue(family, out string[]? allowed)) return Empty(family, string.Empty, machine) with { Error = "command-unknown" };
        bool self = family == "doctor";
        string operation = self ? family : args.Length > 1 ? args[1] : string.Empty;
        if (!allowed.Contains(operation, StringComparer.Ordinal)) return Empty(family, operation, machine) with { Error = "operation-unknown" };

        int index = self ? 1 : 2;
        bool text = false, dryRun = false, help = false;
        string? input = null, baseline = null, output = null, key = null, topology = null, error = null;
        var capabilities = new List<string>();
        var providers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (index < args.Length)
        {
            string option = args[index++];
            if (IsForbidden(option)) { error = "secret-surface"; continue; }
            bool repeatable = option is "--capability" or "--provider";
            if (!repeatable && !seen.Add(option)) { error = "option-duplicate"; continue; }
            switch (option)
            {
                case "--machine": machine = true; break;
                case "--text": text = true; break;
                case "--dry-run": dryRun = true; break;
                case "--help": help = true; break;
                case "--input": input = Value("path"); break;
                case "--baseline": baseline = Value("path"); break;
                case "--output": output = Value("path"); break;
                case "--key": key = Value("key"); break;
                case "--topology": topology = Value("identifier"); break;
                case "--capability": Add(capabilities, Value("identifier")); break;
                case "--provider": Add(providers, Value("identifier")); break;
                default: error = option.Length > 0 && option[0] == '-' ? "option-unknown" : "argument-tail"; break;
            }
        }

        if (machine && text) error = "mode-conflict";
        if (input is not null && !InputOperations.Contains(operation)) error = "input-not-supported";
        if (baseline is not null && operation != "diff") error = "baseline-not-supported";
        if (operation == "diff" && baseline is null) error = "baseline-required";
        if (key is not null && operation is not ("show" or "describe")) error = "key-not-supported";
        if (operation is "show" or "describe" && key is null) error = "key-required";
        bool environmentOptions = topology is not null || capabilities.Count > 0 || providers.Count > 0;
        if (environmentOptions && (family != "env" || operation != "render")) error = "activation-option-not-supported";
        if (output is not null && !OutputOperations.Contains(operation)) error = "output-not-supported";
        if (dryRun && !OutputOperations.Contains(operation)) error = "dry-run-not-supported";
        if (machine && output == "-") error = "machine-artifact-stdout";
        return new(family, operation, machine, dryRun, help, input, baseline, output, key, topology,
            capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            providers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), error);

        string? Value(string kind)
        {
            if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) { error = "option-value-missing"; return null; }
            string value = args[index++];
            if (IsForbidden(value) || ContainsControl(value)) { error = kind == "path" ? "path-rejected" : "option-value-rejected"; return null; }
            if (kind == "identifier" && !IsIdentifier(value)) { error = "identifier-invalid"; return null; }
            if (kind == "key" && !IsCatalogueKey(value)) { error = "catalogue-key-invalid"; return null; }
            return value;
        }
        static void Add(List<string> values, string? value) { if (value is not null) values.Add(value); }
    }

    internal static bool IsForbidden(string value) => Forbidden.Any(term =>
        value.Replace('_', '-').Contains(term, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsControl(string value) => value.Any(character => character < ' ' || character == 0x7f);
    private static bool IsIdentifier(string value) => value.Length is > 0 and <= 128 && value[0] is >= 'a' and <= 'z'
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    private static bool IsCatalogueKey(string value) => value.Length is > 0 and <= 128 && value[0] is >= 'A' and <= 'Z'
        && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    private static SetupCliCommand Empty(string family, string operation, bool machine) =>
        new(family, operation, machine, false, false, null, null, null, null, null, [], [], null);
}
