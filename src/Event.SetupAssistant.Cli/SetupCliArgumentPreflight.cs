namespace ISLAMU.Event.SetupAssistant.Cli;

internal sealed record SetupCliPreflight(
    SetupCliCommand Command,
    string[] Arguments,
    bool RunSpectre,
    string? Error);

internal static class SetupCliArgumentPreflight
{
    internal const string StandardIoSentinel = "__EVENT_SETUP_STANDARD_IO__";

    private static readonly string[] Forbidden =
        ["secret", "password", "token", "credential", "private-key", "api-key", "connection-string"];

    internal static SetupCliPreflight Inspect(SetupCliInvocation invocation)
    {
        string[] args = invocation.Arguments.ToArray();
        string[] spectreArguments = (string[])args.Clone();
        bool invocationMachine = invocation.Mode == SetupCliMode.Machine;
        if (args.Length == 0 || args is ["--help"])
            return Help("doctor", "doctor", invocationMachine, ["--help"]);

        if (args.Length > 128 || args.Any(argument => argument.Length > 4096))
            return Failure(invocationMachine, "argument-shape-invalid");

        string family = args[0];
        SetupCliFamilyDescriptor? familyDescriptor = SetupCliCommandRegistry.Families
            .FirstOrDefault(item => item.Name == family);
        if (familyDescriptor is null || IsHostile(family))
            return Failure(invocationMachine || args.Contains("--machine", StringComparer.Ordinal), "command-unknown");

        bool doctor = family == "doctor";
        int optionStart = doctor ? 1 : 2;
        string operation = doctor ? "doctor" : args.Length > 1 ? args[1] : string.Empty;
        bool machine = invocationMachine || args.Contains("--machine", StringComparer.Ordinal);
        if (!doctor && operation == "--help"
            && args.Skip(2).All(argument => argument is "--machine" or "--text"))
        {
            if (machine && args.Contains("--text", StringComparer.Ordinal))
                return Failure(machine, "mode-conflict", family, familyDescriptor.Operations[0].Name);
            return Help(family, familyDescriptor.Operations[0].Name, machine, args);
        }
        if (!SetupCliCommandRegistry.TryResolve(family, operation, out SetupCliOperationDescriptor? descriptor))
            return Failure(machine, "operation-unknown", family, familyDescriptor.Operations[0].Name);

        var options = descriptor!.Options.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool help = false;
        string? error = null;
        for (int index = optionStart; index < args.Length; index++)
        {
            string token = args[index];
            if (IsHostile(token) || HasControl(token))
            {
                error = "secret-surface";
                break;
            }
            if (token == "--help")
            {
                help = true;
                continue;
            }
            if (!options.TryGetValue(token, out SetupCliOptionDescriptor? option))
            {
                error = token.StartsWith('-') ? "option-unknown" : "argument-tail";
                break;
            }
            if (!option.Repeatable && !seen.Add(token))
            {
                error = "option-duplicate";
                break;
            }
            if (!option.RequiresValue)
            {
                values[token] = [];
                continue;
            }
            if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            {
                error = "option-value-missing";
                break;
            }
            string value = args[index];
            if (value.Length > 4096 || IsHostile(value) || HasControl(value))
            {
                error = token is "--input" or "--baseline" or "--output" ? "path-rejected" : "option-value-rejected";
                break;
            }
            if (!values.TryGetValue(token, out List<string>? list))
                values[token] = list = [];
            list.Add(value);
            if (value == "-" && token is "--input" or "--baseline" or "--output")
                spectreArguments[index] = StandardIoSentinel;
        }

        bool dryRun = values.ContainsKey("--dry-run");
        machine |= values.ContainsKey("--machine");
        bool text = values.ContainsKey("--text");
        error ??= machine && text ? "mode-conflict" : null;
        error ??= Required("--input", "input-required");
        error ??= Required("--baseline", "baseline-required");
        error ??= Required("--key", "key-required");
        error ??= Required("--expected-revision", "revision-required");
        if (!help && options.ContainsKey("--output") && !values.ContainsKey("--output") && !dryRun)
            error ??= "output-required";
        if (machine && First("--output") == "-")
            error ??= "machine-artifact-stdout";
        if (First("--key") is { } key && !IsCatalogueKey(key))
            error ??= "catalogue-key-invalid";
        foreach (string name in new[] { "--topology", "--capability", "--provider" })
        {
            if (values.TryGetValue(name, out List<string>? identifiers) && identifiers.Any(value => !IsIdentifier(value)))
                error ??= "identifier-invalid";
        }
        if (First("--format") is { } format && format is not ("json" or "yaml"))
            error ??= "format-not-supported";
        if (First("--expected-revision") is { } revision && !IsRevision(revision))
            error ??= "revision-required";

        var command = new SetupCliCommand(
            family,
            operation,
            machine,
            dryRun,
            help,
            First("--input"),
            First("--baseline"),
            First("--output"),
            First("--key"),
            First("--topology"),
            All("--capability"),
            All("--provider"),
            error)
        {
            Format = First("--format") ?? "json",
            ExpectedRevision = First("--expected-revision")
        };
        return new(command, spectreArguments, error is null && (!help || !machine), error);

        string? Required(string option, string code) =>
            !help && options.ContainsKey(option) && !values.ContainsKey(option) ? code : null;
        string? First(string option) =>
            values.TryGetValue(option, out List<string>? found) && found.Count > 0 ? found[0] : null;
        string[] All(string option) =>
            values.TryGetValue(option, out List<string>? found)
                ? found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                : [];
    }

    internal static bool IsForbiddenName(string value) => IsHostile(value);

    private static SetupCliPreflight Help(string family, string operation, bool machine, string[] arguments) =>
        new(
            new SetupCliCommand(family, operation, machine, false, true, null, null, null, null, null, [], [], null),
            arguments,
            !machine,
            null);

    private static SetupCliPreflight Failure(
        bool machine,
        string error,
        string family = "doctor",
        string operation = "doctor") =>
        new(
            new SetupCliCommand(family, operation, machine, false, false, null, null, null, null, null, [], [], error),
            [],
            false,
            error);

    private static bool IsHostile(string value)
    {
        string normalized = value.Replace('_', '-');
        return Forbidden.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasControl(string value) =>
        value.Any(character => character < ' ' || character == 0x7f);

    private static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 128
        && value[0] is >= 'a' and <= 'z'
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool IsCatalogueKey(string value) =>
        value.Length is > 0 and <= 128
        && value[0] is >= 'A' and <= 'Z'
        && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    private static bool IsRevision(string value) =>
        value == "absent"
        || value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
