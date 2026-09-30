namespace ISLAMU.Event.SetupAssistant.Cli.Commands;

internal static class ManifestCommands
{
    internal static SetupCliFamilyDescriptor Descriptor { get; } = SetupCliCommandRegistry.Family(
        "manifest",
        SetupCliCommandRegistry.Operation<OutputSettings>("create"),
        SetupCliCommandRegistry.Operation<InputSettings>("open"),
        SetupCliCommandRegistry.Operation<InputSettings>("validate"),
        SetupCliCommandRegistry.Operation<InputOutputSettings>("format"),
        SetupCliCommandRegistry.Operation<DiffSettings>("diff"),
        SetupCliCommandRegistry.Operation<InputSettings>("coverage"),
        SetupCliCommandRegistry.Operation<InputOutputSettings>("export"));
}
