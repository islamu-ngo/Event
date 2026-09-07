using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ISLAMU.Event.SetupAssistant.Cli;

public enum SetupCliMode { Text, Machine }
public enum SetupCliExitCode { Success = 0, Validation = 2, Incomplete = 3, Blocked = 4, Usage = 64, Data = 65, Internal = 70, Io = 74 }

public interface ISetupCliInput
{
    ReadOnlyMemory<byte> Read(string path, int maximumBytes);
}

public interface ISetupCliWriter
{
    void Write(string path, ReadOnlyMemory<byte> bytes, int maximumBytes);
}

public sealed record SetupCliIo
{
    public SetupCliIo(ISetupCliInput input, ISetupCliWriter output, ISetupCliWriter error,
        int maximumCharacters, int maximumBytes)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Error = error ?? throw new ArgumentNullException(nameof(error));
        if (maximumCharacters is < 1 or > 65_536 || maximumBytes is < 1 or > 4 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        MaximumCharacters = maximumCharacters;
        MaximumBytes = maximumBytes;
    }
    public ISetupCliInput Input { get; }
    public ISetupCliWriter Output { get; }
    public ISetupCliWriter Error { get; }
    public int MaximumCharacters { get; }
    public int MaximumBytes { get; }
    public override string ToString() => $"{nameof(SetupCliIo)}:Characters={MaximumCharacters}:Bytes={MaximumBytes}";
}

public sealed record SetupCliEnvironmentPresence
{
    private readonly string[] _names;
    public SetupCliEnvironmentPresence(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        _names = names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (_names.Any(name => name is null || name.Length is < 1 or > 128))
            throw new ArgumentException("environment-name-invalid", nameof(names));
    }
    public IReadOnlyList<string> Names => Array.AsReadOnly((string[])_names.Clone());
    public override string ToString() => $"{nameof(SetupCliEnvironmentPresence)}:Count={_names.Length}";
}

public sealed record SetupCliInvocation
{
    private readonly string[] _arguments;
    public SetupCliInvocation(IEnumerable<string> arguments, SetupCliMode mode, SetupCliIo io,
        SetupCliEnvironmentPresence environment)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        _arguments = arguments.ToArray();
        if (_arguments.Any(argument => argument is null || argument.Length > 4096) || _arguments.Length > 128)
            throw new ArgumentException("argument-bound-exceeded", nameof(arguments));
        Mode = mode;
        Io = io ?? throw new ArgumentNullException(nameof(io));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }
    public IReadOnlyList<string> Arguments => Array.AsReadOnly((string[])_arguments.Clone());
    public SetupCliMode Mode { get; }
    public SetupCliIo Io { get; }
    public SetupCliEnvironmentPresence Environment { get; }
    public override string ToString() => $"{nameof(SetupCliInvocation)}:Mode={Mode}:Count={_arguments.Length}";
}

public sealed record SetupCliMachineInvocation(string CommandFamily, string Operation, string Mode);
public sealed record SetupCliMachineDiagnostic(string Code, string Path, string Severity);
public sealed record SetupCliMachineCoverage
{
    private readonly string[] _coveredKeys;
    private readonly string[] _missingKeys;
    public SetupCliMachineCoverage(IEnumerable<string> coveredKeys, IEnumerable<string> missingKeys)
    {
        _coveredKeys = Snapshot(coveredKeys);
        _missingKeys = Snapshot(missingKeys);
    }
    public IReadOnlyList<string> CoveredKeys => Array.AsReadOnly((string[])_coveredKeys.Clone());
    public IReadOnlyList<string> MissingKeys => Array.AsReadOnly((string[])_missingKeys.Clone());
    private static string[] Snapshot(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
public sealed record SetupCliMachineReadiness
{
    private readonly string[] _missingKeys;
    private readonly string[] _blockedKeys;
    public SetupCliMachineReadiness(string state, IEnumerable<string> missingKeys, IEnumerable<string> blockedKeys)
    {
        State = state;
        _missingKeys = Snapshot(missingKeys);
        _blockedKeys = Snapshot(blockedKeys);
    }
    public string State { get; }
    public IReadOnlyList<string> MissingKeys => Array.AsReadOnly((string[])_missingKeys.Clone());
    public IReadOnlyList<string> BlockedKeys => Array.AsReadOnly((string[])_blockedKeys.Clone());
    private static string[] Snapshot(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
public sealed record SetupCliMachineArtifact(string Kind, string MediaType, string Digest, string Sensitivity,
    SetupCliMachineCoverage Coverage, SetupCliMachineReadiness Readiness, string PathIntent, string WriteStatus);
public sealed record SetupCliMachineEnvelope
{
    private readonly SetupCliMachineDiagnostic[] _diagnostics;
    private readonly SetupCliMachineArtifact[] _artifacts;
    public SetupCliMachineEnvelope(string schemaVersion, SetupCliMachineInvocation invocation,
        string status, string exitCategory, int exitCode, bool dryRun,
        IEnumerable<SetupCliMachineDiagnostic> diagnostics, IEnumerable<SetupCliMachineArtifact> artifacts,
        SetupCliMachineCoverage coverage, SetupCliMachineReadiness readiness)
    {
        SchemaVersion = schemaVersion;
        Invocation = invocation;
        Status = status;
        ExitCategory = exitCategory;
        ExitCode = exitCode;
        DryRun = dryRun;
        _diagnostics = diagnostics.ToArray();
        _artifacts = artifacts.ToArray();
        Coverage = coverage;
        Readiness = readiness;
    }
    public string SchemaVersion { get; }
    public SetupCliMachineInvocation Invocation { get; }
    public string Status { get; }
    public string ExitCategory { get; }
    public int ExitCode { get; }
    public bool DryRun { get; }
    public IReadOnlyList<SetupCliMachineDiagnostic> Diagnostics => Array.AsReadOnly((SetupCliMachineDiagnostic[])_diagnostics.Clone());
    public IReadOnlyList<SetupCliMachineArtifact> Artifacts => Array.AsReadOnly((SetupCliMachineArtifact[])_artifacts.Clone());
    public SetupCliMachineCoverage Coverage { get; }
    public SetupCliMachineReadiness Readiness { get; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never, WriteIndented = false)]
[JsonSerializable(typeof(SetupCliMachineEnvelope))]
public partial class SetupCliJsonContext : JsonSerializerContext;

public static class SetupCliExecutableMarker
{
    public const string ExecutableName = "event-setup";
}
