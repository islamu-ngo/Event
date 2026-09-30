using System.ComponentModel;
using Spectre.Console.Cli;

namespace ISLAMU.Event.SetupAssistant.Cli.Commands;

internal interface ISetupCliCommandSettings
{
    SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode);
}

internal abstract class SetupCliCommandSettings : CommandSettings, ISetupCliCommandSettings
{
    [CommandOption("--machine")]
    [Description("Emit one deterministic machine-readable JSON envelope.")]
    public bool Machine { get; set; }

    [CommandOption("--text")]
    [Description("Render human-readable output.")]
    public bool Text { get; set; }

    public abstract SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode);

    protected SetupCliCommand Create(
        string family,
        string operation,
        SetupCliMode invocationMode,
        bool dryRun = false,
        string? input = null,
        string? baseline = null,
        string? output = null,
        string? key = null,
        string? topology = null,
        IReadOnlyList<string>? capabilities = null,
        IReadOnlyList<string>? providers = null,
        string format = "json",
        string? expectedRevision = null) =>
        new(
            family,
            operation,
            Machine || invocationMode == SetupCliMode.Machine,
            dryRun,
            false,
            input,
            baseline,
            output,
            key,
            topology,
            Normalize(capabilities),
            Normalize(providers),
            null)
        {
            Format = format,
            ExpectedRevision = expectedRevision
        };

    protected static string? Path(string? value) =>
        value == SetupCliArgumentPreflight.StandardIoSentinel ? "-" : value;

    private static string[] Normalize(IReadOnlyList<string>? values) =>
        values is null
            ? []
            : values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}

internal sealed class InputSettings : SetupCliCommandSettings
{
    [CommandOption("--input <PATH>")]
    public string? Input { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, input: Path(Input));
}

internal class OutputSettings : SetupCliCommandSettings
{
    [CommandOption("--output <PATH>")]
    public string? Output { get; set; }

    [CommandOption("--dry-run")]
    public bool DryRun { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, DryRun, output: Path(Output));
}

internal class InputOutputSettings : SetupCliCommandSettings
{
    [CommandOption("--input <PATH>")]
    public string? Input { get; set; }

    [CommandOption("--output <PATH>")]
    public string? Output { get; set; }

    [CommandOption("--dry-run")]
    public bool DryRun { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, DryRun, Path(Input), output: Path(Output));
}

internal sealed class DiffSettings : SetupCliCommandSettings
{
    [CommandOption("--input <PATH>")]
    public string? Input { get; set; }

    [CommandOption("--baseline <PATH>")]
    public string? Baseline { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, input: Path(Input), baseline: Path(Baseline));
}

internal sealed class CatalogueListSettings : OutputSettings;

internal sealed class CatalogueItemSettings : SetupCliCommandSettings
{
    [CommandOption("--key <KEY>")]
    public string? Key { get; set; }

    [CommandOption("--output <PATH>")]
    public string? Output { get; set; }

    [CommandOption("--dry-run")]
    public bool DryRun { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, DryRun, output: Path(Output), key: Key);
}

internal sealed class EnvironmentRenderSettings : OutputSettings
{
    [CommandOption("--topology <ID>")]
    public string? Topology { get; set; }

    [CommandOption("--capability <ID>")]
    public string[] Capabilities { get; set; } = [];

    [CommandOption("--provider <ID>")]
    public string[] Providers { get; set; } = [];

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, DryRun, output: Path(Output), topology: Topology,
            capabilities: Capabilities, providers: Providers);
}

internal sealed class OperatorIdentityExportSettings : InputOutputSettings
{
    [CommandOption("--format <FORMAT>")]
    public string Format { get; set; } = "json";

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, DryRun, Path(Input), output: Path(Output), format: Format);
}

internal sealed class OperatorIdentityImportSettings : InputOutputSettings
{
    [CommandOption("--expected-revision <REVISION>")]
    public string? ExpectedRevision { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, DryRun, Path(Input), output: Path(Output),
            expectedRevision: ExpectedRevision);
}

internal sealed class DoctorSettings : SetupCliCommandSettings
{
    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode);
}
