namespace ISLAMU.Event.SetupAssistant.Terminal.Views;

using ISLAMU.Event.Setup.Core;
using global::Terminal.Gui.Input;
using global::Terminal.Gui.ViewBase;
using global::Terminal.Gui.Views;

internal sealed class LegalDraftView : View
{
    private readonly TextField _accountableIdentity;
    private readonly TextField _markdown;
    private readonly TextField _summary;
    private readonly TextField _title;
    private readonly Label _preview;
    private readonly Label _status;

    internal LegalDraftView()
    {
        Width = Dim.Fill();
        Height = Dim.Fill();

        var heading = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = SetupTerminalText.Get("LegalHeading")
        };
        var titleLabel = new Label { X = 0, Y = 2, Text = SetupTerminalText.Get("LegalTitle") };
        _title = new TextField { X = 22, Y = 2, Width = Dim.Fill(), Text = "Tenant terms" };
        var summaryLabel = new Label { X = 0, Y = 3, Text = SetupTerminalText.Get("LegalSummary") };
        _summary = new TextField { X = 22, Y = 3, Width = Dim.Fill(), Text = "Local review required" };
        var identityLabel = new Label { X = 0, Y = 4, Text = SetupTerminalText.Get("AccountableIdentity") };
        _accountableIdentity = new TextField { X = 22, Y = 4, Width = Dim.Fill() };
        var markdownLabel = new Label { X = 0, Y = 5, Text = SetupTerminalText.Get("LegalMarkdown") };
        _markdown = new TextField
        {
            X = 22,
            Y = 5,
            Width = Dim.Fill(),
            Text = "# Terms\\n\\nOperator: {{accountable_identity}}."
        };
        var preview = new Button { X = 0, Y = 7, Text = SetupTerminalText.Get("PreparePreview") };
        _status = new Label
        {
            X = 0,
            Y = 9,
            Width = Dim.Fill(),
            Text = SetupTerminalText.Get("LocalDraftReady")
        };
        _preview = new Label
        {
            X = 0,
            Y = 11,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = string.Empty
        };

        preview.Accepting += PreviewAccepted;
        foreach (TextField field in new[] { _title, _summary, _accountableIdentity, _markdown })
        {
            field.ValueChanged += (_, _) =>
            {
                _preview.Text = string.Empty;
                _status.Text = SetupTerminalText.Get("LocalDraftReady");
                _preview.SetNeedsDraw();
                _status.SetNeedsDraw();
            };
        }
        Add(
            heading,
            titleLabel,
            _title,
            summaryLabel,
            _summary,
            identityLabel,
            _accountableIdentity,
            markdownLabel,
            _markdown,
            preview,
            _status,
            _preview);
    }

    internal string LegalTitle
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    internal string Summary
    {
        get => _summary.Text;
        set => _summary.Text = value;
    }

    internal string AccountableIdentity
    {
        get => _accountableIdentity.Text;
        set => _accountableIdentity.Text = value;
    }

    internal string Markdown
    {
        get => DecodeMarkdown(_markdown.Text);
        set => _markdown.Text = EncodeMarkdown(value);
    }

    internal string Preview => _preview.Text.ToString() ?? string.Empty;
    internal string Status => _status.Text.ToString() ?? string.Empty;

    internal bool PreparePreview()
    {
        try
        {
            OfflineLegalLocale locale = OfflineLegalLocale.Create(
                "en",
                _title.Text,
                _summary.Text,
                DecodeMarkdown(_markdown.Text));
            OfflineLegalDraft draft = OfflineLegalDraft.Create(
                OfflineLegalDraftScope.Tenant,
                OfflineLegalDocumentKind.TenantTerms,
                OfflineLegalAudience.Public,
                false,
                [],
                null,
                OfflineLegalDraftProvenance.Blank,
                [locale]);
            OfflineLegalPreview preview = draft.Preview(
                "en",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["accountable_identity"] = _accountableIdentity.Text
                });
            _preview.Text = preview.IsReady ? preview.Html : string.Empty;
            _status.Text = preview.IsReady
                ? SetupTerminalText.Get("LegalPreviewReady")
                : SetupTerminalText.Get("LegalPreviewIncomplete");
            _preview.SetNeedsDraw();
            _status.SetNeedsDraw();
            return preview.IsReady;
        }
        catch (ArgumentException)
        {
            _preview.Text = string.Empty;
            _status.Text = SetupTerminalText.Get("LegalPreviewInvalid");
            _preview.SetNeedsDraw();
            _status.SetNeedsDraw();
            return false;
        }
    }

    internal void ClearPrivateState()
    {
        _accountableIdentity.Text = string.Empty;
        _preview.Text = string.Empty;
        _status.Text = SetupTerminalText.Get("LocalDraftReady");
        _preview.SetNeedsDraw();
        _status.SetNeedsDraw();
    }

    private void PreviewAccepted(object? sender, CommandEventArgs args)
    {
        args.Handled = true;
        PreparePreview();
    }

    private static string DecodeMarkdown(string value) =>
        value.Replace("\\n", "\n", StringComparison.Ordinal);

    private static string EncodeMarkdown(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
