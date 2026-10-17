namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Captures only the two actual accounting actions before their selected owner begins.
/// This is not a settings, import, provider, or generic database mutation capability.
/// </summary>
public interface IStorageAccountingOriginalAuthority
{
    Task<IStorageAccountingOriginalAdmission> PrepareAsync(
        object command, CancellationToken cancellationToken);
}

/// <summary>
/// Single-attempt source custody. The accounting repository retains physical ownership
/// and invokes all source checks before terminal time validation.
/// </summary>
public interface IStorageAccountingOriginalAdmission : IDisposable
{
    Task AcquireAsync(object command, CancellationToken cancellationToken);
    Task ValidateSourcesAsync(CancellationToken cancellationToken);
    void ValidateTime();
}

/// <summary>Captures the actual idle accounting owner, not caller-supplied transaction evidence.</summary>
public interface IStorageAccountingOriginalOwner
{
    IStorageAccountingOwnerLease Capture();
}

public interface IStorageAccountingOwnerLease
{
    void Acquire();
    void Validate();
}
