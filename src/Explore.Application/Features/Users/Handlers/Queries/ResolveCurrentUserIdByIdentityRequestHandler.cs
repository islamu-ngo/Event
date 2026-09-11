using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Users.Handlers.Queries;

public class ResolveCurrentUserIdByIdentityRequestHandler
    : IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>
{
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;

    public ResolveCurrentUserIdByIdentityRequestHandler(
        IUserExternalLoginRepository userExternalLoginRepository)
    {
        _userExternalLoginRepository = userExternalLoginRepository;
    }

    public async Task<Guid?> QueryAsync(ResolveCurrentUserIdByIdentityRequest request, CancellationToken cancellationToken)
    {
        AuthenticationProviderKind providerKind =
            request.Provider.ParseAuthenticationProviderKind();
        var accountKey = new ProviderAccountKey(providerKind, request.ProviderId.Trim());

        var externalLogin = await _userExternalLoginRepository.GetByProviderAndKey(accountKey);
        return externalLogin?.UserId;
    }
}
