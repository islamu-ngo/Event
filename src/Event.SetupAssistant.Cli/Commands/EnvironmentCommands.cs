namespace ISLAMU.Event.SetupAssistant.Cli.Commands;

internal static class EnvironmentCommands
{
    internal static SetupCliFamilyDescriptor Descriptor { get; } = SetupCliCommandRegistry.Family(
        "env",
        SetupCliCommandRegistry.Operation<EnvironmentRenderSettings>("render"),
        SetupCliCommandRegistry.Operation<InputSettings>("validate"));
}
