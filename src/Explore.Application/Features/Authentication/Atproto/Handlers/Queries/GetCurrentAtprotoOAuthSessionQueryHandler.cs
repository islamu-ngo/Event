using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Application.Features.Authentication.Atproto.Validators;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Queries;

public sealed class GetCurrentAtprotoOAuthSessionQueryHandler(IAtprotoOAuthSecurityGateway securityGateway)
    : IQueryHandler<GetCurrentAtprotoOAuthSessionQuery, AtprotoCurrentOAuthSession?>
{
    public async Task<AtprotoCurrentOAuthSession?> QueryAsync(
        GetCurrentAtprotoOAuthSessionQuery request,
        CancellationToken cancellationToken = default)
    {
        await new AtprotoCurrentSessionIdentityValidator()
            .ValidateAndThrowAsync(request.Identity, cancellationToken).ConfigureAwait(false);
        return await securityGateway.GetCurrentAsync(request.Identity, cancellationToken).ConfigureAwait(false);
    }
}
