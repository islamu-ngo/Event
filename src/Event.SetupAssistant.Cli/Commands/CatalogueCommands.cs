namespace ISLAMU.Event.SetupAssistant.Cli.Commands;

internal static class CatalogueCommands
{
    internal static SetupCliFamilyDescriptor Descriptor { get; } = SetupCliCommandRegistry.Family(
        "catalogue",
        SetupCliCommandRegistry.Operation<OutputSettings>("list"),
        SetupCliCommandRegistry.Operation<CatalogueItemSettings>("show"),
        SetupCliCommandRegistry.Operation<CatalogueItemSettings>("describe"));
}
