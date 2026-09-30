namespace ISLAMU.Event.SetupAssistant.Cli.Commands;

internal static class TenantPackageCommands
{
    internal static SetupCliFamilyDescriptor Descriptor { get; } = SetupCliCommandRegistry.Family(
        "tenant-package",
        SetupCliCommandRegistry.Operation<OutputSettings>("create"),
        SetupCliCommandRegistry.Operation<InputSettings>("open"),
        SetupCliCommandRegistry.Operation<InputSettings>("validate"),
        SetupCliCommandRegistry.Operation<InputOutputSettings>("format"),
        SetupCliCommandRegistry.Operation<DiffSettings>("diff"),
        SetupCliCommandRegistry.Operation<InputSettings>("coverage"),
        SetupCliCommandRegistry.Operation<InputOutputSettings>("export"));
}
