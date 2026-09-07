using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Validators;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Commands;

public sealed class RefreshAtprotoSessionCommandHandler(
    IAtprotoOAuthSecurityGateway securityGateway,
    IAtprotoSessionTokenIssuer tokenIssuer)
    : IRequestHandler<RefreshAtprotoSessionCommand, AtprotoSessionRefreshResult>
{
    public async Task<AtprotoSessionRefreshResult> Handle(
        RefreshAtprotoSessionCommand request,
        CancellationToken cancellationToken)
    {
        await new AtprotoCurrentSessionIdentityValidator()
            .ValidateAndThrowAsync(request.Identity, cancellationToken).ConfigureAwait(false);
        var refresh = await securityGateway
            .RefreshAsync(request.Identity, cancellationToken).ConfigureAwait(false);
        if (!refresh.Success)
        {
            return AtprotoSessionRefreshResult.Failed(refresh.FailureCode);
        }

        var issued = await tokenIssuer.IssueAsync(
            request.Identity.UserId,
            request.Identity.TenantId,
            request.Identity.Did,
            cancellationToken).ConfigureAwait(false);
        return AtprotoSessionRefreshResult.Succeeded(issued);
    }
}
