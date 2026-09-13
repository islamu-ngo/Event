using Explore.Application.Mappings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Footer.Handlers.Queries;

public sealed class GetFooterLinkGroupListQueryHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    ITenantContext tenantContext)
    : IQueryHandler<GetFooterLinkGroupListQuery, List<FooterLinkGroupListDto>>
{
    public async Task<List<FooterLinkGroupListDto>> QueryAsync(
        GetFooterLinkGroupListQuery request, CancellationToken cancellationToken)
    {
        var groups = await footerLinkGroupRepository.GetByTenantIdAsync(
            tenantContext.TenantId, cancellationToken);

        return groups.Select(FooterMapper.ToListItem).ToList();
    }
}
