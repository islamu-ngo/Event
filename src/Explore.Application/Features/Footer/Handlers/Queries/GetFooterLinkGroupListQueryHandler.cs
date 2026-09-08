using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Footer.Handlers.Queries;

public sealed class GetFooterLinkGroupListQueryHandler(
    IFooterLinkGroupRepository footerLinkGroupRepository,
    ITenantContext tenantContext,
    IMapper mapper)
    : IRequestHandler<GetFooterLinkGroupListQuery, List<FooterLinkGroupListDto>>
{
    public async Task<List<FooterLinkGroupListDto>> Handle(
        GetFooterLinkGroupListQuery request, CancellationToken cancellationToken)
    {
        var groups = await footerLinkGroupRepository.GetByTenantIdAsync(
            tenantContext.TenantId, cancellationToken);

        return mapper.Map<List<FooterLinkGroupListDto>>(groups);
    }
}
