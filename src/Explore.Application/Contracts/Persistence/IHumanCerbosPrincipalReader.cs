namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads the original authenticated human's current policy inputs on the selected idle owner.
/// This observation supplies no retained commit admission.
/// </summary>
public interface IHumanCerbosPrincipalReader
{
    Task<HumanPrincipalSourceFacts> ReadAsync(Guid userId, CancellationToken cancellationToken);
}
