namespace Explore.Application.Contracts.Secrets;

/// <summary>
/// Reads only the explicitly selected secret authority. Ordinary options, scheduler data
/// and persisted machine bindings cannot supply possession of the maintenance credential.
/// </summary>
public interface IModerationReconciliationSecretProvider
{
    ValueTask<string?> ReadAsync(CancellationToken cancellationToken);
}
