namespace ISLAMU.Event.SetupAssistant.Cli.Commands;

internal static class DoctorCommands
{
    internal static SetupCliFamilyDescriptor Descriptor { get; } = SetupCliCommandRegistry.Family(
        "doctor",
        SetupCliCommandRegistry.Operation<DoctorSettings>("doctor"));
}
