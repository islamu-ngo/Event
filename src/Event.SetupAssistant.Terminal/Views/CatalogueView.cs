namespace ISLAMU.Event.SetupAssistant.Terminal.Views;

using ISLAMU.Event.Setup.Core.Environment;
using System.Collections.ObjectModel;
using global::Terminal.Gui.Input;
using global::Terminal.Gui.ViewBase;
using global::Terminal.Gui.Views;

internal sealed class CatalogueView : View
{
    private readonly TextField _key;
    private readonly ListView _output;
    private readonly ObservableCollection<string> _rows = [];

    internal CatalogueView()
    {
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var heading = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = SetupTerminalText.Get("CatalogueHeading")
        };
        var keyLabel = new Label
        {
            X = 0,
            Y = 2,
            Text = SetupTerminalText.Get("CatalogueKey")
        };
        _key = new TextField
        {
            X = 14,
            Y = 2,
            Width = Dim.Fill(),
            Text = "PUBLIC_BASE_URL"
        };
        var list = new Button
        {
            X = 0,
            Y = 4,
            Text = SetupTerminalText.Get("CatalogueList")
        };
        var lookup = new Button
        {
            X = Pos.Right(list) + 1,
            Y = 4,
            Text = SetupTerminalText.Get("CatalogueLookup")
        };
        _output = new ListView
        {
            X = 0,
            Y = 6,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _output.SetSource(_rows);

        list.Accepting += ListAccepted;
        lookup.Accepting += LookupAccepted;
        Add(heading, keyLabel, _key, list, lookup, _output);
    }

    internal string Key
    {
        get => _key.Text;
        set => _key.Text = value;
    }

    internal string Output => string.Join(Environment.NewLine, _rows);

    internal void ShowList()
    {
        string[] rows = PlatformEnvironmentCatalogue.Catalogue.Definitions
            .OrderBy(definition => definition.Key, StringComparer.Ordinal)
            .Select(Describe)
            .ToArray();
        _rows.Clear();
        foreach (string row in rows)
            _rows.Add(row);
        _output.SetNeedsDraw();
    }

    internal void ShowLookup()
    {
        EnvironmentVariableDefinition? definition =
            PlatformEnvironmentCatalogue.Catalogue.Lookup(_key.Text);
        _rows.Clear();
        _rows.Add(definition is null
            ? SetupTerminalText.Get("CatalogueNotFound")
            : Describe(definition));
        _output.SetNeedsDraw();
    }

    private static string Describe(EnvironmentVariableDefinition definition) =>
        $"{definition.Key} | {definition.Category} | {definition.Sensitivity} | {definition.Requirement}";

    private void ListAccepted(object? sender, CommandEventArgs args)
    {
        args.Handled = true;
        ShowList();
    }

    private void LookupAccepted(object? sender, CommandEventArgs args)
    {
        args.Handled = true;
        ShowLookup();
    }
}
