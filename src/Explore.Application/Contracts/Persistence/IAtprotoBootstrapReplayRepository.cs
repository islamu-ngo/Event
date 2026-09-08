namespace Explore.Application.Contracts.Persistence;

public interface IAtprotoBootstrapReplayRepository
{
    Task<bool> TryConsumeAsync(
        string jti,
        Guid tenantId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}
