namespace ISLAMU.Event.Setup.Core.Environment;

public enum DotenvReadinessState
{
    Ready,
    Incomplete,
    Blocked,
}

public sealed class DotenvReadinessResult
{
    private readonly string[] _missing;
    private readonly string[] _blocked;
    private readonly EnvironmentDiagnostic[] _diagnostics;

    internal DotenvReadinessResult(
        DotenvReadinessState state,
        IEnumerable<string> missing,
        IEnumerable<string> blocked,
        IEnumerable<EnvironmentDiagnostic> diagnostics)
    {
        State = state;
        _missing = missing.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        _blocked = blocked.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        _diagnostics = diagnostics.ToArray();
    }

    public DotenvReadinessState State { get; }
    public IReadOnlyList<string> Missing => Array.AsReadOnly((string[])_missing.Clone());
    public IReadOnlyList<string> Blocked => Array.AsReadOnly((string[])_blocked.Clone());
    public IReadOnlyList<EnvironmentDiagnostic> Diagnostics =>
        Array.AsReadOnly((EnvironmentDiagnostic[])_diagnostics.Clone());
    public override string ToString() =>
        $"{nameof(DotenvReadinessResult)}:State={State}:Missing={_missing.Length}:Blocked={_blocked.Length}";
}

public static class DotenvReadiness
{
    public static DotenvReadinessResult Evaluate(
        EnvironmentCatalogue catalogue,
        EnvironmentActivationContext context,
        DotenvDocument document)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(context);
        return Evaluate(catalogue.Relevant(context), document);
    }

    public static DotenvReadinessResult Evaluate(
        IEnumerable<EnvironmentVariableDefinition> relevantDefinitions,
        DotenvDocument document)
    {
        ArgumentNullException.ThrowIfNull(relevantDefinitions);
        ArgumentNullException.ThrowIfNull(document);
        EnvironmentVariableDefinition[] relevant = relevantDefinitions.ToArray();
        var diagnostics = new List<EnvironmentDiagnostic>();
        var entries = new Dictionary<string, DotenvEntry?>(StringComparer.Ordinal);
        foreach (DotenvEntry? entry in document.Entries)
        {
            if (entry is null)
            {
                diagnostics.Add(new EnvironmentDiagnostic(
                    "dotenv-entry-state-invalid", "$.readiness", null, "dotenv-readiness"));
                continue;
            }
            if (!entries.TryAdd(entry.Key, entry)) entries[entry.Key] = null;
        }

        var missing = new List<string>();
        var blocked = new List<string>();
        foreach (EnvironmentVariableDefinition definition in relevant)
        {
            if (definition.Requirement != EnvironmentVariableRequirement.Required) continue;
            bool isProtected = definition.Sensitivity != EnvironmentVariableSensitivity.Public;
            bool present = entries.TryGetValue(definition.Key, out DotenvEntry? entry)
                && ValidRequiredState(entry, isProtected);
            if (present) continue;
            if (entry is not null && entry.Kind != DotenvEntryKind.EmptyPlaceholder)
                diagnostics.Add(new EnvironmentDiagnostic(
                    "dotenv-entry-state-invalid", "$.readiness", definition.Key, "dotenv-readiness"));
            if (isProtected) blocked.Add(definition.Key);
            else missing.Add(definition.Key);
        }
        var relevantKeys = relevant.Select(definition => definition.Key).ToHashSet(StringComparer.Ordinal);
        string[] roles = ["RUNTIME", "GLOBAL", "MIGRATOR"];
        for (var index = 0; index < roles.Length; index++)
        {
            string usernameKey = $"DATABASE_{roles[index]}_USERNAME";
            string passwordKey = $"DATABASE_{roles[index]}_PASSWORD";
            if (!relevantKeys.Contains(passwordKey)) continue;
            entries.TryGetValue(passwordKey, out var password);
            entries.TryGetValue(usernameKey, out var username);
            if (!string.IsNullOrWhiteSpace(password?.Value)
                && (string.Equals(password.Value.Trim(), "explore", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(password.Value, username?.Value, StringComparison.OrdinalIgnoreCase)))
            {
                blocked.Add(passwordKey);
                diagnostics.Add(new EnvironmentDiagnostic(
                    "database-default-credential-forbidden", "$.readiness", passwordKey, "dotenv-readiness"));
            }
            for (var other = index + 1; other < roles.Length; other++)
            {
                foreach (var field in new[] { "USERNAME", "PASSWORD" })
                {
                    string firstKey = $"DATABASE_{roles[index]}_{field}";
                    string secondKey = $"DATABASE_{roles[other]}_{field}";
                    if (!relevantKeys.Contains(secondKey)
                        || !entries.TryGetValue(firstKey, out var first)
                        || !entries.TryGetValue(secondKey, out var second)
                        || string.IsNullOrWhiteSpace(first?.Value) || string.IsNullOrWhiteSpace(second?.Value))
                        continue;
                    var comparison = field == "USERNAME" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    if (!string.Equals(first.Value, second.Value, comparison)) continue;
                    blocked.Add(firstKey);
                    blocked.Add(secondKey);
                    diagnostics.Add(new EnvironmentDiagnostic(
                        "database-role-credentials-not-distinct", "$.readiness", firstKey, "dotenv-readiness"));
                }
            }
        }
        DotenvReadinessState state = blocked.Count > 0
            ? DotenvReadinessState.Blocked
            : missing.Count > 0 ? DotenvReadinessState.Incomplete : DotenvReadinessState.Ready;
        return new DotenvReadinessResult(state, missing, blocked, diagnostics);
    }

    private static bool ValidRequiredState(DotenvEntry? entry, bool isProtected)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Value)) return false;
        if (!isProtected)
            return !entry.IsSecret
                && entry.Kind == DotenvEntryKind.LocalHumanValue
                && entry.Provenance == DotenvProvenance.UserInput;
        return entry.IsSecret && entry.Kind switch
        {
            DotenvEntryKind.LocalHumanValue => entry.Provenance == DotenvProvenance.UserInput,
            DotenvEntryKind.GeneratedValueReference => entry.Provenance == DotenvProvenance.Generated,
            _ => false,
        };
    }
}
