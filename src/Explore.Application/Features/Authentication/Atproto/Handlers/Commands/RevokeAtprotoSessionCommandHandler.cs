using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Validators;
using FluentValidation;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Commands;

public sealed class RevokeAtprotoSessionCommandHandler(IAtprotoOAuthSecurityGateway securityGateway)
    : ICommandHandler<RevokeAtprotoSessionCommand, AtprotoSessionRevocationResult>
{
    public async Task<AtprotoSessionRevocationResult> ExecuteAsync(
        RevokeAtprotoSessionCommand request,
        CancellationToken cancellationToken = default)
    {
        await new AtprotoCurrentSessionIdentityValidator()
            .ValidateAndThrowAsync(request.Identity, cancellationToken).ConfigureAwait(false);
        return await securityGateway
            .RevokeCurrentAsync(request.Identity, cancellationToken).ConfigureAwait(false);
    }
}
