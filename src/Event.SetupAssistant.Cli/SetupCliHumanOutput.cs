using Spectre.Console;

namespace ISLAMU.Event.SetupAssistant.Cli;

internal static class SetupCliHumanOutput
{
    internal static void Render(IAnsiConsole console, SetupCliCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(result);

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow("Status", SetupCliResults.Lower(result.Exit));
        grid.AddRow("Scope", "Local draft or export; no platform changes were applied.");
        if (result.Artifacts.Count > 0)
            grid.AddRow("Artifacts", string.Join(", ", result.Artifacts.Select(artifact => artifact.Kind)));
        if (result.Diagnostics.Count > 0)
            grid.AddRow("Diagnostics", string.Join(", ", result.Diagnostics.Select(diagnostic => diagnostic.Code)));
        if (result.Readiness.State != "ready")
            grid.AddRow("Readiness", result.Readiness.State);
        console.Write(new Panel(grid).Header("event-setup"));
    }
}
