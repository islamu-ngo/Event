using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;

namespace ISLAMU.Event.SetupAssistant.Cli;

internal sealed record SetupCliCommand(
    string Family, string Operation, bool Machine, bool DryRun, bool Help,
    string? Input, string? Baseline, string? Output, string? Key, string? Topology,
    IReadOnlyList<string> Capabilities, IReadOnlyList<string> Providers, string? Error);

internal sealed record SetupCliCommandResult(
    SetupCliExitCode Exit, IReadOnlyList<SetupCliMachineDiagnostic> Diagnostics,
    IReadOnlyList<SetupCliMachineArtifact> Artifacts, SetupCliMachineCoverage Coverage,
    SetupCliMachineReadiness Readiness);

internal static class SetupCliResults
{
    internal static SetupCliCommandResult Success(IReadOnlyList<SetupCliMachineArtifact>? artifacts = null,
        SetupCliMachineCoverage? coverage = null, SetupCliMachineReadiness? readiness = null) =>
        new(SetupCliExitCode.Success, [], artifacts ?? [], coverage ?? EmptyCoverage(), readiness ?? Ready());

    internal static SetupCliCommandResult Failure(SetupCliExitCode exit, string code, string path = "$.arguments") =>
        new(exit, [new SetupCliMachineDiagnostic(code, path, "error")], [], EmptyCoverage(), FailureReadiness(exit));

    internal static SetupCliCommandResult CoreFailure(IEnumerable<SetupDiagnostic> diagnostics, SetupCliExitCode exit)
    {
        SetupDiagnostic[] snapshot = diagnostics.Take(128).ToArray();
        return new(exit, snapshot.Select(item => new SetupCliMachineDiagnostic(
                item.Code.Value, NormalizePath(item.Path.Value), Lower(item.Severity))).ToArray(), [], EmptyCoverage(),
            DiagnosticReadiness(snapshot.Select(item => item.Code.Value)));
    }

    internal static SetupCliCommandResult EnvironmentFailure(IEnumerable<EnvironmentDiagnostic> diagnostics, SetupCliExitCode exit)
    {
        EnvironmentDiagnostic[] snapshot = diagnostics.Take(128).ToArray();
        return new(exit, snapshot.Select(item => new SetupCliMachineDiagnostic(
                item.Code, NormalizePath(item.Path), "error")).ToArray(), [], EmptyCoverage(),
            DiagnosticReadiness(snapshot.Select(item => item.Code)));
    }

    internal static SetupCliMachineArtifact Artifact(string kind, string mediaType, ReadOnlySpan<byte> bytes,
        string sensitivity, string pathIntent, string writeStatus, SetupCliMachineCoverage? coverage = null,
        SetupCliMachineReadiness? readiness = null) =>
        new(kind, mediaType, ArtifactDigest.Compute(bytes).Value, sensitivity, coverage ?? EmptyCoverage(),
            readiness ?? Ready(), pathIntent, writeStatus);

    internal static SetupCliMachineCoverage Coverage(SetupCoverageResult value) => new(
        NormalizeKeys(value.Covered.Select(item => item.Value)), NormalizeKeys(value.Missing.Select(item => item.Value)));
    internal static SetupCliMachineCoverage EmptyCoverage() => new([], []);
    internal static SetupCliMachineReadiness Ready() => new("ready", [], []);
    internal static SetupCliMachineReadiness Readiness(SetupCliMachineCoverage coverage) =>
        coverage.MissingKeys.Count == 0 ? Ready() : new("incomplete", coverage.MissingKeys, []);
    internal static string PathIntent(string? path) => path == "-" ? "stdout" : path is null ? "none" : "output";
    internal static string Lower<T>(T value) => value!.ToString()!.ToLowerInvariant();
    internal static string[] NormalizeKeys(IEnumerable<string> keys) => keys.Select(key => key.ToLowerInvariant().Replace('_', '-'))
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static string NormalizePath(string? source)
    {
        if (string.IsNullOrEmpty(source) || source[0] != '$') return "$.input";
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        for (int index = 1; index < source.Length; index++)
        {
            char value = source[index];
            if (value == '.') Flush();
            else if (value == '[')
            {
                Flush();
                while (index < source.Length && source[index] != ']') index++;
                segments.Add("item");
            }
            else if (char.IsAsciiLetterOrDigit(value) || value == '-') current.Append(value);
            else if (value == '_') current.Append('-');
        }
        Flush();
        return segments.Count == 0 ? "$" : "$." + string.Join('.', segments);

        void Flush()
        {
            if (current.Length == 0) return;
            if (!char.IsAsciiLetter(current[0]) || !char.IsLower(current[0])) current.Insert(0, 'p');
            segments.Add(current.ToString());
            current.Clear();
        }
    }

    private static SetupCliMachineReadiness FailureReadiness(SetupCliExitCode exit) => exit switch
    {
        SetupCliExitCode.Blocked => new("blocked", [], ["command-boundary"]),
        SetupCliExitCode.Validation or SetupCliExitCode.Data or SetupCliExitCode.Incomplete =>
            new("incomplete", ["input-invalid"], []),
        _ => Ready()
    };

    private static SetupCliMachineReadiness DiagnosticReadiness(IEnumerable<string> codes)
    {
        string[] values = codes.ToArray();
        if (values.Any(code => code.Contains("blocked", StringComparison.Ordinal)))
            return new("blocked", [], ["input-blocked"]);
        return new("incomplete", [values.Any(code => code.Contains("missing", StringComparison.Ordinal)
            || code.Contains("required", StringComparison.Ordinal)) ? "input-missing" : "input-invalid"], []);
    }
}
