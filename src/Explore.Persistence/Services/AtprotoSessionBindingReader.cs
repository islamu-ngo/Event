using System.Collections.Immutable;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class AtprotoSessionBindingReader(ExploreDbContext context) : IAtprotoSessionBindingReader
{
    public async Task<ImmutableArray<AtprotoSessionBinding>> ReadAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (context.TenantFilterTenantId != tenantId)
            throw new InvalidOperationException("ATProto binding reads require the exact execution tenant.");
        var bindings = await context.UserAuthenticationTokens.AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.UserId == userId &&
                value.Provider == AuthenticationProviderDeploymentOptions.AtprotoProvider)
            .OrderBy(value => value.Id).Take(2)
            .Select(value => new AtprotoSessionBinding(value.SubjectDid, value.PdsHost))
            .ToListAsync(cancellationToken);
        return bindings.ToImmutableArray();
    }
}
