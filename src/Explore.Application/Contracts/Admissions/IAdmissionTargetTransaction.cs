namespace Explore.Application.Contracts.Admissions;

/// <summary>
/// Coordinates one tenant target before BEGIN and retains its original owner
/// through one Serializable attempt. This is cooperative ordering, not authorization.
/// </summary>
public interface IAdmissionTargetTransaction
{
    Task<T> ExecuteAsync<T>(Guid tenantId, Guid targetId,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
