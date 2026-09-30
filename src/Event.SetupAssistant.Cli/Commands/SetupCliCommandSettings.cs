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
        SetupCliCommandArgs? args = null)
    {
        args ??= new();
        return new(
            family,
            operation,
            Machine || invocationMode == SetupCliMode.Machine,
            args.DryRun,
            false,
            args.Input,
            args.Baseline,
            args.Output,
            args.Key,
            args.Topology,
            Normalize(args.Capabilities),
            Normalize(args.Providers),
            null)
        {
            Format = args.Format,
            ExpectedRevision = args.ExpectedRevision
        };
    }

    protected static string? Path(string? value) =>
        value == SetupCliArgumentPreflight.StandardIoSentinel ? "-" : value;

    private static string[] Normalize(IReadOnlyList<string>? values) =>
        values is null
            ? []
            : values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}

internal sealed record SetupCliCommandArgs(
    bool DryRun = false,
    string? Input = null,
    string? Baseline = null,
    string? Output = null,
    string? Key = null,
    string? Topology = null,
    IReadOnlyList<string>? Capabilities = null,
    IReadOnlyList<string>? Providers = null,
    string Format = "json",
    string? ExpectedRevision = null);

internal sealed class InputSettings : SetupCliCommandSettings
{
    [CommandOption("--input <PATH>")]
    public string? Input { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, new(Input: Path(Input)));
}

internal class OutputSettings : SetupCliCommandSettings
{
    [CommandOption("--output <PATH>")]
    public string? Output { get; set; }

    [CommandOption("--dry-run")]
    public bool DryRun { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, new(DryRun: DryRun, Output: Path(Output)));
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
        Create(family, operation, invocationMode, new(DryRun: DryRun, Input: Path(Input), Output: Path(Output)));
}

internal sealed class DiffSettings : SetupCliCommandSettings
{
    [CommandOption("--input <PATH>")]
    public string? Input { get; set; }

    [CommandOption("--baseline <PATH>")]
    public string? Baseline { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, new(Input: Path(Input), Baseline: Path(Baseline)));
}

internal sealed class CatalogueItemSettings : SetupCliCommandSettings
{
    [CommandOption("--key <KEY>")]
    public string? Key { get; set; }

    [CommandOption("--output <PATH>")]
    public string? Output { get; set; }

    [CommandOption("--dry-run")]
    public bool DryRun { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, new(DryRun: DryRun, Output: Path(Output), Key: Key));
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
        Create(family, operation, invocationMode, new(
            DryRun: DryRun,
            Output: Path(Output),
            Topology: Topology,
            Capabilities: Capabilities,
            Providers: Providers));
}

internal sealed class OperatorIdentityExportSettings : InputOutputSettings
{
    [CommandOption("--format <FORMAT>")]
    public string Format { get; set; } = "json";

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, new(
            DryRun: DryRun,
            Input: Path(Input),
            Output: Path(Output),
            Format: Format));
}

internal sealed class OperatorIdentityImportSettings : InputOutputSettings
{
    [CommandOption("--expected-revision <REVISION>")]
    public string? ExpectedRevision { get; set; }

    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode, new(
            DryRun: DryRun,
            Input: Path(Input),
            Output: Path(Output),
            ExpectedRevision: ExpectedRevision));
}

internal sealed class DoctorSettings : SetupCliCommandSettings
{
    public override SetupCliCommand Bind(string family, string operation, SetupCliMode invocationMode) =>
        Create(family, operation, invocationMode);
}
