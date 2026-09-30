namespace ISLAMU.Event.SetupAssistant.Terminal;

using System.ComponentModel;
using System.Drawing;
using System.Windows.Input;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.SetupAssistant.Presentation;
using ISLAMU.Event.SetupAssistant.Terminal.Views;
using global::Terminal.Gui.App;
using global::Terminal.Gui.ViewBase;
using global::Terminal.Gui.Views;

internal enum SetupTerminalWorkspaceKind
{
    Environment,
    Catalogue,
    Manifest,
    TenantPackage,
    LegalDraft,
    IdentityDraft
}

internal sealed class SetupTerminalWindow : Window
{
    internal const int MinimumTerminalHeight = 17;
    internal const int MinimumTerminalWidth = 80;
    private const int ContentStart = 23;

    private readonly IApplication _application;
    private readonly Button _closeButton;
    private readonly Button _generateButton;
    private readonly TextField _outputFileName;
    private readonly Button _saveManualButton;
    private readonly SetupSecretTextField _secretField;
    private readonly SetupTerminalSecretBuffer _secret;
    private readonly SetupTerminalArtifactOperation _operation;
    private readonly Label _status;
    private readonly Label _smallTerminalNotice;
    private readonly View _navigation;
    private readonly List<Button> _navigationButtons = [];
    private readonly View[] _standardViews;
    private readonly View[] _environmentViews;
    private readonly Dictionary<SetupTerminalWorkspaceKind, View> _offlineViews;
    private readonly IdentityDraftView _identityDraftView;
    private readonly LegalDraftView _legalDraftView;
    private readonly ManifestView _manifestView;
    private readonly ManifestView _tenantPackageView;
    private readonly SetupPresentationWorkspace _workspace;
    private SetupTerminalWorkspaceKind _activeWorkspace = SetupTerminalWorkspaceKind.Environment;
    private bool _disposed;

    internal SetupTerminalWindow(
        IApplication application,
        SetupPresentationWorkspace workspace,
        SetupTerminalArtifactOperation operation,
        SetupTerminalSecretBuffer secret,
        bool protectedOutputAvailable)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        _secret = secret ?? throw new ArgumentNullException(nameof(secret));

        Title = SetupTerminalText.Get("WindowTitle");
        Width = Dim.Fill();
        Height = Dim.Fill();

        var introduction = new Label
        {
            X = ContentStart,
            Y = 1,
            Width = Dim.Fill(2),
            Height = 2,
            Text = SetupTerminalText.Get("Introduction")
        };
        var outputLabel = new Label { X = ContentStart, Y = 3, Text = SetupTerminalText.Get("OutputFile") };
        _outputFileName = new TextField
        {
            X = ContentStart + 17,
            Y = 3,
            Width = Dim.Fill(2),
            Text = ".env.setup"
        };
        var secretLabel = new Label { X = ContentStart, Y = 5, Text = SetupTerminalText.Get("SetupSecret") };
        _secretField = new SetupSecretTextField(_secret)
        {
            X = ContentStart + 17,
            Y = 5,
            Width = Dim.Fill(2)
        };
        _saveManualButton = new Button { X = ContentStart, Y = 7, Text = SetupTerminalText.Get("SaveManual") };
        _generateButton = new Button
        {
            X = ContentStart,
            Y = 8,
            Text = SetupTerminalText.Get("Generate")
        };
        _closeButton = new Button
        {
            X = ContentStart,
            Y = 9,
            Text = SetupTerminalText.Get("Close")
        };
        _status = new Label
        {
            X = ContentStart,
            Y = 11,
            Width = Dim.Fill(2),
            Height = 2,
            Text = protectedOutputAvailable
                ? SetupTerminalText.Get("Ready")
                : SetupTerminalText.Get("ProtectedUnavailable")
        };
        var limitations = new Label
        {
            X = ContentStart,
            Y = 14,
            Width = Dim.Fill(2),
            Height = 2,
            Text = SetupTerminalText.Get("Limitations")
        };

