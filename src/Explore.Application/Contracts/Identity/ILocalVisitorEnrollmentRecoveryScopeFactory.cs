namespace Explore.Application.Contracts.Identity;

/// <summary>
/// Constructs a fresh Local writer pair for a persisted maintenance target.
/// Candidate metadata does not authorize mutation; the selected store must reread
/// the original receipt and its account evidence before settlement.
/// </summary>
public interface ILocalVisitorEnrollmentRecoveryScopeFactory
{
    ValueTask<ILocalIdentityExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken);
}
