using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Queries;

/// <summary>
/// Handler for GetTenantNavLinksQuery.
/// Retrieves all navigation links for the current tenant, ordered by display order.
/// </summary>
public class GetTenantNavLinksQueryHandler : IRequestHandler<GetTenantNavLinksQuery, List<TenantNavigationLinkDto>>
{
    private readonly ITenantNavigationLinkRepository _navigationLinkRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IMapper _mapper;

    public GetTenantNavLinksQueryHandler(
        ITenantNavigationLinkRepository navigationLinkRepository,
        ITenantContext tenantContext,
        IMapper mapper)
    {
        _navigationLinkRepository = navigationLinkRepository;
        _tenantContext = tenantContext;
        _mapper = mapper;
    }

    public async Task<List<TenantNavigationLinkDto>> Handle(GetTenantNavLinksQuery request, CancellationToken cancellationToken)
    {
        // Get all navigation links for the current tenant, ordered by Order property
        var navigationLinks = await _navigationLinkRepository.GetByTenantIdOrderedAsync(
            _tenantContext.TenantId,
            cancellationToken);

        // Map to DTOs
        var dtos = _mapper.Map<List<TenantNavigationLinkDto>>(navigationLinks);

        return dtos;
    }
}
