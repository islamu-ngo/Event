namespace ISLAMU.Event.SetupAssistant.Desktop.Files;

public enum ProtectedWriteDisposition
{
    Written,
    Rejected,
    Unsupported
}

public enum ProtectedWriteFailureCode
{
    None,
    UnsupportedPlatform,
    InvalidRequest,
    TargetExists,
    UnsafeTarget,
    TargetChanged,
    PermissionDenied,
    PermissionVerificationFailed,
    IoFailure
}

public sealed class ProtectedWriteRequest
{
    public ProtectedWriteRequest(
        string targetPath,
        ReadOnlyMemory<byte> bytes,
        bool allowOverwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        TargetPath = targetPath;
        Bytes = bytes;
        AllowOverwrite = allowOverwrite;
    }

    public bool AllowOverwrite { get; }

    public ReadOnlyMemory<byte> Bytes { get; }

    public string TargetPath { get; }

    public override string ToString() => nameof(ProtectedWriteRequest);
}

public sealed class ProtectedWriteResult
{
    internal ProtectedWriteResult(
        ProtectedWriteDisposition disposition,
        ProtectedWriteFailureCode failureCode)
    {
        Disposition = disposition;
        FailureCode = failureCode;
    }

    public ProtectedWriteDisposition Disposition { get; }

    public ProtectedWriteFailureCode FailureCode { get; }

    internal static ProtectedWriteResult Written() =>
        new(ProtectedWriteDisposition.Written, ProtectedWriteFailureCode.None);

    internal static ProtectedWriteResult Rejected(ProtectedWriteFailureCode code) =>
        new(ProtectedWriteDisposition.Rejected, code);

    internal static ProtectedWriteResult Unsupported() =>
        new(
            ProtectedWriteDisposition.Unsupported,
            ProtectedWriteFailureCode.UnsupportedPlatform);

    public override string ToString() => nameof(ProtectedWriteResult);
}

public interface IProtectedFileWriter
{
    bool IsAvailable { get; }

    Task<ProtectedWritePreparation> PrepareAsync(
        ProtectedWriteRequest request,
        CancellationToken cancellationToken);
}
