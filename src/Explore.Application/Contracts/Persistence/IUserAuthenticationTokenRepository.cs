using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IUserAuthenticationTokenRepository : IGenericRepository<UserAuthenticationToken, Guid>
{
    Task<IReadOnlyList<UserAuthenticationToken>> GetAtprotoSessionsForReadAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        CancellationToken cancellationToken = default);

    Task<UserAuthenticationToken?> GetAtprotoSessionForReadAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid,
        CancellationToken cancellationToken = default);

    Task<UserAuthenticationToken?> GetAtprotoSessionForUpdateAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid,
        CancellationToken cancellationToken = default);

    Task DeleteAtprotoSessionAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string subjectDid,
        CancellationToken cancellationToken = default);

    Task<UserAuthenticationToken> CreateAtprotoSessionAsync(
        UserAuthenticationToken session,
        CancellationToken cancellationToken = default);

    Task UpdateAtprotoSessionAsync(
        UserAuthenticationToken session,
        CancellationToken cancellationToken = default);

    Task<UserAuthenticationToken?> GetByUserAndProvider(Guid userId, string provider, CancellationToken cancellationToken = default);
    Task<List<UserAuthenticationToken>> GetByUser(Guid userId, CancellationToken cancellationToken = default);
    Task<UserAuthenticationToken?> GetByIdForUser(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<UserAuthenticationToken?> GetUserAuthenticationTokenWithDetailsForUser(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<List<UserAuthenticationToken>> GetUserAuthenticationTokensWithDetailsForUser(Guid userId, CancellationToken cancellationToken = default);
}
