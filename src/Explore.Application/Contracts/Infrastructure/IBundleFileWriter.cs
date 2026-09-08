namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Writes translation bundle files to a writable path that <see cref="OfflineTranslationProvider"/>
/// checks before falling back to embedded resources.
/// <para>
/// The default <c>BundleFileWriter</c> writes to the local filesystem
/// (<c>{ContentRoot}/App_Data/Localization/Bundles/{code}.json</c>), which is correct for
/// single-instance deployments and for multi-instance deployments mounting a shared volume.
/// Multi-replica deployments without shared storage require a future
/// <c>DistributedBundleFileWriter</c> implementation — see
/// <c>dev/backlog/distributed-bundle-file-writer.md</c>.
/// </para>
/// </summary>
public interface IBundleFileWriter
{
    /// <summary>
    /// Writes the given flat translations dictionary to an on-disk bundle file for the given language.
    /// Must be atomic (rename-over-temp) so readers never observe a partial file.
    /// </summary>
    /// <returns>The absolute path of the file that was written.</returns>
    /// <exception cref="BundleWriteException">Thrown when the write fails for any reason.</exception>
    Task<string> WriteBundleAsync(
        string languageCode,
        IReadOnlyDictionary<string, string> translations,
        CancellationToken ct = default);

    /// <summary>
    /// Reports whether the target bundle directory exists and is writable.
    /// The admin UI surfaces this as a health banner and gates the "Export" button.
    /// </summary>
    Task<WritablePathHealth> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Outcome of a writable-path probe: the admin UI shows a red banner when <paramref name="Writable"/> is false.
/// </summary>
public sealed record WritablePathHealth(
    bool Exists,
    bool Writable,
    string? Reason,
    string TargetPath);

/// <summary>
/// Thrown when a bundle write fails (directory creation, serialization, atomic move, IO error).
/// </summary>
public sealed class BundleWriteException : Exception
{
    public BundleWriteException(string message) : base(message) { }
    public BundleWriteException(string message, Exception inner) : base(message, inner) { }
}
