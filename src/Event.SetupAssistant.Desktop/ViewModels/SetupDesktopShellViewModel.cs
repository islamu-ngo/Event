namespace ISLAMU.Event.SetupAssistant.Desktop.ViewModels;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.SetupAssistant.Presentation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed partial class SetupDesktopShellViewModel : ObservableObject, IDisposable
{
    private readonly object _gate = new();
    private readonly DesktopProtectedSaveOperation _environmentOperation;
    private readonly SetupPresentationWorkspace _environmentWorkspace;
    private readonly DesktopProtectedSaveOperation _identityOperation;
    private readonly SetupPresentationWorkspace _identityWorkspace;
    private readonly DesktopProtectedSaveOperation _manifestOperation;
    private readonly SetupPresentationWorkspace _manifestWorkspace;
    private readonly string _outputDirectory;
    private readonly SetupPresentationSession _session;
    private byte[] _environmentBytes = [];
    private long _environmentRevision;
    private byte[] _identityBytes = [];
    private long _identityRevision;
    private byte[] _manifestBytes = [];
    private long _manifestRevision;
    private bool _disposed;

    private const string EnvironmentNotPreparedStatus = "environment-not-prepared";
    private const string EnvironmentInvalidStatus = "environment-invalid";
    private const string ManifestNotPreparedStatus = "manifest-not-prepared";
    private const string IdentityInputRequiredStatus = "identity-input-required";
    private const string IdentityInvalidStatus = "identity-invalid";

    private string _environmentFileName = ".env.setup";
    public string EnvironmentFileName
    {
        get => _environmentFileName;
        set
        {
            if (SetProperty(ref _environmentFileName, value))
            {
                OnEnvironmentFileNameChanged(value);
            }
        }
    }

    private string _environmentStatus = EnvironmentNotPreparedStatus;
    public string EnvironmentStatus
    {
        get => _environmentStatus;
        set => SetProperty(ref _environmentStatus, value);
    }

    private string _identityFileName = "operator-identity.json";
    public string IdentityFileName
    {
        get => _identityFileName;
        set
        {
            if (SetProperty(ref _identityFileName, value))
            {
                OnIdentityFileNameChanged(value);
            }
        }
    }

    private string _identityStatus = IdentityInputRequiredStatus;
    public string IdentityStatus
    {
        get => _identityStatus;
        set => SetProperty(ref _identityStatus, value);
    }

    [ObservableProperty]
    private bool _isEnvironmentSaving;

    [ObservableProperty]
    private bool _isIdentitySaving;

    [ObservableProperty]
    private bool _isManifestSaving;

    [ObservableProperty]
    private string _manifestFileName = "configuration-manifest.json";

    [ObservableProperty]
    private string _manifestSourceName = "event-setup";

    [ObservableProperty]
    private string _manifestStatus = ManifestNotPreparedStatus;

    public SetupDesktopShellViewModel()
        : this(CreateNativeSave(), Directory.GetCurrentDirectory(),
            new ProtectedArtifactWriter().IsAvailable)
    {
    }

    internal SetupDesktopShellViewModel(
        Func<SetupArtifactKind, string, ReadOnlyMemory<byte>, CancellationToken,
            Task<ProtectedArtifactStatus>> save,
        string outputDirectory,
        bool protectedOutputAvailable)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _outputDirectory = outputDirectory;
        ProtectedSaveAvailable = protectedOutputAvailable;
        _session = new SetupPresentationSession(new StrongReferenceMessenger());
        _environmentOperation = new DesktopProtectedSaveOperation(save);
        _manifestOperation = new DesktopProtectedSaveOperation(save);
        _identityOperation = new DesktopProtectedSaveOperation(save);
        _environmentWorkspace = CreateWorkspace("environment", _environmentOperation);
        _manifestWorkspace = CreateWorkspace("manifest", _manifestOperation);
        _identityWorkspace = CreateWorkspace("identity-draft", _identityOperation);
    }

    public bool CanSaveEnvironment =>
        ProtectedSaveAvailable && !IsEnvironmentSaving && _environmentBytes.Length > 0;

    public bool CanSaveIdentity =>
        ProtectedSaveAvailable && !IsIdentitySaving && _identityBytes.Length > 0;

    public bool CanSaveManifest =>
        ProtectedSaveAvailable && !IsManifestSaving && _manifestBytes.Length > 0;

    public bool ProtectedSaveAvailable { get; }

    public bool PrepareEnvironment()
    {
        ThrowIfDisposed();
        InvalidateEnvironment(EnvironmentNotPreparedStatus);
        try
        {
            var context = new EnvironmentActivationContext(
                "standalone",
                ["database", "platform", "storage"],
                ["environment", "local", "sqlite"]);
            DotenvCompositionResult composition = DotenvComposer.ComposeNoSecrets(
                PlatformEnvironmentCatalogue.Catalogue,
                context,
                [new DotenvEntry("DATABASE_PROVIDER", "Sqlite",
                    DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput)]);
            DotenvRenderResult rendered = DotenvCodec.Render(composition.Document, finalNewline: true);
            if (!rendered.Succeeded)
            {
                EnvironmentStatus = EnvironmentInvalidStatus;
                return false;
            }

            lock (_gate)
                _environmentBytes = rendered.Bytes.ToArray();
            EnvironmentStatus = $"environment-ready:{ArtifactDigest.Compute(_environmentBytes).Value}";
            NotifyEnvironmentSaveChanged();
            return true;
        }
        catch (ArgumentException)
        {
            EnvironmentStatus = EnvironmentInvalidStatus;
            return false;
        }
    }

    public bool PrepareManifest()
    {
        ThrowIfDisposed();
        InvalidateManifest(ManifestNotPreparedStatus);
        OfflinePortabilityResult created = OfflinePortabilityWorkflow.CreateManifest(
            Profile(),
            Selection(),
            ManifestSourceName,
            null,
            null);
        if (!created.Succeeded)
        {
            ManifestStatus = Diagnostic("manifest-invalid", created.Diagnostics);
            return false;
        }

        OfflinePortabilityResult validated = OfflinePortabilityWorkflow.Validate(created.Document!);
        if (!validated.Succeeded)
        {
            ManifestStatus = Diagnostic("manifest-invalid", validated.Diagnostics);
            return false;
        }

        OfflinePortabilityFormatResult formatted = OfflinePortabilityWorkflow.Format(validated.Document!);
        if (!formatted.Succeeded)
        {
            ManifestStatus = Diagnostic("manifest-invalid", formatted.Diagnostics);
            return false;
        }

        lock (_gate)
            _manifestBytes = formatted.Output!.Bytes.ToArray();
        ManifestStatus = $"manifest-ready:{formatted.Output.Digest.Value}";
        NotifyManifestSaveChanged();
        return true;
    }

    public bool PrepareIdentityDraft(string document)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(document);
        InvalidateIdentityDraft();
        if (Encoding.UTF8.GetByteCount(document) > OperatorIdentityManifestJson.MaximumBytes)
        {
            IdentityStatus = IdentityInvalidStatus;
            return false;
        }
        byte[] source = Encoding.UTF8.GetBytes(document);
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(
                source,
                new JsonDocumentOptions { MaxDepth = 4 });
            OperatorIdentityManifest manifest =
                OperatorIdentityManifestJson.Create(parsed.RootElement);
            lock (_gate)
                _identityBytes = OperatorIdentityManifestCodec.Write(manifest);
            IdentityStatus = $"identity-ready:{manifest.ContentDigest}";
            NotifyIdentitySaveChanged();
            return true;
        }
        catch (Exception exception) when (exception is JsonException
            or OperatorIdentityManifestException)
        {
            IdentityStatus = IdentityInvalidStatus;
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(source);
        }
    }

    public Task<ProtectedArtifactStatus> SaveEnvironmentAsync(
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            new DesktopSaveContext(
                SetupArtifactKind.Environment,
                EnvironmentFileName,
                _environmentWorkspace,
                _environmentOperation,
                GetEnvironmentPreparation,
                revision => revision == _environmentRevision,
                status => EnvironmentStatus = status,
                value => IsEnvironmentSaving = value,
                NotifyEnvironmentSaveChanged),
            cancellationToken);

    public Task<ProtectedArtifactStatus> SaveManifestAsync(
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            new DesktopSaveContext(
                SetupArtifactKind.Configuration,
                ManifestFileName,
                _manifestWorkspace,
                _manifestOperation,
                GetManifestPreparation,
                revision => revision == _manifestRevision,
                status => ManifestStatus = status,
                value => IsManifestSaving = value,
                NotifyManifestSaveChanged),
            cancellationToken);

    public Task<ProtectedArtifactStatus> SaveIdentityAsync(
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            new DesktopSaveContext(
                SetupArtifactKind.OperatorIdentity,
                IdentityFileName,
                _identityWorkspace,
                _identityOperation,
                GetIdentityPreparation,
                revision => revision == _identityRevision,
                status => IdentityStatus = status,
                value => IsIdentitySaving = value,
                NotifyIdentitySaveChanged),
            cancellationToken);

    public void InvalidateIdentityDraft()
    {
        if (_disposed)
            return;
        _identityWorkspace.Cancel();
        lock (_gate)
        {
            _identityRevision++;
            Zero(ref _identityBytes);
        }
        IdentityStatus = IdentityInputRequiredStatus;
        NotifyIdentitySaveChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_gate)
        {
            Zero(ref _environmentBytes);
            Zero(ref _manifestBytes);
            Zero(ref _identityBytes);
        }
        _environmentWorkspace.Dispose();
        _manifestWorkspace.Dispose();
        _identityWorkspace.Dispose();
        _environmentOperation.Dispose();
        _manifestOperation.Dispose();
        _identityOperation.Dispose();
        _session.Dispose();
    }

    public override string ToString() =>
        $"{nameof(SetupDesktopShellViewModel)}:ProtectedSaveAvailable={ProtectedSaveAvailable}";

    private void OnEnvironmentFileNameChanged(string value) =>
        InvalidateEnvironment(EnvironmentNotPreparedStatus);

    private void OnIdentityFileNameChanged(string value) =>
        InvalidateIdentityDraft();

    partial void OnManifestFileNameChanged(string value) =>
        InvalidateManifest(ManifestNotPreparedStatus);

    partial void OnManifestSourceNameChanged(string value) =>
        InvalidateManifest(ManifestNotPreparedStatus);

    private static Func<SetupArtifactKind, string, ReadOnlyMemory<byte>, CancellationToken,
        Task<ProtectedArtifactStatus>> CreateNativeSave()
    {
        var writer = new ProtectedArtifactWriter();
        return async (kind, path, bytes, cancellationToken) =>
        {
            using ProtectedArtifactPreparation preparation =
                await writer.PrepareAsync(kind, path, bytes, cancellationToken);
            return await preparation.CommitAsync(cancellationToken);
        };
    }

    private SetupPresentationWorkspace CreateWorkspace(
        string identifier,
        ISetupPresentationOperation operation)
    {
        if (!SetupWorkspaceId.TryCreate(identifier, out SetupWorkspaceId workspaceId))
            throw new InvalidOperationException("desktop-workspace-invalid");
        SetupPresentationWorkspace workspace = _session.CreateWorkspace(workspaceId, operation);
        workspace.Activate();
        return workspace;
    }

    private sealed record DesktopSaveContext(
        SetupArtifactKind Kind,
        string FileName,
        SetupPresentationWorkspace Workspace,
        DesktopProtectedSaveOperation Operation,
        Func<(byte[] Bytes, long Revision)> TakePreparation,
        Func<long, bool> IsCurrent,
        Action<string> SetStatus,
        Action<bool> SetSaving,
        Action NotifySaveChanged);

    private async Task<ProtectedArtifactStatus> SaveAsync(
        DesktopSaveContext context,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        (byte[] bytes, long revision) = context.TakePreparation();
        if (bytes.Length == 0)
            return ProtectedArtifactStatus.InvalidRequest;

        context.SetSaving(true);
        context.NotifySaveChanged();
        try
        {
            if (!ProtectedSaveAvailable)
            {
                if (context.IsCurrent(revision))
                    context.SetStatus("save-failed:UnsupportedPlatform");
                return ProtectedArtifactStatus.UnsupportedPlatform;
            }
            if (!IsSafeFileName(context.FileName))
            {
                if (context.IsCurrent(revision))
                    context.SetStatus("save-failed:UnsafeTarget");
                return ProtectedArtifactStatus.UnsafeTarget;
            }

            context.Operation.Prepare(context.Kind, Path.Combine(_outputDirectory, context.FileName), bytes);
            await context.Workspace.ExecuteAsync(Guid.CreateVersion7()).WaitAsync(cancellationToken);
            if (!context.IsCurrent(revision))
                return ProtectedArtifactStatus.InvalidRequest;

            ProtectedArtifactStatus result = context.Workspace.Result is DesktopProtectedSaveResult settled
                ? settled.Status
                : ProtectedArtifactStatus.InvalidRequest;
            context.SetStatus(result == ProtectedArtifactStatus.Written
                ? "save-complete"
                : $"save-failed:{result}");
            return result;
        }
        catch (OperationCanceledException)
        {
            context.Workspace.Cancel();
            if (context.IsCurrent(revision))
                context.SetStatus("save-cancelled");
            return ProtectedArtifactStatus.InvalidRequest;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            context.Operation.Clear();
            context.SetSaving(false);
            context.NotifySaveChanged();
        }
    }

    private (byte[] Bytes, long Revision) GetEnvironmentPreparation()
    {
        lock (_gate)
        {
            byte[] bytes = _environmentBytes;
            _environmentBytes = [];
            return (bytes, _environmentRevision);
        }
    }

    private (byte[] Bytes, long Revision) GetManifestPreparation()
    {
        lock (_gate)
        {
            byte[] bytes = _manifestBytes;
            _manifestBytes = [];
            return (bytes, _manifestRevision);
        }
    }

    private (byte[] Bytes, long Revision) GetIdentityPreparation()
    {
        lock (_gate)
        {
            byte[] bytes = _identityBytes;
            _identityBytes = [];
            return (bytes, _identityRevision);
        }
    }

    private void InvalidateEnvironment(string status)
    {
        if (_disposed)
            return;
        _environmentWorkspace.Cancel();
        lock (_gate)
        {
            _environmentRevision++;
            Zero(ref _environmentBytes);
        }
        EnvironmentStatus = status;
        NotifyEnvironmentSaveChanged();
    }

    private void InvalidateManifest(string status)
    {
        if (_disposed)
            return;
        _manifestWorkspace.Cancel();
        lock (_gate)
        {
            _manifestRevision++;
            Zero(ref _manifestBytes);
        }
        ManifestStatus = status;
        NotifyManifestSaveChanged();
    }

    private void NotifyEnvironmentSaveChanged() =>
        OnPropertyChanged(nameof(CanSaveEnvironment));

    private void NotifyManifestSaveChanged() =>
        OnPropertyChanged(nameof(CanSaveManifest));

    private void NotifyIdentitySaveChanged() =>
        OnPropertyChanged(nameof(CanSaveIdentity));

    private static string Diagnostic(
        string fallback,
        IReadOnlyList<SetupDiagnostic> diagnostics) =>
        diagnostics.Count == 0 ? fallback : $"{fallback}:{diagnostics[0].Code.Value}";

    private static bool IsSafeFileName(string value) =>
        value.Length is > 0 and <= 128
        && value is not "-" and not "." and not ".."
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or '-');

    private static SetupProfile Profile() => new(
        new SetupProfileIdentity("event-setup"),
        [],
        [new SetupTopologyKey("standalone")]);

    private static SetupSelection Selection() => new(
        SetupScope.Instance,
        ConfigurationImportApplyMode.PreviewOnly,
        [
            new PortableSectionKey("instance.settings"),
            new PortableSectionKey("instance.documents"),
            new PortableSectionKey("instance.legal_documents")
        ]);

    private static void Zero(ref byte[] bytes)
    {
        if (bytes.Length > 0)
            CryptographicOperations.ZeroMemory(bytes);
        bytes = [];
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal sealed record DesktopProtectedSaveResult(ProtectedArtifactStatus Status)
{
    public override string ToString() =>
        $"{nameof(DesktopProtectedSaveResult)}:{Status}";
}

internal sealed class DesktopProtectedSaveOperation(
    Func<SetupArtifactKind, string, ReadOnlyMemory<byte>, CancellationToken,
        Task<ProtectedArtifactStatus>> save) : ISetupPresentationOperation, IDisposable
{
    private readonly object _gate = new();
    private DesktopSaveRequest? _request;

    internal void Prepare(SetupArtifactKind kind, string path, ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            ClearRequest();
            _request = new DesktopSaveRequest(kind, path, bytes.ToArray());
        }
    }

    public async Task<SetupPresentationOutcome> ExecuteAsync(CancellationToken cancellationToken)
    {
        DesktopSaveRequest request;
        lock (_gate)
        {
            request = _request ?? throw new InvalidOperationException("desktop-save-not-prepared");
            _request = null;
        }

        try
        {
            ProtectedArtifactStatus result =
                await save(request.Kind, request.Path, request.Bytes, cancellationToken);
            return new SetupPresentationOutcome(
                new DesktopProtectedSaveResult(result),
                ReadOnlyMemory<byte>.Empty);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(request.Bytes);
        }
    }

    internal void Clear()
    {
        lock (_gate)
            ClearRequest();
    }

    public void Dispose() => Clear();

    private void ClearRequest()
    {
        if (_request is not null)
            CryptographicOperations.ZeroMemory(_request.Bytes);
        _request = null;
    }

    private sealed record DesktopSaveRequest(
        SetupArtifactKind Kind,
        string Path,
        byte[] Bytes);
}
