using System.Collections.ObjectModel;
using System.Text;

namespace ISLAMU.SetupAssistant.Cli.Tests;

internal sealed record CliVector(
    IReadOnlyList<string> Arguments,
    string CapturedInput,
    IReadOnlyCollection<string> EnvironmentNames);

internal static class SetupCliContractSpecification
{
    internal static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Operations =
        new ReadOnlyDictionary<string, IReadOnlySet<string>>(
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["catalogue"] = Set("list", "show", "describe"),
                ["manifest"] = Set("create", "open", "validate", "format", "diff", "coverage", "export"),
                ["tenant-package"] = Set("create", "open", "validate", "format", "diff", "coverage", "export"),
                ["env"] = Set("render", "validate"),
                ["legal"] = Set("validate", "preview"),
                ["doctor"] = Set("doctor")
            });

    internal static readonly IReadOnlyDictionary<string, int> ExitCodes =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["success"] = 0,
            ["validation"] = 2,
            ["incomplete"] = 3,
            ["blocked"] = 4,
            ["usage"] = 64,
            ["data"] = 65,
            ["internal"] = 70,
            ["io"] = 74
        });

    private static readonly HashSet<string> Options = Set("--help", "--machine", "--text", "--dry-run", "--input", "--baseline", "--output", "--key", "--topology", "--capability", "--provider");
    private static readonly HashSet<string> InputOperations = Set("open", "validate", "format", "diff", "coverage", "export", "preview");
    private static readonly HashSet<string> OutputOperations = Set("create", "format", "export", "render", "list", "show", "describe");
    private static readonly string[] ForbiddenNames = ["secret", "password", "token", "credential", "private-key", "api-key", "connection-string"];

    internal static IReadOnlyList<string> Validate(CliVector vector)
    {
        var errors = new List<string>();
        IReadOnlyList<string> args = vector.Arguments;
        if (args.Count == 0)
        {
            return ["usage-command-missing"];
        }

        string family = args[0];
        if (!Operations.TryGetValue(family, out IReadOnlySet<string>? operations))
        {
            errors.Add("usage-command-unknown");
        }

        bool selfOperation = family == "doctor";
        int optionStart = selfOperation ? 1 : 2;
        string operation = selfOperation ? family : args.Count > 1 ? args[1] : string.Empty;
        if (operation.Length == 0 || operations is null || !operations.Contains(operation))
        {
            errors.Add("usage-operation-unknown");
        }

        bool machine = false;
        bool text = false;
        bool dryRun = false;
        bool hasInput = false;
        bool hasOutput = false;
        bool hasBaseline = false;
        bool hasKey = false;
        string? inputPath = null;
        string? outputPath = null;
        for (int index = optionStart; index < args.Count; index++)
        {
            string token = args[index];
            if (LooksForbidden(token))
            {
                errors.Add("usage-secret-surface");
                continue;
            }

            if (!Options.Contains(token))
            {
                errors.Add(token.StartsWith('-') ? "usage-option-unknown" : "usage-argument-tail");
                continue;
            }

            machine |= token == "--machine";
            text |= token == "--text";
            dryRun |= token == "--dry-run";
            if (token is "--input" or "--baseline" or "--output" or "--key" or "--topology" or "--capability" or "--provider")
            {
                if (++index >= args.Count || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    errors.Add("usage-option-value-missing");
                    continue;
                }

                string value = args[index];
                if (LooksForbidden(value)) errors.Add("usage-secret-surface");
                if (token == "--input") { hasInput = true; inputPath = value; }
                else if (token == "--baseline") hasBaseline = true;
                else if (token == "--output") { hasOutput = true; outputPath = value; }
                else if (token == "--key") hasKey = true;
            }
        }

        if (machine && text)
        {
            errors.Add("usage-mode-conflict");
        }
        if (hasInput && !InputOperations.Contains(operation))
        {
            errors.Add("usage-input-not-supported");
        }
        if (hasOutput && !OutputOperations.Contains(operation)) errors.Add("usage-output-not-supported");
        if (dryRun && !OutputOperations.Contains(operation)) errors.Add("usage-dry-run-not-supported");
        if (hasBaseline != (operation == "diff")) errors.Add(hasBaseline ? "usage-baseline-not-supported" : "usage-baseline-required");
        if (hasKey != (operation is "show" or "describe")) errors.Add(hasKey ? "usage-key-not-supported" : "usage-key-required");
        if ((family == "catalogue" && OutputOperations.Contains(operation)) && !hasOutput && !dryRun)
            errors.Add("usage-output-required");
        if (machine && outputPath == "-")
        {
            errors.Add("usage-machine-artifact-stdout");
        }
        if (vector.CapturedInput.Length > 0 && (inputPath != "-" || !InputOperations.Contains(operation)))
        {
            errors.Add("usage-stdin-not-explicit");
        }
        if (Encoding.UTF8.GetByteCount(vector.CapturedInput) > 4 * 1024 * 1024 || ContainsUnsafeText(vector.CapturedInput) || LooksForbidden(vector.CapturedInput))
        {
            errors.Add("data-stdin-rejected");
        }
        if (vector.EnvironmentNames.Any(LooksForbidden))
        {
            errors.Add("blocked-environment-name");
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static bool ProjectionIsSafe(string output) =>
        output.Length <= 65_536 && !ContainsUnsafeText(output) &&
        !ForbiddenNames.Any(name => output.Contains(name, StringComparison.OrdinalIgnoreCase)) &&
        !output.Contains('@') &&
        !output.Contains("://", StringComparison.Ordinal) &&
        !output.Contains("Server=", StringComparison.OrdinalIgnoreCase) &&
        !output.Contains("terminal-title", StringComparison.OrdinalIgnoreCase);

    private static bool LooksForbidden(string text)
    {
        string normalized = text.Replace('_', '-');
        return ForbiddenNames.Any(name => normalized.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsUnsafeText(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value is 0x1B or 0x7F || (rune.Value < 0x20 && rune.Value is not 0x0A and not 0x09))
            {
                return true;
            }
        }
        return false;
    }

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.Ordinal);
}
