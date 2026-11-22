using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Features.Announcements;

public sealed class GetMyAnnouncementDismissalQueryHandler(
    IPolicyMutationExecutionScopeFactory scopes, ITenantContext tenantContext)
    : IQueryHandler<GetMyAnnouncementDismissalQuery, AnnouncementDismissalState>
{
    public async Task<AnnouncementDismissalState> QueryAsync(
        GetMyAnnouncementDismissalQuery request, CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("A self announcement query requires its authenticated tenant.");
        await using var selected = await scopes.CreateAsync(new(tenantContext.TenantId), cancellationToken);
        return await selected.Services.GetRequiredService<AnnouncementEditionResolver>()
            .ResolveSelfAsync(cancellationToken);
    }
}
