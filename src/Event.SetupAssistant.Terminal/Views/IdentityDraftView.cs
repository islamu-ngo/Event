namespace ISLAMU.Event.SetupAssistant.Terminal.Views;

using System.Security.Cryptography;
using System.Text.Json;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using global::Terminal.Gui.Input;
using global::Terminal.Gui.ViewBase;
using global::Terminal.Gui.Views;

internal sealed class IdentityDraftView : View
{
    private readonly string _baseDirectory;
    private readonly SetupTerminalSecretBuffer _identityInput =
        new(OperatorIdentityManifestJson.MaximumBytes, urlSafeOnly: false);
    private readonly SetupSecretTextField _document;
    private readonly TextField _fileName;
    private readonly Label _status;
    private readonly ProtectedArtifactWriter _writer;
    private byte[] _manifestBytes = [];

    internal IdentityDraftView(ProtectedArtifactWriter writer, string baseDirectory)
    {
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
            Height = 2,
            Text = SetupTerminalText.Get("IdentityHeading")
        };
        var documentLabel = new Label
        {
            X = 0,
            Y = 3,
            Text = SetupTerminalText.Get("IdentityDocument")
        };
        _document = new SetupSecretTextField(_identityInput)
        {
            X = 17,
            Y = 3,
            Width = Dim.Fill()
        };
        var fileLabel = new Label { X = 0, Y = 5, Text = SetupTerminalText.Get("OutputFile") };
        _fileName = new TextField
        {
            X = 17,
            Y = 5,
            Width = Dim.Fill(),
            Text = "operator-identity.json"
        };
        var validate = new Button
        {
            X = 0,
            Y = 7,
            Text = SetupTerminalText.Get("ValidateDraft")
        };
        var save = new Button
        {
            X = Pos.Right(validate) + 1,
            Y = 7,
            Text = SetupTerminalText.Get("SaveProtected")
        };
        _status = new Label
        {
            X = 0,
            Y = 9,
            Width = Dim.Fill(),
            Height = 3,
            Text = SetupTerminalText.Get("IdentityPrivateNotice")
        };

        validate.Accepting += ValidateAccepted;
        save.Accepting += SaveAccepted;
        _document.ValueChanged += (_, _) =>
        {
            ClearPreparedBytes();
            _status.Text = SetupTerminalText.Get("IdentityPrivateNotice");
            _status.SetNeedsDraw();
        };
        _document.SensitiveCommandBlocked += (_, _) =>
        {
            _status.Text = SetupTerminalText.Get("IdentityPrivateNotice");
            _status.SetNeedsDraw();
        };
        _document.InputRejected += (_, _) =>
        {
            ClearPreparedBytes();
            _status.Text = SetupTerminalText.Get("IdentityInvalid");
            _status.SetNeedsDraw();
        };
        Add(heading, documentLabel, _document, fileLabel, _fileName, validate, save, _status);
    }

    internal string Document
    {
        get => _document.Text;
        set => _document.TryReplaceSensitiveInput(value);
    }

    internal string FileName
    {
        get => _fileName.Text;
        set => _fileName.Text = value;
    }

    internal ReadOnlyMemory<byte> ManifestBytes => new((byte[])_manifestBytes.Clone());
    internal string Status => _status.Text.ToString() ?? string.Empty;

    internal bool ValidateDraft()
    {
        ClearPreparedBytes();
        if (_identityInput.Count == 0)
        {
            _status.Text = SetupTerminalText.Get("IdentityInvalid");
            _status.SetNeedsDraw();
            return false;
        }
        try
        {
            byte[] documentBytes = _identityInput.CopyUtf8Bytes();
            try
            {
                using JsonDocument parsed = JsonDocument.Parse(
                    documentBytes,
                    new JsonDocumentOptions { MaxDepth = 4 });
                OperatorIdentityManifest manifest =
                    OperatorIdentityManifestJson.Create(parsed.RootElement);
                _manifestBytes = OperatorIdentityManifestCodec.Write(manifest);
                _status.Text = $"{SetupTerminalText.Get("IdentityValid")} {manifest.ContentDigest}";
                _status.SetNeedsDraw();
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(documentBytes);
            }
        }
        catch (Exception exception) when (exception is JsonException
            or OperatorIdentityManifestException)
        {
            _status.Text = SetupTerminalText.Get("IdentityInvalid");
            _status.SetNeedsDraw();
            return false;
        }
    }

    internal async Task<ProtectedArtifactStatus> SaveProtectedAsync(
        CancellationToken cancellationToken = default)
    {
        if (_manifestBytes.Length == 0 && !ValidateDraft())
            return ProtectedArtifactStatus.InvalidRequest;
        if (!SetupTerminalFileName.IsSafe(_fileName.Text))
        {
            _status.Text = SetupTerminalText.Get("ProtectedDraftFailed");
            _status.SetNeedsDraw();
            return ProtectedArtifactStatus.InvalidRequest;
        }

        using ProtectedArtifactPreparation preparation = await _writer.PrepareAsync(
            SetupArtifactKind.OperatorIdentity,
            Path.Combine(_baseDirectory, _fileName.Text),
            _manifestBytes,
            cancellationToken);
        ProtectedArtifactStatus result = await preparation.CommitAsync(cancellationToken);
        _status.Text = result == ProtectedArtifactStatus.Written
            ? SetupTerminalText.Get("ProtectedDraftSaved")
            : SetupTerminalText.Get("ProtectedDraftFailed");
        _status.SetNeedsDraw();
        return result;
    }

    internal void ClearPrivateState()
    {
        _document.ClearSensitiveState();
        ClearPreparedBytes();
        _status.Text = SetupTerminalText.Get("IdentityPrivateNotice");
        _status.SetNeedsDraw();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearPrivateState();
            _identityInput.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ClearPreparedBytes()
    {
        if (_manifestBytes.Length > 0)
            CryptographicOperations.ZeroMemory(_manifestBytes);
        _manifestBytes = [];
    }

    private void ValidateAccepted(object? sender, CommandEventArgs args)
    {
        args.Handled = true;
        ValidateDraft();
    }

    private void SaveAccepted(object? sender, CommandEventArgs args)
    {
        args.Handled = true;
        SaveProtectedAsync().GetAwaiter().GetResult();
    }
}
