namespace ISLAMU.Event.SetupAssistant.Terminal.Views;

using System.Text;
using System.Collections.ObjectModel;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using global::Terminal.Gui.Input;
using global::Terminal.Gui.ViewBase;
using global::Terminal.Gui.Views;

internal enum ManifestWorkspaceKind
{
    Manifest,
    TenantPackage
}

internal sealed class ManifestView : View
{
    private readonly string _baseDirectory;
    private readonly ManifestWorkspaceKind _kind;
    private readonly TextField _displayName;
    private readonly TextField _fileName;
    private readonly TextField _inputFile;
    private readonly TextField _baselineFile;
    private readonly TextField _sourceName;
    private readonly TextField _tenantName;
    private readonly ListView _preview;
    private readonly ObservableCollection<string> _previewLines = [];
    private readonly Label _status;
    private readonly ProtectedArtifactWriter _writer;
    private byte[] _previewBytes = [];

    internal ManifestView(
        ManifestWorkspaceKind kind,
        ProtectedArtifactWriter writer,
        string baseDirectory)
    {
        _kind = kind;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));

        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var heading = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Text = SetupTerminalText.Get(kind == ManifestWorkspaceKind.Manifest
                ? "ManifestHeading"
                : "TenantPackageHeading")
        };
        var sourceLabel = new Label { X = 0, Y = 2, Text = SetupTerminalText.Get("SourceName") };
        _sourceName = new TextField
        {
            X = 17,
            Y = 2,
            Width = Dim.Fill(),
            Text = kind == ManifestWorkspaceKind.Manifest ? "event-setup" : "tenant-package"
        };
        var tenantLabel = new Label { X = 0, Y = 3, Text = SetupTerminalText.Get("TenantName") };
        _tenantName = new TextField
        {
            X = 17,
            Y = 3,
            Width = Dim.Fill(),
            Text = "tenant-source"
        };
        var displayLabel = new Label { X = 0, Y = 4, Text = SetupTerminalText.Get("DisplayName") };
        _displayName = new TextField
        {
            X = 17,
            Y = 4,
            Width = Dim.Fill(),
            Text = "Tenant source"
        };
        var fileLabel = new Label { X = 0, Y = 5, Text = SetupTerminalText.Get("OutputFile") };
        _fileName = new TextField
        {
            X = 17,
            Y = 5,
            Width = Dim.Fill(),
            Text = kind == ManifestWorkspaceKind.Manifest
                ? "configuration-manifest.json"
                : "tenant-package.json"
        };
        var inputLabel = new Label { X = 0, Y = 6, Text = SetupTerminalText.Get("InputFile") };
        _inputFile = new TextField { X = 17, Y = 6, Width = Dim.Fill() };
        var baselineLabel = new Label { X = 0, Y = 7, Text = SetupTerminalText.Get("BaselineFile") };
        _baselineFile = new TextField { X = 17, Y = 7, Width = Dim.Fill() };
        var prepare = new Button
        {
            X = 0,
            Y = 9,
            Text = SetupTerminalText.Get("PreparePreview")
        };
        var save = new Button
        {
            X = Pos.Right(prepare) + 1,
            Y = 9,
            Text = SetupTerminalText.Get("SaveProtected")
        };
        _status = new Label
        {
            X = 0,
            Y = 12,
            Width = Dim.Fill(),
            Text = SetupTerminalText.Get("LocalDraftReady")
        };
        _preview = new ListView
        {
            X = 0,
            Y = 14,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _preview.SetSource(_previewLines);
        var open = new Button { X = 0, Y = 10, Text = SetupTerminalText.Get("OpenValidate") };
        var compare = new Button { X = Pos.Right(open) + 1, Y = 10, Text = SetupTerminalText.Get("CompareFiles") };
        open.Accepting += (_, args) => { args.Handled = true; OpenLocalFile(); };
        compare.Accepting += (_, args) => { args.Handled = true; CompareLocalFiles(); };

        bool tenant = kind == ManifestWorkspaceKind.TenantPackage;
        tenantLabel.Visible = tenant;
        _tenantName.Visible = tenant;
        displayLabel.Visible = tenant;
        _displayName.Visible = tenant;
        prepare.Accepting += PrepareAccepted;
        save.Accepting += SaveAccepted;
        _sourceName.ValueChanged += (_, _) => ClearPreview();
        _tenantName.ValueChanged += (_, _) => ClearPreview();
        _displayName.ValueChanged += (_, _) => ClearPreview();
        _inputFile.ValueChanged += (_, _) => ClearPreview();
        _baselineFile.ValueChanged += (_, _) => DifferenceCount = null;
        Add(
            heading,
            sourceLabel,
            _sourceName,
            tenantLabel,
            _tenantName,
            displayLabel,
            _displayName,
            fileLabel,
            _fileName,
            inputLabel,
            _inputFile,
            baselineLabel,
            _baselineFile,
            prepare,
            save,
            open,
            compare,
            _status,
            _preview);
    }

    internal string SourceName
    {
        get => _sourceName.Text;
        set => _sourceName.Text = value;
    }

    internal string TenantName
    {
        get => _tenantName.Text;
        set => _tenantName.Text = value;
    }

    internal string DisplayName
    {
        get => _displayName.Text;
        set => _displayName.Text = value;
    }

    internal string FileName
    {
        get => _fileName.Text;
        set => _fileName.Text = value;
    }

    internal ReadOnlyMemory<byte> GetPreviewBytes() => new((byte[])_previewBytes.Clone());
    internal string Status => _status.Text.ToString() ?? string.Empty;
    internal string InputFileName
    {
        get => _inputFile.Text;
        set => _inputFile.Text = value;
    }
    internal string BaselineFileName
    {
        get => _baselineFile.Text;
        set => _baselineFile.Text = value;
    }
    internal int? DifferenceCount { get; private set; }

    internal bool OpenLocalFile()
    {
        ClearPreview();
        OfflinePortabilityDocument? document = ReadLocalDocument(InputFileName);
        if (document is null)
            return Fail("input-invalid");
        OfflinePortabilityFormatResult formatted = OfflinePortabilityWorkflow.Format(document);
        if (!formatted.Succeeded)
            return Fail("input-invalid");
        _previewBytes = formatted.Output!.Bytes.ToArray();
        ShowPreview(Encoding.UTF8.GetString(_previewBytes));
        _status.Text = SetupTerminalText.Get("PreviewReady");
        DrawUpdated();
        return true;
    }

    internal bool CompareLocalFiles()
    {
        ClearPreview();
        OfflinePortabilityDocument? candidate = ReadLocalDocument(InputFileName);
        OfflinePortabilityDocument? baseline = ReadLocalDocument(BaselineFileName);
        if (candidate is null || baseline is null)
            return Fail("input-invalid");
        SetupDiffResult result = OfflinePortabilityWorkflow.Diff(baseline, candidate);
        DifferenceCount = result.Added.Count + result.Removed.Count + result.Changed.Count;
        _status.Text = $"{SetupTerminalText.Get("DifferenceCount")}: {DifferenceCount}";
        ShowPreview(string.Join(Environment.NewLine,
            result.Added.Select(key => $"+ {key.Value}")
                .Concat(result.Removed.Select(key => $"- {key.Value}"))
                .Concat(result.Changed.Select(key => $"~ {key.Value}"))));
        DrawUpdated();
        return true;
    }

    private OfflinePortabilityDocument? ReadLocalDocument(string fileName)
    {
        if (!SetupTerminalFileName.IsSafe(fileName))
            return null;
        try
        {
            using var input = new FileStream(
                Path.Combine(_baseDirectory, fileName), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > ProtectedArtifactWriter.MaximumBytes)
                return null;
            byte[] bytes = new byte[(int)input.Length];
            try
            {
                input.ReadExactly(bytes);
                if (input.ReadByte() != -1)
                    return null;
                OfflinePortabilityResult opened = _kind == ManifestWorkspaceKind.Manifest
                    ? OfflinePortabilityWorkflow.OpenManifest(Profile(),
                        Selection(SetupScope.Instance, "instance.settings", "instance.documents", "instance.legal_documents"), bytes)
                    : OfflinePortabilityWorkflow.OpenTenantPackage(Profile(),
                        Selection(SetupScope.Tenant, "tenant.settings", "tenant.documents", "tenant.legal_documents"), bytes);
                return opened.Succeeded ? opened.Document : null;
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal bool PreparePreview()
    {
        OfflinePortabilityResult created = _kind == ManifestWorkspaceKind.Manifest
            ? OfflinePortabilityWorkflow.CreateManifest(
                Profile(),
                Selection(SetupScope.Instance, "instance.settings", "instance.documents", "instance.legal_documents"),
                _sourceName.Text,
                null,
                null)
            : OfflinePortabilityWorkflow.CreateTenantPackage(
                Profile(),
                Selection(SetupScope.Tenant, "tenant.settings", "tenant.documents", "tenant.legal_documents"),
                _sourceName.Text,
                _tenantName.Text,
                _displayName.Text,
                null);
        if (!created.Succeeded)
            return Fail(created.Diagnostics.Count == 0
                ? "draft-invalid"
                : created.Diagnostics[0].Code.Value);

        OfflinePortabilityResult validated =
            OfflinePortabilityWorkflow.Validate(created.Document!);
        if (!validated.Succeeded)
            return Fail(validated.Diagnostics.Count == 0
                ? "draft-invalid"
                : validated.Diagnostics[0].Code.Value);

        OfflinePortabilityFormatResult formatted =
            OfflinePortabilityWorkflow.Format(validated.Document!);
        if (!formatted.Succeeded)
            return Fail(formatted.Diagnostics.Count == 0
                ? "draft-invalid"
                : formatted.Diagnostics[0].Code.Value);

        ClearPreview();
        _previewBytes = formatted.Output!.Bytes.ToArray();
        ShowPreview(Encoding.UTF8.GetString(_previewBytes));
        _status.Text = SetupTerminalText.Get("PreviewReady");
        DrawUpdated();
        return true;
    }

    internal async Task<ProtectedArtifactStatus> SaveProtectedAsync(
        CancellationToken cancellationToken = default)
    {
        if (_previewBytes.Length == 0
            && !(InputFileName.Length > 0 ? OpenLocalFile() : PreparePreview()))
            return ProtectedArtifactStatus.InvalidRequest;
        if (!SetupTerminalFileName.IsSafe(_fileName.Text))
        {
            Fail("output-name-invalid");
            return ProtectedArtifactStatus.InvalidRequest;
        }

        using ProtectedArtifactPreparation preparation = await _writer.PrepareAsync(
            SetupArtifactKind.Configuration,
            Path.Combine(_baseDirectory, _fileName.Text),
            _previewBytes,
            cancellationToken);
        ProtectedArtifactStatus result = await preparation.CommitAsync(cancellationToken);
        _status.Text = result == ProtectedArtifactStatus.Written
            ? SetupTerminalText.Get("ProtectedDraftSaved")
            : SetupTerminalText.Get("ProtectedDraftFailed");
        DrawUpdated();
        return result;
    }

    internal void ClearPreview()
    {
        DifferenceCount = null;
        if (_previewBytes.Length > 0)
            Array.Clear(_previewBytes);
        _previewBytes = [];
        _previewLines.Clear();
        _status.Text = SetupTerminalText.Get("LocalDraftReady");
        DrawUpdated();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ClearPreview();
        base.Dispose(disposing);
    }

    private static SetupProfile Profile() => new(
        new SetupProfileIdentity("event-setup"),
        [],
        [new SetupTopologyKey("standalone")]);

    private static SetupSelection Selection(SetupScope scope, params string[] sections) => new(
        scope,
        ConfigurationImportApplyMode.PreviewOnly,
        sections.Select(section => new PortableSectionKey(section)));

    private bool Fail(string code)
    {
        ClearPreview();
        _status.Text = $"{SetupTerminalText.Get("DraftInvalid")} ({code})";
        DrawUpdated();
        return false;
    }

    private void DrawUpdated()
    {
        _status.SetNeedsDraw();
        _preview.SetNeedsDraw();
    }

    private void ShowPreview(string text)
    {
        _previewLines.Clear();
        foreach (string line in text.Split('\n'))
            _previewLines.Add(line);
    }

    private void PrepareAccepted(object? sender, CommandEventArgs args)
    {
        args.Handled = true;
        PreparePreview();
    }

    private void SaveAccepted(object? sender, CommandEventArgs args)
    {
        args.Handled = true;
        SaveProtectedAsync().GetAwaiter().GetResult();
    }
}
