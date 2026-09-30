namespace ISLAMU.Event.SetupAssistant.Cli.Commands;

internal static class LegalCommands
{
    internal static SetupCliFamilyDescriptor Descriptor { get; } = SetupCliCommandRegistry.Family(
        "legal",
        SetupCliCommandRegistry.Operation<InputSettings>("validate"),
        SetupCliCommandRegistry.Operation<InputSettings>("preview"));
}
