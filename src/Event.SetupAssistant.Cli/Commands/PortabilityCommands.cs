namespace ISLAMU.Event.SetupAssistant.Cli.Commands;

internal static class PortabilityCommands
{
    internal static SetupCliFamilyDescriptor Descriptor { get; } = SetupCliCommandRegistry.Family(
        "portability",
        SetupCliCommandRegistry.Operation<OperatorIdentityExportSettings>("export-operator-identity"),
        SetupCliCommandRegistry.Operation<OperatorIdentityImportSettings>("import-operator-identity"));
}
