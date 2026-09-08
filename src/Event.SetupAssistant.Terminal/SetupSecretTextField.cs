namespace ISLAMU.Event.SetupAssistant.Terminal;

using global::Terminal.Gui.Input;
using global::Terminal.Gui.Views;

internal sealed class SetupSecretTextField : TextField
{
    private readonly SetupTerminalSecretBuffer _secret;

    internal SetupSecretTextField(SetupTerminalSecretBuffer secret)
    {
        _secret = secret ?? throw new ArgumentNullException(nameof(secret));
        Secret = true;
        foreach (Command command in new[]
        {
            Command.Copy,
            Command.Cut,
            Command.Paste,
            Command.Undo,
            Command.Redo,
            Command.Context,
            Command.CutToEndOfLine,
            Command.CutToStartOfLine
        })
            AddCommand(command, () => BlockSensitiveCommand());
        foreach (Command command in new[]
        {
            Command.KillWordLeft,
            Command.KillWordRight,
            Command.Left,
            Command.Right,
            Command.LeftStart,
            Command.RightEnd,
            Command.WordLeft,
            Command.WordRight,
            Command.SelectAll
        })
            AddCommand(command, () => true);
        AddCommand(Command.DeleteCharLeft, () => RemoveLast());
        AddCommand(Command.DeleteCharRight, () => RemoveLast());
        AddCommand(Command.DeleteAll, () => ClearInput());
    }

    internal event EventHandler? InputRejected;

    internal event EventHandler? SensitiveCommandBlocked;

    protected override bool OnKeyDownNotHandled(Key key)
    {
        if (key.IsCtrl || key.IsAlt || string.IsNullOrEmpty(key.AsGrapheme))
            return true;
        if (_secret.TryAppend(key.AsGrapheme))
            RefreshMask();
        else
            InputRejected?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool BlockSensitiveCommand()
    {
        SensitiveCommandBlocked?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool RemoveLast()
    {
        _secret.RemoveLast();
        RefreshMask();
        return true;
    }

    private bool ClearInput()
    {
        ClearSensitiveState();
        return true;
    }

    private void RefreshMask()
    {
        Text = new string('●', _secret.Count);
        ClearHistoryChanges();
    }

    internal void ClearSensitiveState()
    {
        _secret.Clear();
        Text = string.Empty;
        ClearHistoryChanges();
    }
}
