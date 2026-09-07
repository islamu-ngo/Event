namespace ISLAMU.Event.SetupAssistant.Terminal;

using System.Security.Cryptography;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.SetupAssistant.Presentation;

internal enum SetupTerminalSecretMode
{
    None,
    Manual,
    Generated
}

internal sealed record SetupTerminalArtifactResult(
    bool Written,
    string DiagnosticCode,
    string? Digest,
    DotenvReadinessState Readiness,
    int MissingCount,
    int BlockedCount)
{
    public override string ToString() =>
        $"{nameof(SetupTerminalArtifactResult)}:{DiagnosticCode}:Readiness={Readiness}:Missing={MissingCount}:Blocked={BlockedCount}";
}

internal sealed class SetupTerminalArtifactOperation(
    Func<string> outputFileName,
    SetupTerminalSecretBuffer secret,
    SetupTerminalProtectedWriter protectedWriter) : ISetupPresentationOperation
{
    private static ReadOnlySpan<byte> Placeholder => "ISLAMU_SETUP_SECRET_PLACEHOLDER"u8;
    private readonly object _gate = new();
    private SetupTerminalSecretMode _mode;

    internal bool PrepareManual()
    {
        lock (_gate)
        {
            if (secret.Count == 0)
                return false;
            _mode = SetupTerminalSecretMode.Manual;
            return true;
        }
    }

    internal void PrepareGenerated()
    {
        lock (_gate)
        {
            secret.Clear();
            _mode = SetupTerminalSecretMode.Generated;
        }
    }

    public async Task<SetupPresentationOutcome> ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetupTerminalSecretMode mode;
            lock (_gate)
            {
                mode = _mode;
                _mode = SetupTerminalSecretMode.None;
            }

            SetupTerminalArtifactResult result = mode switch
            {
                SetupTerminalSecretMode.Manual => await ComposeManualAsync(outputFileName(), cancellationToken),
                SetupTerminalSecretMode.Generated => await ComposeGeneratedAsync(outputFileName(), cancellationToken),
                _ => Failed("terminal-mode-required")
            };
            return new SetupPresentationOutcome(result, ReadOnlyMemory<byte>.Empty);
        }
        finally
        {
            secret.Clear();
        }
    }

    private async Task<SetupTerminalArtifactResult> ComposeManualAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        byte[] transient;
        try
        {
            transient = secret.CopyUtf8Bytes();
        }
        catch (InvalidOperationException)
        {
            return Failed("terminal-secret-unavailable");
        }
        try
        {
            return await ComposeAsync(
                fileName,
                transient,
                DotenvEntryKind.LocalHumanValue,
                DotenvProvenance.UserInput,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transient);
            secret.Clear();
        }
    }

    private async Task<SetupTerminalArtifactResult> ComposeGeneratedAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        using LocalSecretGenerator generator = LocalSecretGenerator.Create();
        using LocalSecretGenerationResult generated = generator.Generate(
            "SETUP_SECRET",
            LocalSecretGenerationProfile.OpaqueUrlSafe256);
        if (!generated.Succeeded)
            return Failed("terminal-generation-failed");
        byte[] transient = generated.Output!.CopyUtf8Bytes();
        try
        {
            return await ComposeAsync(
                fileName,
                transient,
                DotenvEntryKind.GeneratedValueReference,
                DotenvProvenance.Generated,
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(transient);
        }
    }

    private async Task<SetupTerminalArtifactResult> ComposeAsync(
        string fileName,
        byte[] transient,
        DotenvEntryKind kind,
        DotenvProvenance provenance,
        CancellationToken cancellationToken)
    {
        byte[] renderedBytes = [];
        byte[] finalBytes = [];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SetupTerminalFileName.IsSafe(fileName))
                return Failed("terminal-output-name-invalid");
            if (!protectedWriter.IsAvailable)
                return Failed("protected-output-unavailable");

            var context = new EnvironmentActivationContext(
                "standalone",
                ["platform"],
                ["environment", "local", "sqlite"]);
            DotenvCompositionResult composition = DotenvComposer.ComposeWithSecrets(
                CanonicalEnvironmentCatalogue.Catalogue,
                context,
                [new DotenvEntry(
                    "SETUP_SECRET",
                    "ISLAMU_SETUP_SECRET_PLACEHOLDER",
                    kind,
                    true,
                    provenance)]);
            DotenvRenderResult rendered = DotenvCodec.Render(composition.Document, true);
            if (!rendered.Succeeded)
                return FromComposition(false, "terminal-compose-failed", null, composition);

            renderedBytes = rendered.Bytes.ToArray();
            int placeholderIndex = renderedBytes.AsSpan().IndexOf(Placeholder);
            if (placeholderIndex < 0
                || renderedBytes.AsSpan(placeholderIndex + Placeholder.Length).IndexOf(Placeholder) >= 0)
                return FromComposition(false, "terminal-compose-failed", null, composition);
            finalBytes = new byte[renderedBytes.Length - Placeholder.Length + transient.Length];
            renderedBytes.AsSpan(0, placeholderIndex).CopyTo(finalBytes);
            transient.CopyTo(finalBytes.AsSpan(placeholderIndex));
            renderedBytes.AsSpan(placeholderIndex + Placeholder.Length).CopyTo(
                finalBytes.AsSpan(placeholderIndex + transient.Length));
            cancellationToken.ThrowIfCancellationRequested();
            string digest = ArtifactDigest.Compute(finalBytes).Value;
            bool written = await protectedWriter.WriteCreateNewAsync(
                fileName,
                finalBytes,
                DotenvCodec.MaximumFileUtf8Bytes,
                cancellationToken);
            return FromComposition(
                written,
                written ? "terminal-complete" : "protected-output-unavailable",
                digest,
                composition);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            return Failed("terminal-compose-failed");
        }
        finally
        {
            if (renderedBytes.Length > 0)
                CryptographicOperations.ZeroMemory(renderedBytes);
            if (finalBytes.Length > 0)
                CryptographicOperations.ZeroMemory(finalBytes);
        }
    }

    private static SetupTerminalArtifactResult FromComposition(
        bool succeeded,
        string code,
        string? digest,
        DotenvCompositionResult composition) => new(
            succeeded,
            code,
            digest,
            composition.Readiness.State,
            composition.Readiness.Missing.Count,
            composition.Readiness.Blocked.Count);

    private static SetupTerminalArtifactResult Failed(string code) => new(
        false,
        code,
        null,
        DotenvReadinessState.Blocked,
        0,
        1);
}
