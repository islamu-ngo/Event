using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Application.Features.Authentication.Atproto.Validators;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Queries;

public sealed class ReadAtprotoTransientQueryHandler(IAtprotoTransientStoreRepository store,
    ITenantRepository tenants) : IQueryHandler<ReadAtprotoTransientQuery, AtprotoTransientValue?>
{
    public async Task<AtprotoTransientValue?> QueryAsync(ReadAtprotoTransientQuery request, CancellationToken cancellationToken = default)
    {
        if (!(await new ReadAtprotoTransientQueryValidator().ValidateAsync(request, cancellationToken)).IsValid)
            return null;
        if (request.ExpectedTenantId is { } expectedTenant
            && await tenants.GetByIdAsNoTrackingAsync(expectedTenant, cancellationToken) is not { IsActive: true })
            return null;
        var row = request.ExpectedTenantId is { } tenantId
            ? await store.ReadAsync(request.Purpose, request.TokenDigest, tenantId, cancellationToken)
            : await store.ReadOAuthStateAsync(request.TokenDigest, cancellationToken);
        if (row is null || row.FlowKind != request.FlowKind)
            return null;
        switch (request.FlowKind)
        {
            case AtprotoAuthenticationFlowKind.Targeted:
                if (row.TenantId is not { } rowTenant || rowTenant == Guid.Empty
                    || (request.ExpectedTenantId.HasValue && rowTenant != request.ExpectedTenantId)
                    || (!request.ExpectedTenantId.HasValue
                        && await tenants.GetByIdAsNoTrackingAsync(rowTenant, cancellationToken) is not { IsActive: true }))
                    return null;
                break;
            case AtprotoAuthenticationFlowKind.UnscopedVerification:
                if (row.Purpose != AtprotoTransientPurpose.OAuthState || row.TenantId is not null)
                    return null;
                break;
            default:
                return null;
        }
        return new(row.Id, row.Purpose, row.TokenDigest, row.TenantId, row.ProtectedPayload,
            row.ExpiresAtUnixMilliseconds, request.FlowKind);
    }
}
