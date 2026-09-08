using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Contracts.Infrastructure;

public interface IAtprotoOAuthSecurityGateway
{
    Task<AtprotoOAuthVerificationResult> VerifyAsync(
        AtprotoOAuthVerificationInput request,
        CancellationToken cancellationToken);

    Task<AtprotoPreparedOAuthSession> PreparePersistenceAsync(
        AtprotoVerifiedOAuthSession verifiedSession,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken);

    Task PersistPreparedAsync(
        AtprotoPreparedOAuthSession preparedSession,
        CancellationToken cancellationToken);

    Task<AtprotoCurrentOAuthSession?> GetCurrentAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken);

    Task<AtprotoOAuthRefreshResult> RefreshAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken);

    Task<AtprotoSessionRevocationResult> RevokeCurrentAsync(
        AtprotoCurrentSessionIdentity identity,
        CancellationToken cancellationToken);
}

public interface IAtprotoSessionTokenIssuer
{
    Task<AtprotoIssuedSessionToken> IssueAsync(
        Guid userId,
        Guid tenantId,
        AtprotoDid did,
        CancellationToken cancellationToken);
}
