// ABOUTME: Consumes one tenant-bound transient without a transaction wrapper or destructive retry.
// ABOUTME: Invalid, disabled, mismatched, expired and losing-race candidates share the same not-found result.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Validators;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Commands;

public sealed class ConsumeAtprotoTransientCommandHandler(IAtprotoTransientStoreRepository store,
    ITenantRepository tenants) : IRequestHandler<ConsumeAtprotoTransientCommand, AtprotoTransientCommandResult>
{
    public async Task<AtprotoTransientCommandResult> Handle(ConsumeAtprotoTransientCommand request, CancellationToken cancellationToken)
    {
        if (!(await new ConsumeAtprotoTransientCommandValidator().ValidateAsync(request, cancellationToken)).IsValid
            || await tenants.GetByIdAsNoTrackingAsync(request.ExpectedTenantId, cancellationToken) is not { IsActive: true })
            return AtprotoTransientCommandResult.Failure(BaseCommandResponse.NotFound<Guid>());
        var row = await store.ConsumeAsync(request.CandidateId, request.Purpose, request.TokenDigest,
            request.ExpectedTenantId, cancellationToken);
        return row is null
            ? AtprotoTransientCommandResult.Failure(BaseCommandResponse.NotFound<Guid>())
            : AtprotoTransientCommandResult.Success(new(row.Id, row.Purpose, row.TokenDigest, request.ExpectedTenantId,
                row.ProtectedPayload, row.ExpiresAtUnixMilliseconds));
    }
}
