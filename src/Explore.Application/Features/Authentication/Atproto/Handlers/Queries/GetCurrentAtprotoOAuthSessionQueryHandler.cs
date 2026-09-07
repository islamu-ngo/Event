using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Application.Features.Authentication.Atproto.Validators;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Queries;

public sealed class GetCurrentAtprotoOAuthSessionQueryHandler(IAtprotoOAuthSecurityGateway securityGateway)
    : IRequestHandler<GetCurrentAtprotoOAuthSessionQuery, AtprotoCurrentOAuthSession?>
{
    public async Task<AtprotoCurrentOAuthSession?> Handle(
        GetCurrentAtprotoOAuthSessionQuery request,
        CancellationToken cancellationToken)
    {
        await new AtprotoCurrentSessionIdentityValidator()
            .ValidateAndThrowAsync(request.Identity, cancellationToken).ConfigureAwait(false);
        return await securityGateway.GetCurrentAsync(request.Identity, cancellationToken).ConfigureAwait(false);
    }
}
