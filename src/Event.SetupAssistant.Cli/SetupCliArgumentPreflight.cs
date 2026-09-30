namespace ISLAMU.Event.SetupAssistant.Cli;

internal sealed record SetupCliPreflight(
    SetupCliCommand Command,
    string[] Arguments,
    bool RunSpectre,
    string? Error);

internal static class SetupCliArgumentPreflight
{
    internal const string StandardIoSentinel = "__EVENT_SETUP_STANDARD_IO__";

    private const string MachineOption = "--machine";
    private const string TextOption = "--text";
    private const string HelpOption = "--help";
    private const string InputOption = "--input";
    private const string BaselineOption = "--baseline";
    private const string OutputOption = "--output";
    private const string KeyOption = "--key";
    private const string ExpectedRevisionOption = "--expected-revision";
    private const string DoctorCommand = "doctor";

    private static readonly string[] Forbidden =
        ["secret", "password", "token", "credential", "private-key", "api-key", "connection-string"];

    internal static SetupCliPreflight Inspect(SetupCliInvocation invocation)
    {
        string[] args = invocation.Arguments.ToArray();
        string[] spectreArguments = (string[])args.Clone();
        bool invocationMachine = invocation.Mode == SetupCliMode.Machine;
        if (args.Length == 0 || args is [HelpOption])
            return Help(DoctorCommand, DoctorCommand, invocationMachine, [HelpOption]);

        if (args.Length > 128 || args.Any(argument => argument.Length > 4096))
            return Failure(invocationMachine, "argument-shape-invalid");

        string family = args[0];
        SetupCliFamilyDescriptor? familyDescriptor = SetupCliCommandRegistry.Families
            .FirstOrDefault(item => item.Name == family);
        if (familyDescriptor is null || IsHostile(family))
            return Failure(invocationMachine || args.Contains(MachineOption, StringComparer.Ordinal), "command-unknown");

        bool doctor = family == DoctorCommand;
        int optionStart = doctor ? 1 : 2;
        string operation = ResolveOperation(doctor, args);
        bool machine = invocationMachine || args.Contains(MachineOption, StringComparer.Ordinal);
        if (!doctor && operation == HelpOption
            && args.Skip(2).All(argument => argument is MachineOption or TextOption))
        {
            if (machine && args.Contains(TextOption, StringComparer.Ordinal))
                return Failure(machine, "mode-conflict", family, familyDescriptor.Operations[0].Name);
            return Help(family, familyDescriptor.Operations[0].Name, machine, args);
        }
        if (!SetupCliCommandRegistry.TryResolve(family, operation, out SetupCliOperationDescriptor? descriptor))
            return Failure(machine, "operation-unknown", family, familyDescriptor.Operations[0].Name);

        var options = descriptor!.Options.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        bool help = false;
        string? error = ParseTokens(args, optionStart, options, spectreArguments, values, ref help);
        error ??= ValidateConstraints(options, values, help, ref machine);

        bool dryRun = values.ContainsKey("--dry-run");
        var command = new SetupCliCommand(
            family,
            operation,
            machine,
            dryRun,
            help,
            GetFirst(values, InputOption),
            GetFirst(values, BaselineOption),
            GetFirst(values, OutputOption),
            GetFirst(values, KeyOption),
            GetFirst(values, "--topology"),
            GetAll(values, "--capability"),
            GetAll(values, "--provider"),
            error)
        {
            Format = GetFirst(values, "--format") ?? "json",
            ExpectedRevision = GetFirst(values, ExpectedRevisionOption)
        };
        return new(command, spectreArguments, error is null && (!help || !machine), error);
    }

    private static string ResolveOperation(bool doctor, string[] args)
    {
        if (doctor) return DoctorCommand;
        return args.Length > 1 ? args[1] : string.Empty;
    }

    private static string? ParseTokens(
        string[] args,
        int optionStart,
        IReadOnlyDictionary<string, SetupCliOptionDescriptor> options,
        string[] spectreArguments,
        Dictionary<string, List<string>> values,
        ref bool help)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int index = optionStart;
        while (index < args.Length)
        {
            string token = args[index];
            if (IsHostile(token) || HasControl(token))
                return "secret-surface";

            if (token == HelpOption)
            {
                help = true;
                index++;
                continue;
            }

            if (!options.TryGetValue(token, out SetupCliOptionDescriptor? option))
                return token.StartsWith('-') ? "option-unknown" : "argument-tail";

            if (!option.Repeatable && !seen.Add(token))
                return "option-duplicate";

            if (!option.RequiresValue)
            {
                values[token] = [];
                index++;
                continue;
            }

            index++;
            if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                return "option-value-missing";

            string value = args[index];
            if (value.Length > 4096 || IsHostile(value) || HasControl(value))
                return IsPathOption(token) ? "path-rejected" : "option-value-rejected";

            if (!values.TryGetValue(token, out List<string>? list))
                values[token] = list = [];
            list.Add(value);

            if (value == "-" && IsPathOption(token))
                spectreArguments[index] = StandardIoSentinel;

            index++;
        }

        return null;
    }

    private static bool IsPathOption(string token) =>
        token is InputOption or BaselineOption or OutputOption;

    private static string? ValidateConstraints(
        IReadOnlyDictionary<string, SetupCliOptionDescriptor> options,
        Dictionary<string, List<string>> values,
        bool help,
        ref bool machine)
    {
        bool dryRun = values.ContainsKey("--dry-run");
        machine |= values.ContainsKey(MachineOption);
        bool text = values.ContainsKey(TextOption);
        if (machine && text)
            return "mode-conflict";

        string? requiredError = CheckRequired(options, values, help, dryRun);
        if (requiredError is not null)
            return requiredError;

        if (machine && GetFirst(values, OutputOption) == "-")
            return "machine-artifact-stdout";

        if (GetFirst(values, KeyOption) is { } key && !IsCatalogueKey(key))
            return "catalogue-key-invalid";

        foreach (string name in new[] { "--topology", "--capability", "--provider" })
        {
            if (values.TryGetValue(name, out List<string>? identifiers) && identifiers.Any(value => !IsIdentifier(value)))
                return "identifier-invalid";
        }

        if (GetFirst(values, "--format") is { } format && format is not ("json" or "yaml"))
            return "format-not-supported";

        if (GetFirst(values, ExpectedRevisionOption) is { } revision && !IsRevision(revision))
            return "revision-required";

        return null;
    }

    private static string? CheckRequired(
        IReadOnlyDictionary<string, SetupCliOptionDescriptor> options,
        Dictionary<string, List<string>> values,
        bool help,
        bool dryRun)
    {
        if (help) return null;
        if (options.ContainsKey(InputOption) && !values.ContainsKey(InputOption))
            return "input-required";
        if (options.ContainsKey(BaselineOption) && !values.ContainsKey(BaselineOption))
            return "baseline-required";
        if (options.ContainsKey(KeyOption) && !values.ContainsKey(KeyOption))
            return "key-required";
        if (options.ContainsKey(ExpectedRevisionOption) && !values.ContainsKey(ExpectedRevisionOption))
            return "revision-required";
        if (options.ContainsKey(OutputOption) && !values.ContainsKey(OutputOption) && !dryRun)
            return "output-required";
        return null;
    }

    private static string? GetFirst(Dictionary<string, List<string>> values, string option) =>
        values.TryGetValue(option, out List<string>? found) && found.Count > 0 ? found[0] : null;

    private static string[] GetAll(Dictionary<string, List<string>> values, string option) =>
        values.TryGetValue(option, out List<string>? found)
            ? found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            : [];

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
