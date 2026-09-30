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

    [ObservableProperty]
    private string _environmentFileName = ".env.setup";

    [ObservableProperty]
    private string _environmentStatus = "environment-not-prepared";

    [ObservableProperty]
    private string _identityFileName = "operator-identity.json";

    [ObservableProperty]
    private string _identityStatus = "identity-input-required";

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
    private string _manifestStatus = "manifest-not-prepared";

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
        InvalidateEnvironment("environment-not-prepared");
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
                EnvironmentStatus = "environment-invalid";
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
            EnvironmentStatus = "environment-invalid";
            return false;
        }
    }

    public bool PrepareManifest()
    {
        ThrowIfDisposed();
        InvalidateManifest("manifest-not-prepared");
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
            IdentityStatus = "identity-invalid";
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
            IdentityStatus = "identity-invalid";
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
            SetupArtifactKind.Environment,
            EnvironmentFileName,
            _environmentWorkspace,
            _environmentOperation,
            GetEnvironmentPreparation,
            revision => revision == _environmentRevision,
            status => EnvironmentStatus = status,
            value => IsEnvironmentSaving = value,
            NotifyEnvironmentSaveChanged,
            cancellationToken);

    public Task<ProtectedArtifactStatus> SaveManifestAsync(
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            SetupArtifactKind.Configuration,
            ManifestFileName,
            _manifestWorkspace,
            _manifestOperation,
            GetManifestPreparation,
            revision => revision == _manifestRevision,
            status => ManifestStatus = status,
            value => IsManifestSaving = value,
            NotifyManifestSaveChanged,
            cancellationToken);

    public Task<ProtectedArtifactStatus> SaveIdentityAsync(
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            SetupArtifactKind.OperatorIdentity,
            IdentityFileName,
            _identityWorkspace,
            _identityOperation,
            GetIdentityPreparation,
            revision => revision == _identityRevision,
            status => IdentityStatus = status,
            value => IsIdentitySaving = value,
            NotifyIdentitySaveChanged,
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
        IdentityStatus = "identity-input-required";
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

    partial void OnEnvironmentFileNameChanged(string value) =>
        InvalidateEnvironment("environment-not-prepared");

    partial void OnIdentityFileNameChanged(string value) =>
        InvalidateIdentityDraft();

    partial void OnManifestFileNameChanged(string value) =>
        InvalidateManifest("manifest-not-prepared");

    partial void OnManifestSourceNameChanged(string value) =>
        InvalidateManifest("manifest-not-prepared");

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

    private async Task<ProtectedArtifactStatus> SaveAsync(
        SetupArtifactKind kind,
        string fileName,
        SetupPresentationWorkspace workspace,
        DesktopProtectedSaveOperation operation,
        Func<(byte[] Bytes, long Revision)> takePreparation,
        Func<long, bool> isCurrent,
        Action<string> setStatus,
        Action<bool> setSaving,
        Action notifySaveChanged,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        (byte[] bytes, long revision) = takePreparation();
        if (bytes.Length == 0)
            return ProtectedArtifactStatus.InvalidRequest;

        setSaving(true);
        notifySaveChanged();
        try
        {
            if (!ProtectedSaveAvailable)
            {
                if (isCurrent(revision))
                    setStatus("save-failed:UnsupportedPlatform");
                return ProtectedArtifactStatus.UnsupportedPlatform;
            }
            if (!IsSafeFileName(fileName))
            {
                if (isCurrent(revision))
                    setStatus("save-failed:UnsafeTarget");
                return ProtectedArtifactStatus.UnsafeTarget;
            }

            operation.Prepare(kind, Path.Combine(_outputDirectory, fileName), bytes);
            await workspace.ExecuteAsync(Guid.CreateVersion7()).WaitAsync(cancellationToken);
            if (!isCurrent(revision))
                return ProtectedArtifactStatus.InvalidRequest;

            ProtectedArtifactStatus result = workspace.Result is DesktopProtectedSaveResult settled
                ? settled.Status
                : ProtectedArtifactStatus.InvalidRequest;
            setStatus(result == ProtectedArtifactStatus.Written
                ? "save-complete"
                : $"save-failed:{result}");
            return result;
        }
        catch (OperationCanceledException)
        {
            workspace.Cancel();
            if (isCurrent(revision))
                setStatus("save-cancelled");
            return ProtectedArtifactStatus.InvalidRequest;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            operation.Clear();
            setSaving(false);
            notifySaveChanged();
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
