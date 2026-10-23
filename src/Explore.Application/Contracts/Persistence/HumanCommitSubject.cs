using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Users.Requests.Queries;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// An immutable authenticated account observation, not a grant. Resolve before selecting the
/// committing scope; that scope must fence the account and its current authority again.
/// </summary>
public sealed class HumanCommitSubject
{
    private HumanCommitSubject(Guid principalId, ProviderAccountKey? accountKey)
    {
        PrincipalId = principalId;
        AccountKey = accountKey;
    }

    public Guid PrincipalId { get; }
    public ProviderAccountKey? AccountKey { get; }

    /// <summary>
    /// Uses the existing account-resolution boundary, including provider binding precedence
    /// and rejection of machine/purpose-bound identities. No audit/request actor is accepted.
    /// </summary>
    public static async Task<HumanCommitSubject?> ResolveAsync(
        ClaimsPrincipal principal,
        IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(identityQuery);
        var captured = new ClaimsPrincipal(principal.Identities.Select(identity => new ClaimsIdentity(identity)));
        var account = captured.GetProviderIdentity()?.AccountKey;
        Guid? user = await identityQuery.ResolveCurrentUserIdAsync(captured, cancellationToken);
        return user is { } id && id != Guid.Empty ? new HumanCommitSubject(id, account) : null;
    }
}
