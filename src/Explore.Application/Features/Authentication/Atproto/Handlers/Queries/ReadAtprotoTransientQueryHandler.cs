// ABOUTME: Reads protected OAuth state before tenant recovery or a strictly tenant-bound handoff.
// ABOUTME: Maps immutable repository entities to private results and rejects disabled tenants without a filter bypass.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Application.Features.Authentication.Atproto.Validators;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Queries;

public sealed class ReadAtprotoTransientQueryHandler(IAtprotoTransientStoreRepository store,
    ITenantRepository tenants) : IRequestHandler<ReadAtprotoTransientQuery, AtprotoTransientValue?>
{
    public async Task<AtprotoTransientValue?> Handle(ReadAtprotoTransientQuery request, CancellationToken cancellationToken)
    {
        if (!(await new ReadAtprotoTransientQueryValidator().ValidateAsync(request, cancellationToken)).IsValid)
            return null;
        if (request.ExpectedTenantId is { } expectedTenant
            && await tenants.GetByIdAsNoTrackingAsync(expectedTenant, cancellationToken) is not { IsActive: true })
            return null;
        var row = request.ExpectedTenantId is { } tenantId
            ? await store.ReadAsync(request.Purpose, request.TokenDigest, tenantId, cancellationToken)
            : await store.ReadOAuthStateAsync(request.TokenDigest, cancellationToken);
        if (row?.TenantId is not { } rowTenant
            || (!request.ExpectedTenantId.HasValue
                && await tenants.GetByIdAsNoTrackingAsync(rowTenant, cancellationToken) is not { IsActive: true }))
            return null;
        return new(row.Id, row.Purpose, row.TokenDigest, rowTenant, row.ProtectedPayload, row.ExpiresAtUnixMilliseconds);
    }
}
