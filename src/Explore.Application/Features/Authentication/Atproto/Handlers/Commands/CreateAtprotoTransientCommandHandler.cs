// ABOUTME: Validates and creates immutable protected authentication state through the P1 repository.
// ABOUTME: Checks the enabled target tenant without setting ambient tenant or fabricating a user principal.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Commands;

public sealed class CreateAtprotoTransientCommandHandler(IAtprotoTransientStoreRepository store,
    ITenantRepository tenants, TimeProvider clock) : IRequestHandler<CreateAtprotoTransientCommand, AtprotoTransientCommandResult>
{
    public async Task<AtprotoTransientCommandResult> Handle(CreateAtprotoTransientCommand request, CancellationToken cancellationToken)
    {
        if (!(await new CreateAtprotoTransientCommandValidator(clock).ValidateAsync(request, cancellationToken)).IsValid)
            return AtprotoTransientCommandResult.Failure(BaseCommandResponse.Validation<Guid>(["Invalid transient request."]));
        if (await tenants.GetByIdAsNoTrackingAsync(request.TenantId, cancellationToken) is not { IsActive: true })
            return AtprotoTransientCommandResult.Failure(BaseCommandResponse.NotFound<Guid>());

        var row = AtprotoTransientRecord.Create(request.Purpose, request.TokenDigest, request.TenantId,
            request.ProtectedPayload, request.ExpiresAtUnixMilliseconds);
        return await store.TryCreateAsync(row, cancellationToken)
            ? AtprotoTransientCommandResult.Success(new(row.Id, row.Purpose, row.TokenDigest, request.TenantId,
                row.ProtectedPayload, row.ExpiresAtUnixMilliseconds))
            : AtprotoTransientCommandResult.Failure(BaseCommandResponse.Conflict(row.Id));
    }
}