        _smallTerminalNotice = new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = 2,
            Text = SetupTerminalText.Get("TerminalTooSmall"),
            Visible = false
        };

        var protectedWriter = new ProtectedArtifactWriter();
        string baseDirectory = Directory.GetCurrentDirectory();
        var catalogueView = new CatalogueView
        {
            X = ContentStart,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(1),
            Visible = false
        };
        _manifestView = new ManifestView(
            ManifestWorkspaceKind.Manifest,
            protectedWriter,
            baseDirectory)
        {
            X = ContentStart,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(1),
            Visible = false
        };
        _tenantPackageView = new ManifestView(
            ManifestWorkspaceKind.TenantPackage,
            protectedWriter,
            baseDirectory)
        {
            X = ContentStart,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(1),
            Visible = false
        };
        _legalDraftView = new LegalDraftView
        {
            X = ContentStart,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(1),
            Visible = false
        };
        _identityDraftView = new IdentityDraftView(protectedWriter, baseDirectory)
        {
            X = ContentStart,
            Y = 1,
            Width = Dim.Fill(2),
            Height = Dim.Fill(1),
            Visible = false
        };
        _offlineViews = new Dictionary<SetupTerminalWorkspaceKind, View>
        {
            [SetupTerminalWorkspaceKind.Catalogue] = catalogueView,
            [SetupTerminalWorkspaceKind.Manifest] = _manifestView,
            [SetupTerminalWorkspaceKind.TenantPackage] = _tenantPackageView,
            [SetupTerminalWorkspaceKind.LegalDraft] = _legalDraftView,
            [SetupTerminalWorkspaceKind.IdentityDraft] = _identityDraftView
        };

        _navigation = new View
        {
            X = 1,
            Y = 1,
            Width = ContentStart - 2,
            Height = Dim.Fill(1)
        };
        AddNavigationButton(SetupTerminalWorkspaceKind.Environment, "NavigationEnvironment", 0);
        AddNavigationButton(SetupTerminalWorkspaceKind.Catalogue, "NavigationCatalogue", 2);
        AddNavigationButton(SetupTerminalWorkspaceKind.Manifest, "NavigationManifest", 4);
        AddNavigationButton(SetupTerminalWorkspaceKind.TenantPackage, "NavigationTenantPackage", 6);
        AddNavigationButton(SetupTerminalWorkspaceKind.LegalDraft, "NavigationLegalDraft", 8);
        AddNavigationButton(SetupTerminalWorkspaceKind.IdentityDraft, "NavigationIdentityDraft", 10);

        Add(
            introduction,
            outputLabel,
            _outputFileName,
            secretLabel,
            _secretField,
            _saveManualButton,
            _generateButton,
            _closeButton,
            _status,
            limitations,
            _navigation,
            catalogueView,
            _manifestView,
            _tenantPackageView,
            _legalDraftView,
            _identityDraftView,
            _smallTerminalNotice);
        _environmentViews =
        [
            introduction,
            outputLabel,
            _outputFileName,
            secretLabel,
            _secretField,
            _saveManualButton,
            _generateButton,
            _closeButton,
            _status,
            limitations
        ];
        _standardViews =
        [
            .. _environmentViews,
            _navigation,
            .. _offlineViews.Values
        ];

        _outputFileName.TextChanging += ValidateOutputName;
        _outputFileName.ValueChanged += (_, _) => RefreshCommandState();
        _secretField.ValueChanged += SecretFieldValueChanged;
        _secretField.InputRejected += SecretInputRejected;
        _secretField.SensitiveCommandBlocked += SecretCommandBlocked;
        _saveManualButton.Accepting += SaveManual;
        _generateButton.Accepting += Generate;
        _closeButton.Accepting += CloseOrCancel;
        _workspace.PropertyChanged += WorkspacePropertyChanged;
        _workspace.ExecuteCommand.CanExecuteChanged += CommandCanExecuteChanged;
        _workspace.CancelCommand.CanExecuteChanged += CommandCanExecuteChanged;
        ViewportChanged += TerminalViewportChanged;
        RefreshCommandState();
    }

    internal int ExitCode { get; private set; } = 4;
    internal SetupTerminalWorkspaceKind ActiveWorkspace => _activeWorkspace;

    internal void RequestStopFromSignal()
    {
        ClearSecretField();
        if (_workspace.CancelCommand.CanExecute(null))
            _workspace.CancelCommand.Execute(null);
        _application.RequestStop(this);
    }

    private void ValidateOutputName(object? sender, ResultEventArgs<string> args)
    {
        if (!SetupTerminalFileName.IsPartialSafe(args.Result ?? string.Empty))
        {
            args.Handled = true;
            SetStatus(SetupTerminalText.Get("InvalidOutputPartial"));
        }
    }

    private void SecretCommandBlocked(object? sender, EventArgs args) =>
        SetStatus(SetupTerminalText.Get("SecretCommandsDisabled"));

    private void SecretInputRejected(object? sender, EventArgs args) =>
        SetStatus(SetupTerminalText.Get("InvalidSecret"));

    private void TerminalViewportChanged(object? sender, DrawEventArgs args) =>
        ApplyViewportPolicy(args.NewViewport.Size);

    internal void ApplyViewportPolicy(Size size)
    {
        bool tooSmall = size.Width < MinimumTerminalWidth
            || size.Height < MinimumTerminalHeight;
        foreach (View view in _standardViews)
            view.Visible = false;
        _navigation.Visible = !tooSmall;
        if (!tooSmall)
        {
            if (_activeWorkspace == SetupTerminalWorkspaceKind.Environment)
            {
                foreach (View view in _environmentViews)
                    view.Visible = true;
            }
            else
            {
                _offlineViews[_activeWorkspace].Visible = true;
            }
        }
        _smallTerminalNotice.Visible = tooSmall;
        if (tooSmall)
        {
            _saveManualButton.Enabled = false;
            _generateButton.Enabled = false;
        }
        else
        {
            RefreshCommandState();
        }
    }

    internal void SelectWorkspace(SetupTerminalWorkspaceKind workspace)
    {
        if (_activeWorkspace == workspace)
            return;
        ClearWorkspaceState(_activeWorkspace);
        _activeWorkspace = workspace;
        ApplyViewportPolicy(Viewport.Size);
    }

    private void ClearWorkspaceState(SetupTerminalWorkspaceKind workspace)
    {
        switch (workspace)
        {
            case SetupTerminalWorkspaceKind.Environment:
                ClearSecretField();
                break;
            case SetupTerminalWorkspaceKind.Manifest:
                _manifestView.ClearPreview();
                break;
            case SetupTerminalWorkspaceKind.TenantPackage:
                _tenantPackageView.ClearPreview();
                break;
            case SetupTerminalWorkspaceKind.LegalDraft:
                _legalDraftView.ClearPrivateState();
                break;
            case SetupTerminalWorkspaceKind.IdentityDraft:
                _identityDraftView.ClearPrivateState();
                break;
        }
    }

    private void AddNavigationButton(
        SetupTerminalWorkspaceKind workspace,
        string textKey,
        int row)
    {
        var button = new Button
        {
            X = 0,
            Y = row,
            Width = ContentStart - 3,
            Text = SetupTerminalText.Get(textKey)
        };
        button.Accepting += (_, args) =>
        {
            args.Handled = true;
            SelectWorkspace(workspace);
        };
        _navigationButtons.Add(button);
        _navigation.Add(button);
    }

    private void SecretFieldValueChanged(object? sender, ValueChangedEventArgs<string?> args) =>
        RefreshCommandState();

    private void SaveManual(object? sender, global::Terminal.Gui.Input.CommandEventArgs args)
    {
        args.Handled = true;
        if (!PrepareFileName() || !_operation.PrepareManual())
        {
            ClearSecretField();
            SetStatus(SetupTerminalText.Get("InvalidManual"));
            return;
        }

        _secretField.Text = string.Empty;
        ExecuteWorkspace();
    }

    private void Generate(object? sender, global::Terminal.Gui.Input.CommandEventArgs args)
    {
        args.Handled = true;
        if (!PrepareFileName())
        {
            SetStatus(SetupTerminalText.Get("InvalidOutput"));
            return;
        }

        ClearSecretField();
        _operation.PrepareGenerated();
        ExecuteWorkspace();
    }

    private void CloseOrCancel(object? sender, global::Terminal.Gui.Input.CommandEventArgs args)
    {
        args.Handled = true;
        if (_workspace.CancelCommand.CanExecute(null))
        {
            _workspace.CancelCommand.Execute(null);
            ClearSecretField();
            SetStatus(SetupTerminalText.Get("Cancelled"));
            return;
        }

        ClearSecretField();
        _application.RequestStop(this);
    }

    private bool PrepareFileName()
    {
        string fileName = _outputFileName.Text;
        return SetupTerminalFileName.IsSafe(fileName) && _workspace.SetPublicInput(fileName);
    }

    private void ExecuteWorkspace()
    {
        ICommand command = _workspace.ExecuteCommand;
        Guid operationId = Guid.CreateVersion7();
        if (!command.CanExecute(operationId))
        {
            _secret.Clear();
            SetStatus(SetupTerminalText.Get("Unavailable"));
            return;
        }

        SetStatus(SetupTerminalText.Get("Writing"));
        command.Execute(operationId);
    }

    private void WorkspacePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SetupPresentationWorkspace.IsBusy)
            or nameof(SetupPresentationWorkspace.Result)
            or nameof(SetupPresentationWorkspace.IsTerminated))
            _application.Invoke(_ => UpdateFromWorkspace());
    }

    private void CommandCanExecuteChanged(object? sender, EventArgs args) =>
        _application.Invoke(_ => RefreshCommandState());

    private void UpdateFromWorkspace()
    {
        RefreshCommandState();
        if (_workspace.Result is not SetupTerminalArtifactResult result)
            return;

        ExitCode = result switch
        {
            { Written: true, Readiness: DotenvReadinessState.Ready } => 0,
            { Written: true, Readiness: DotenvReadinessState.Incomplete } => 3,
            _ => 4
        };
        SetStatus(SetupTerminalText.FormatResult(result));
    }

    private void RefreshCommandState()
    {
        bool idle = !_workspace.IsBusy && !_workspace.IsTerminated;
        _saveManualButton.Enabled = idle
            && SetupTerminalFileName.IsSafe(_outputFileName.Text)
            && _secret.Count > 0;
        _generateButton.Enabled = idle && SetupTerminalFileName.IsSafe(_outputFileName.Text);
        _closeButton.Text = _workspace.IsBusy
            ? SetupTerminalText.Get("Cancel")
            : SetupTerminalText.Get("Close");
    }

    private void ClearSecretField()
    {
        _secretField.ClearSensitiveState();
        _secret.Clear();
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.SetNeedsDraw();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _workspace.PropertyChanged -= WorkspacePropertyChanged;
            _workspace.ExecuteCommand.CanExecuteChanged -= CommandCanExecuteChanged;
            _workspace.CancelCommand.CanExecuteChanged -= CommandCanExecuteChanged;
            ViewportChanged -= TerminalViewportChanged;
            _outputFileName.TextChanging -= ValidateOutputName;
            _secretField.ValueChanged -= SecretFieldValueChanged;
            _secretField.InputRejected -= SecretInputRejected;
            _secretField.SensitiveCommandBlocked -= SecretCommandBlocked;
            _saveManualButton.Accepting -= SaveManual;
            _generateButton.Accepting -= Generate;
            _closeButton.Accepting -= CloseOrCancel;
            foreach (Button button in _navigationButtons)
                button.Dispose();
            _navigationButtons.Clear();
            ClearSecretField();
        }

        base.Dispose(disposing);
    }
}
