namespace Explore.Application.Contracts.Identity;

/// <summary>
/// Prepares an existing account's email mirror inside its caller-owned transaction.
/// Preparation captures concurrency evidence without disclosing private profile fields.
/// </summary>
public interface IIdentityEmailMirrorWriter
{
    Task<IPreparedIdentityEmailMirror> PrepareAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Retains one account's original persistence owner until its single email mirror write.
/// Disposal expires this preparation without disposing the caller's context or transaction.
/// </summary>
public interface IPreparedIdentityEmailMirror : IAsyncDisposable
{
    /// <summary>
    /// Applies the mirror once. A null email or verification value preserves that field.
    /// The caller retains identity proof decisions and transaction completion.
    /// </summary>
    Task ApplyAsync(string? replacementEmail, bool? emailVerified, CancellationToken cancellationToken);
}
