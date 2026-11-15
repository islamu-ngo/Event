using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Commands;

public sealed class CreateAtprotoTransientCommandHandler(IAtprotoTransientStoreRepository store,
    ITenantRepository tenants, TimeProvider clock) : ICommandHandler<CreateAtprotoTransientCommand, AtprotoTransientCommandResult>
{
    public async Task<AtprotoTransientCommandResult> ExecuteAsync(CreateAtprotoTransientCommand request, CancellationToken cancellationToken = default)
    {
        if (!(await new CreateAtprotoTransientCommandValidator(clock).ValidateAsync(request, cancellationToken)).IsValid)
            return AtprotoTransientCommandResult.Failure(BaseCommandResponse.Validation<Guid>(["Invalid transient request."]));
        if (request.TenantId is { } tenantId
            && await tenants.GetByIdAsNoTrackingAsync(tenantId, cancellationToken) is not { IsActive: true })
            return AtprotoTransientCommandResult.Failure(BaseCommandResponse.NotFound<Guid>());

        var target = new AtprotoAuthenticationFlowTarget(request.FlowKind, request.TenantId);
        var row = AtprotoTransientRecord.Create(request.Purpose, request.TokenDigest, target,
            request.ProtectedPayload, request.ExpiresAtUnixMilliseconds);
        return await store.TryCreateAsync(row, cancellationToken)
            ? AtprotoTransientCommandResult.Success(new(row.Id, row.Purpose, row.TokenDigest, row.TenantId,
                row.ProtectedPayload, row.ExpiresAtUnixMilliseconds, target.Kind))
            : AtprotoTransientCommandResult.Failure(BaseCommandResponse.Conflict(row.Id));
    }
}
